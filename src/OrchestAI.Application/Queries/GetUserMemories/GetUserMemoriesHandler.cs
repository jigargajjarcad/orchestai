using MediatR;
using OrchestAI.Application.DTOs;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetUserMemories;

public sealed class GetUserMemoriesHandler
    : IRequestHandler<GetUserMemoriesQuery, IReadOnlyList<AgentMemoryDto>>
{
    private readonly IAgentMemoryRepository _repository;

    public GetUserMemoriesHandler(IAgentMemoryRepository repository) => _repository = repository;

    public async Task<IReadOnlyList<AgentMemoryDto>> Handle(
        GetUserMemoriesQuery request, CancellationToken cancellationToken)
    {
        var memories = request.AgentType.HasValue
            ? await _repository.GetAllForUserAndAgentTypeAsync(request.UserId, request.AgentType.Value, cancellationToken)
                .ConfigureAwait(false)
            : await _repository.GetAllForUserAsync(request.UserId, cancellationToken).ConfigureAwait(false);

        return memories
            .Select(m => new AgentMemoryDto(
                m.Id, m.UserId, m.AgentType.ToString(), m.Key, m.Value,
                m.Importance, m.CreatedAt, m.UpdatedAt, m.ExpiresAt))
            .ToList();
    }
}
