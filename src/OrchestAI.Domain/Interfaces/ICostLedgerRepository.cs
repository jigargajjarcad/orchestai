using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface ICostLedgerRepository
{
    Task AddAsync(CostLedger ledger, CancellationToken cancellationToken = default);

    // Grouped by (Date, UserId, AgentType, Model) across [from, to] inclusive (UTC calendar days) —
    // the input to the background cost rollup job. See ADR-011.
    Task<IReadOnlyList<CostLedgerAggregate>> GetDailyAggregatesAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    // Raw per-execution cost entries for one task — used by the execution summary card to
    // report the provider/model actually used per agent (not the current config, the actual one).
    Task<IReadOnlyList<CostLedger>> GetByTaskIdAsync(
        Guid orchestrationTaskId, CancellationToken cancellationToken = default);
}
