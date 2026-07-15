using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.AdmitOrchestrationTask;

// The synchronous admission step inserted into POST /tasks/{id}/start, run and awaited by the
// controller BEFORE the background dispatch (Task 7) — this is what makes a 429 possible for
// concurrency/budget rejections, since the existing dispatch runs fire-and-forget after the
// HTTP response is already written. See Task 7's controller changes and ADR-015.
public sealed class AdmitOrchestrationTaskHandler
    : IRequestHandler<AdmitOrchestrationTaskCommand, AdmitOrchestrationTaskResponse>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly IOrchestrationAdmissionRepository _admissionRepository;
    private readonly ITenantLimitsProvider _limitsProvider;
    private readonly IBudgetEstimator _budgetEstimator;
    private readonly ICostRollupRepository _costRollupRepository;
    private readonly ICostLedgerRepository _costLedgerRepository;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly IOptions<AbuseProtectionOptions> _abuseProtectionOptions;
    private readonly ILogger<AdmitOrchestrationTaskHandler> _logger;

    public AdmitOrchestrationTaskHandler(
        IOrchestrationTaskRepository taskRepository,
        IOrchestrationAdmissionRepository admissionRepository,
        ITenantLimitsProvider limitsProvider,
        IBudgetEstimator budgetEstimator,
        ICostRollupRepository costRollupRepository,
        ICostLedgerRepository costLedgerRepository,
        ICurrentTenantAccessor tenantAccessor,
        IOptions<AbuseProtectionOptions> abuseProtectionOptions,
        ILogger<AdmitOrchestrationTaskHandler> logger)
    {
        _taskRepository = taskRepository;
        _admissionRepository = admissionRepository;
        _limitsProvider = limitsProvider;
        _budgetEstimator = budgetEstimator;
        _costRollupRepository = costRollupRepository;
        _costLedgerRepository = costLedgerRepository;
        _tenantAccessor = tenantAccessor;
        _abuseProtectionOptions = abuseProtectionOptions;
        _logger = logger;
    }

    public async Task<AdmitOrchestrationTaskResponse> Handle(
        AdmitOrchestrationTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

        var tenantId = _tenantAccessor.TenantId
            ?? throw new InvalidOperationException("AdmitOrchestrationTaskHandler ran with no ambient tenant.");

        var limits = await _limitsProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var reservationAmount = await _budgetEstimator
            .EstimateWorstCaseCostAsync(limits, cancellationToken).ConfigureAwait(false);
        var (actualDailySpend, actualMonthlySpend) = await ReadActualSpendAsync(cancellationToken).ConfigureAwait(false);
        var staleness = TimeSpan.FromMinutes(_abuseProtectionOptions.Value.ReservationStalenessMinutes);

        var result = await _admissionRepository.TryAdmitAsync(
            task.Id, tenantId, limits.MaxConcurrentTasks, reservationAmount,
            limits.DailyCostBudgetUsd, actualDailySpend, limits.MonthlyCostBudgetUsd, actualMonthlySpend,
            staleness, cancellationToken).ConfigureAwait(false);

        if (!result.Admitted)
        {
            if (result.FailureReason == AdmissionFailureReason.TaskNotPending)
                throw new ConflictException($"Task {task.Id} is not in a startable state.");

            var reason = result.FailureReason == AdmissionFailureReason.ConcurrencyExceeded
                ? RejectionReason.ConcurrencyExceeded
                : RejectionReason.BudgetExceeded;
            var retryAfterSeconds = reason == RejectionReason.ConcurrencyExceeded ? 30 : 3600;
            var detail = reason == RejectionReason.ConcurrencyExceeded
                ? $"Tenant has reached its concurrent task limit ({limits.MaxConcurrentTasks})."
                : "Tenant cost budget would be exceeded by this task.";

            _logger.LogWarning(
                "Admission rejected for task {TaskId}, tenant {TenantId}: {Reason} — {Details}",
                task.Id, tenantId, reason, result.DetailsJson);

            throw new TenantLimitExceededException(
                tenantId, reason, detail, retryAfterSeconds, result.DetailsJson ?? "{}", task.TraceId);
        }

        _logger.LogInformation(
            "Task {TaskId} admitted for tenant {TenantId}, reserved ${Amount:F4}", task.Id, tenantId, reservationAmount);

        return new AdmitOrchestrationTaskResponse(task.Id);
    }

    // Reuses ADR-011's hybrid rollup+live-ledger read pattern (GetCostDashboardHandler) unchanged,
    // applied tenant-wide (userId: null) rather than to one user's slice — see Task 6's investigation
    // note. Deliberately not modifying GetCostDashboardHandler itself.
    private async Task<(decimal Daily, decimal Monthly)> ReadActualSpendAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var todayAggregates = await _costLedgerRepository
            .GetDailyAggregatesAsync(today, today, cancellationToken).ConfigureAwait(false);
        var actualDaily = todayAggregates.Sum(a => a.CostUsd);

        var actualMonthly = actualDaily;
        if (monthStart < today)
        {
            var monthRollups = await _costRollupRepository
                .GetByDateRangeAsync(monthStart, today.AddDays(-1), userId: null, cancellationToken)
                .ConfigureAwait(false);
            actualMonthly += monthRollups.Sum(r => r.CostUsd);
        }

        return (actualDaily, actualMonthly);
    }
}
