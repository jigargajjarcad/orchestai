namespace OrchestAI.Application.Queries.GetExecutionSummary;

public sealed record GetExecutionSummaryResponse(
    Guid TaskId,
    string Status,
    double? DurationSeconds,
    decimal TotalCostUsd,
    int TotalInputTokens,
    int TotalOutputTokens,
    IReadOnlyList<string> AgentsInvolved,
    int ToolCallCount,
    IReadOnlyList<string> ProvidersAndModels,
    int RetryCount,
    int ErrorCount,
    bool MemoryUsed,
    bool CheckpointRestored
);
