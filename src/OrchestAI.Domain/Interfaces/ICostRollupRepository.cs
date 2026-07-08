using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface ICostRollupRepository
{
    /// <summary>Upsert by (Date, UserId, AgentType, Model) — recomputed, not accumulated.</summary>
    Task UpsertAsync(CostRollup rollup, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CostRollup>> GetByDateRangeAsync(
        DateOnly from, DateOnly to, Guid? userId = null, CancellationToken cancellationToken = default);

    /// <summary>Latest date with at least one rollup row, or null if the table is empty.</summary>
    Task<DateOnly?> GetLastRolledUpDateAsync(CancellationToken cancellationToken = default);
}
