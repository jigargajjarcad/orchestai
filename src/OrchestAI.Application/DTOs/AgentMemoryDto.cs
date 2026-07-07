namespace OrchestAI.Application.DTOs;

public sealed record AgentMemoryDto(
    Guid Id,
    Guid UserId,
    string AgentType,
    string Key,
    string Value,
    int Importance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt
);
