using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

// Read model for error-rate monitoring — one row per McpToolCall in the queried window.
public sealed record McpToolCallErrorStat(
    string ToolName,
    bool Success,
    ExecutionErrorCategory? ErrorCategory,
    DateTimeOffset CreatedAt
);
