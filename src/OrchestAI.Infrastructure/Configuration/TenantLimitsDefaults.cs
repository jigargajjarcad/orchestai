namespace OrchestAI.Infrastructure.Configuration;

// System-wide fallback values for any tenant without an explicit TenantLimits row, or with a
// null field on that row. The single source of truth for "what happens when nothing is
// configured" — see DESIGN_PRINCIPLES.md "Single-choke-point enforcement" and ADR-015.
public sealed class TenantLimitsDefaults
{
    public const string SectionName = "TenantLimitsDefaults";

    public int RequestsPerMinute { get; init; } = 120;
    public int MaxConcurrentTasks { get; init; } = 5;
    public int MaxAgentsPerTask { get; init; } = 5;
    public int MaxToolCallsPerTask { get; init; } = 50;
    public decimal DailyCostBudgetUsd { get; init; } = 50m;
    public decimal MonthlyCostBudgetUsd { get; init; } = 500m;
    public int MaxQueueDepth { get; init; } = 100;
}
