using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

// Read model produced by ICostLedgerRepository.GetDailyAggregatesAsync (tenant-filtered, for
// the live dashboard's "today" query) and GetDailyAggregatesForRollupAsync (cross-tenant, for
// the background rollup job) — the raw material upserted into CostRollup rows. See ADR-011.
// TenantId is Guid.Empty (unused/ignored) when produced by GetDailyAggregatesAsync's
// tenant-filtered query — only GetDailyAggregatesForRollupAsync populates a real TenantId
// per row, since only that cross-tenant path needs it.
public sealed record CostLedgerAggregate(
    DateOnly Date,
    Guid TenantId,
    Guid UserId,
    AgentType AgentType,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int ExecutionCount
);
