using MediatR;

namespace OrchestAI.Application.Commands.SetTenantLimits;

public sealed record SetTenantLimitsCommand(
    Guid TenantId,
    int? RequestsPerMinute,
    int? MaxConcurrentTasks,
    int? MaxAgentsPerTask,
    int? MaxToolCallsPerTask,
    decimal? DailyCostBudgetUsd,
    decimal? MonthlyCostBudgetUsd,
    int? MaxQueueDepth
) : IRequest<SetTenantLimitsResponse>;
