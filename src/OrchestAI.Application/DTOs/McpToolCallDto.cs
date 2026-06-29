namespace OrchestAI.Application.DTOs;

public sealed record McpToolCallDto(
    Guid Id,
    string ToolName,
    bool Success,
    int? DurationMs,
    string InputParameters,
    string? OutputPreview,
    string? ErrorMessage,
    DateTimeOffset CreatedAt
);
