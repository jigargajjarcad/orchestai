using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.SetTenantLimits;

// Admin/bootstrap-only — same access pattern as Week 10's CreateTenantCommand/CreateApiKeyCommand,
// reachable only through AdminController's RequireAdminSecretFilter gate, never a tenant key.
public sealed class SetTenantLimitsHandler : IRequestHandler<SetTenantLimitsCommand, SetTenantLimitsResponse>
{
    private readonly ITenantLimitsRepository _limitsRepository;
    private readonly ITenantRepository _tenantRepository;

    public SetTenantLimitsHandler(ITenantLimitsRepository limitsRepository, ITenantRepository tenantRepository)
    {
        _limitsRepository = limitsRepository;
        _tenantRepository = tenantRepository;
    }

    public async Task<SetTenantLimitsResponse> Handle(SetTenantLimitsCommand request, CancellationToken cancellationToken)
    {
        _ = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

        var existing = await _limitsRepository.GetByTenantIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false);

        TenantLimits limits;
        if (existing is null)
        {
            limits = TenantLimits.Create(
                request.TenantId, request.RequestsPerMinute, request.MaxConcurrentTasks, request.MaxAgentsPerTask,
                request.MaxToolCallsPerTask, request.DailyCostBudgetUsd, request.MonthlyCostBudgetUsd,
                request.MaxQueueDepth);
        }
        else
        {
            existing.Update(
                request.RequestsPerMinute, request.MaxConcurrentTasks, request.MaxAgentsPerTask,
                request.MaxToolCallsPerTask, request.DailyCostBudgetUsd, request.MonthlyCostBudgetUsd,
                request.MaxQueueDepth);
            limits = existing;
        }

        await _limitsRepository.UpsertAsync(limits, cancellationToken).ConfigureAwait(false);

        return new SetTenantLimitsResponse(
            limits.TenantId, limits.RequestsPerMinute, limits.MaxConcurrentTasks, limits.MaxAgentsPerTask,
            limits.MaxToolCallsPerTask, limits.DailyCostBudgetUsd, limits.MonthlyCostBudgetUsd, limits.MaxQueueDepth,
            limits.UpdatedAt);
    }
}
