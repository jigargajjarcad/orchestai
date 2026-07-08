using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

// Read model for error-rate monitoring — one row per AgentExecution in the queried window.
// Aggregation (rates, category buckets, time buckets) happens in the query handler.
public sealed record AgentExecutionErrorStat(
    Guid AgentExecutionId,
    AgentType AgentType,
    ExecutionStatus Status,
    ExecutionErrorCategory? ErrorCategory,
    DateTimeOffset CreatedAt
);
