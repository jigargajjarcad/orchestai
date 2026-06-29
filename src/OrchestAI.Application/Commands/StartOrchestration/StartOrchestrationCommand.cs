using MediatR;

namespace OrchestAI.Application.Commands.StartOrchestration;

public sealed record StartOrchestrationCommand(Guid TaskId) : IRequest<StartOrchestrationResponse>;
