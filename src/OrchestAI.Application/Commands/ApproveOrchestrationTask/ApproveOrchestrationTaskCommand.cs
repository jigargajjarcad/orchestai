using MediatR;

namespace OrchestAI.Application.Commands.ApproveOrchestrationTask;

public sealed record ApproveOrchestrationTaskCommand(
    Guid TaskId,
    string? Note = null
) : IRequest<Unit>;
