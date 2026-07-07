using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.DeleteAgentMemory;

public sealed class DeleteAgentMemoryHandler : IRequestHandler<DeleteAgentMemoryCommand, Unit>
{
    private readonly IAgentMemoryRepository _repository;

    public DeleteAgentMemoryHandler(IAgentMemoryRepository repository) => _repository = repository;

    public async Task<Unit> Handle(DeleteAgentMemoryCommand request, CancellationToken cancellationToken)
    {
        var memory = await _repository.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(AgentMemory), request.Id);

        await _repository.DeleteAsync(memory.Id, cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }
}
