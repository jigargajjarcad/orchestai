namespace OrchestAI.Domain.Models;

public sealed record AgentExecutionResult(
    Guid AgentExecutionId,
    string Output,
    bool Success,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    string? ErrorMessage = null,
    string SpanId = ""
);
