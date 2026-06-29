using OrchestAI.Application.DTOs;

namespace OrchestAI.Application.Queries.GetOrchestrationTask;

public sealed record GetOrchestrationTaskResponse(
    Guid Id,
    Guid UserId,
    string Title,
    string UserPrompt,
    string Status,
    string? FinalResult,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalCostUsd,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<AgentExecutionDto> AgentExecutions
);
