using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

// Read model produced by ICostLedgerRepository.GetDailyAggregatesAsync — the raw material
// the background rollup job upserts into CostRollup rows. See ADR-011.
public sealed record CostLedgerAggregate(
    DateOnly Date,
    Guid UserId,
    AgentType AgentType,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int ExecutionCount
);
