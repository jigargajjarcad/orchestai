namespace OrchestAI.Application.Queries.GetTaskComparison;

public sealed record ComparisonAgentExecutionDto(
    string AgentType,
    string Status,
    string InputPrompt,
    string? OutputResult,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int? DurationMs
);

public sealed record TaskComparisonSideDto(
    Guid TaskId,
    string UserPrompt,
    string Status,
    string? FinalResult,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalCostUsd,
    double? DurationSeconds,
    IReadOnlyList<ComparisonAgentExecutionDto> Executions
);

public sealed record GetTaskComparisonResponse(
    TaskComparisonSideDto First,
    TaskComparisonSideDto Second
);
