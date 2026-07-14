using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface ICostLedgerRepository
{
    Task AddAsync(CostLedger ledger, CancellationToken cancellationToken = default);

    // Grouped by (Date, UserId, AgentType, Model) across [from, to] inclusive (UTC calendar days),
    // scoped to the ambient tenant via the normal query filter — the live dashboard's "today"
    // query. See ADR-011.
    Task<IReadOnlyList<CostLedgerAggregate>> GetDailyAggregatesAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    // Cross-tenant by design — the ONLY caller is CostRollupBackgroundService, inside its
    // BeginSystemWriteScope(). Bypasses the tenant query filter deliberately via
    // IgnoreQueryFilters() and asserts it's only ever invoked from within that scope (defense in
    // depth — a future accidental call from tenant-facing code fails loudly instead of silently
    // leaking cross-tenant data). Grouped by (Date, TenantId, UserId, AgentType, Model) across
    // [from, to] inclusive (UTC calendar days). See ADR-014 confirmation #5b.
    Task<IReadOnlyList<CostLedgerAggregate>> GetDailyAggregatesForRollupAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    // Raw per-execution cost entries for one task — used by the execution summary card to
    // report the provider/model actually used per agent (not the current config, the actual one).
    Task<IReadOnlyList<CostLedger>> GetByTaskIdAsync(
        Guid orchestrationTaskId, CancellationToken cancellationToken = default);
}
