namespace OrchestAI.Domain.Models;

// TenantLimits with every nullable field resolved against TenantLimitsDefaults — the only
// shape any of the five enforcement points should ever read. See ITenantLimitsProvider.
public sealed record ResolvedTenantLimits(
    int RequestsPerMinute,
    int MaxConcurrentTasks,
    int MaxAgentsPerTask,
    int MaxToolCallsPerTask,
    decimal DailyCostBudgetUsd,
    decimal MonthlyCostBudgetUsd,
    int MaxQueueDepth);
