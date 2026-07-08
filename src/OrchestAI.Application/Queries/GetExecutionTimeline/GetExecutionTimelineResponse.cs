namespace OrchestAI.Application.Queries.GetExecutionTimeline;

// One row per AgentExecution or McpToolCall in the task. The frontend reconstructs the
// nested/Gantt tree from SpanId/ParentSpanId — never from timestamp ordering (ADR-011).
public sealed record TimelineSpanDto(
    string SpanId,
    string? ParentSpanId,
    string SpanType, // "AgentExecution" | "ToolCall"
    string Label, // AgentType for executions, ToolName for tool calls
    string Status,
    string? ErrorCategory,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? DurationMs,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd
);

public sealed record GetExecutionTimelineResponse(
    Guid TaskId,
    string TraceId,
    IReadOnlyList<TimelineSpanDto> Spans
);
