namespace OrchestAI.Application.DTOs;

public sealed record AgentExecutionDto(
    Guid Id,
    string AgentType,
    string Status,
    string InputPrompt,
    string? OutputResult,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AgentMessageDto>? Messages = null,
    IReadOnlyList<McpToolCallDto>? ToolCalls = null
);
