namespace OrchestAI.Application.Queries.GetCostDashboard;

public sealed record CostBreakdownEntryDto(
    DateOnly Date,
    string AgentType,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int ExecutionCount,
    bool IsLive // true for today's entry, sourced from raw data rather than the rollup table
);

public sealed record GetCostDashboardResponse(
    DateOnly From,
    DateOnly To,
    decimal TotalCostUsd,
    int TotalExecutions,
    IReadOnlyList<CostBreakdownEntryDto> Breakdown
);
