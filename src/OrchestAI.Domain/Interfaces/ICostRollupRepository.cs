using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface ICostRollupRepository
{
    /// <summary>
    /// Upsert by (Date, TenantId, UserId, AgentType, Model) — recomputed, not accumulated. Cross-tenant
    /// by design — the ONLY caller is CostRollupBackgroundService, inside its BeginSystemWriteScope().
    /// Bypasses the tenant query filter deliberately via IgnoreQueryFilters() (the ambient TenantId is
    /// null in that scope, so the normal filter would never find the existing row to update) and
    /// asserts it's only ever invoked from within that scope. See ADR-014 confirmation #5b.
    /// </summary>
    Task UpsertAsync(CostRollup rollup, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CostRollup>> GetByDateRangeAsync(
        DateOnly from, DateOnly to, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest date with at least one rollup row across ALL tenants, or null if the table is empty.
    /// Cross-tenant by design — same system-write-scope guard and IgnoreQueryFilters() rationale as
    /// UpsertAsync above; the ONLY caller is CostRollupBackgroundService.
    /// </summary>
    Task<DateOnly?> GetLastRolledUpDateAsync(CancellationToken cancellationToken = default);
}
