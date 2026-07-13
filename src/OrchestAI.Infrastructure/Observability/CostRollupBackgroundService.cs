using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Observability;

// Rolls raw CostLedger rows up into daily CostRollup aggregates on a fixed interval — the
// background half of ADR-011's hybrid aggregation strategy. Dashboard reads (Week 7 task 33)
// hit CostRollup for anything before today and the raw tables directly for today/live runs,
// so this job trades a few minutes of staleness on historical rollups for fast dashboard reads.
public sealed class CostRollupBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    // Re-roll a trailing window on every pass so late-arriving writes (a task that finishes
    // mid-rollup, a resumed task from a prior day) still get picked up — recomputation is
    // idempotent (CostRollup.ReplaceValues), so re-rolling the same day is harmless.
    private static readonly int LookbackDays = 2;
    private static readonly int MaxCatchUpDays = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ILogger<CostRollupBackgroundService> _logger;

    public CostRollupBackgroundService(
        IServiceScopeFactory scopeFactory, ICurrentTenantAccessor tenantAccessor,
        ILogger<CostRollupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cost rollup background pass failed, will retry next interval");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // This job legitimately aggregates and writes every tenant's rows in one pass — the one
        // audited, narrow exception to normal tenant scoping. See ADR-014 confirmation #5b and
        // ICurrentTenantAccessor.IsSystemWriteScope.
        using var systemScope = _tenantAccessor.BeginSystemWriteScope();

        using var scope = _scopeFactory.CreateScope();
        var costLedgerRepository = scope.ServiceProvider.GetRequiredService<ICostLedgerRepository>();
        var costRollupRepository = scope.ServiceProvider.GetRequiredService<ICostRollupRepository>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastRolledUp = await costRollupRepository
            .GetLastRolledUpDateAsync(cancellationToken)
            .ConfigureAwait(false);

        var earliestCatchUp = today.AddDays(-MaxCatchUpDays);
        var from = lastRolledUp is { } last
            ? Max(last.AddDays(-LookbackDays), earliestCatchUp)
            : earliestCatchUp;

        var aggregates = await costLedgerRepository
            .GetDailyAggregatesForRollupAsync(from, today, cancellationToken)
            .ConfigureAwait(false);

        foreach (var aggregate in aggregates)
        {
            var rollup = CostRollup.Create(
                aggregate.Date, aggregate.TenantId, aggregate.UserId, aggregate.AgentType, aggregate.Model,
                aggregate.InputTokens, aggregate.OutputTokens, aggregate.CostUsd, aggregate.ExecutionCount);
            await costRollupRepository.UpsertAsync(rollup, cancellationToken).ConfigureAwait(false);
        }

        if (aggregates.Count > 0)
            _logger.LogInformation(
                "Cost rollup pass upserted {Count} aggregate rows for {From}..{To}",
                aggregates.Count, from, today);
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
