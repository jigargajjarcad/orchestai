namespace OrchestAI.Application.Commands.CreateOrchestrationTask;

public sealed record CreateOrchestrationTaskResponse(
    Guid Id,
    Guid UserId,
    string Title,
    string Status,
    bool RequireApproval,
    DateTimeOffset CreatedAt
);
