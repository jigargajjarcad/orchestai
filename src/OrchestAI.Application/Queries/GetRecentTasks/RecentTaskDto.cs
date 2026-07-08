namespace OrchestAI.Application.Queries.GetRecentTasks;

public sealed record RecentTaskDto(
    Guid Id,
    string Title,
    string Status,
    decimal TotalCostUsd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt
);
