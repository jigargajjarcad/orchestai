using MediatR;
using OrchestAI.Application.DTOs;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Queries.GetUserMemories;

public sealed record GetUserMemoriesQuery(
    Guid UserId,
    AgentType? AgentType = null
) : IRequest<IReadOnlyList<AgentMemoryDto>>;
