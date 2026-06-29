namespace OrchestAI.Application.DTOs;

public sealed record AgentMessageDto(
    Guid Id,
    string Role,
    string Content,
    int SequenceOrder,
    DateTimeOffset CreatedAt
);
