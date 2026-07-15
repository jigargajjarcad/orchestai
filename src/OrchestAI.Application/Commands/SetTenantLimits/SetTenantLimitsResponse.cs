namespace OrchestAI.Application.Commands.SetTenantLimits;

public sealed record SetTenantLimitsResponse(
    Guid TenantId,
    int? RequestsPerMinute,
    int? MaxConcurrentTasks,
    int? MaxAgentsPerTask,
    int? MaxToolCallsPerTask,
    decimal? DailyCostBudgetUsd,
    decimal? MonthlyCostBudgetUsd,
    int? MaxQueueDepth,
    DateTimeOffset UpdatedAt);
