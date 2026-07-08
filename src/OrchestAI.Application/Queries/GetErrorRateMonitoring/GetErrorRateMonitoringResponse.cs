namespace OrchestAI.Application.Queries.GetErrorRateMonitoring;

public sealed record AgentErrorRateDto(
    string AgentType,
    int TotalExecutions,
    int FailedExecutions,
    double FailureRate,
    IReadOnlyDictionary<string, int> FailuresByCategory,
    int RetryCount
);

public sealed record ToolErrorRateDto(
    string ToolName,
    int TotalCalls,
    int FailedCalls,
    double FailureRate,
    IReadOnlyDictionary<string, int> FailuresByCategory
);

public sealed record GetErrorRateMonitoringResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<AgentErrorRateDto> AgentErrorRates,
    IReadOnlyList<ToolErrorRateDto> ToolErrorRates
);
