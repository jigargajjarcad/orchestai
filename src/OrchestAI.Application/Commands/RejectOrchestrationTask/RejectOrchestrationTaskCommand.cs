using MediatR;

namespace OrchestAI.Application.Commands.RejectOrchestrationTask;

public sealed record RejectOrchestrationTaskCommand(
    Guid TaskId,
    string? Note = null
) : IRequest<Unit>;
