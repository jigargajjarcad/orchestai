using MediatR;

namespace OrchestAI.Application.Commands.CreateOrchestrationTask;

public sealed record CreateOrchestrationTaskCommand(
    Guid UserId,
    string Title,
    string UserPrompt
) : IRequest<CreateOrchestrationTaskResponse>;
