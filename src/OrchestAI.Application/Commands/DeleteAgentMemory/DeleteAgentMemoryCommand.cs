using MediatR;

namespace OrchestAI.Application.Commands.DeleteAgentMemory;

public sealed record DeleteAgentMemoryCommand(Guid Id) : IRequest<Unit>;
