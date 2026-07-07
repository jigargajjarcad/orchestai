using MediatR;

namespace OrchestAI.Application.Commands.ResumeOrchestrationTask;

public sealed record ResumeOrchestrationTaskCommand(Guid TaskId) : IRequest<ResumeOrchestrationTaskResponse>;
