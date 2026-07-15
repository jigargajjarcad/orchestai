using MediatR;

namespace OrchestAI.Application.Commands.AdmitOrchestrationTask;

public sealed record AdmitOrchestrationTaskCommand(Guid TaskId) : IRequest<AdmitOrchestrationTaskResponse>;
