using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Interfaces;

public interface IAgentMemoryRepository
{
    /// <summary>Returns top N non-expired memories for the user/agent, ordered by Importance DESC.</summary>
    Task<IReadOnlyList<AgentMemory>> GetRelevantAsync(
        Guid userId,
        AgentType agentType,
        int maxEntries = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentMemory>> GetAllForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentMemory>> GetAllForUserAndAgentTypeAsync(
        Guid userId, AgentType agentType, CancellationToken cancellationToken = default);

    Task<AgentMemory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Upsert by (UserId, AgentType, Key).</summary>
    Task UpsertAsync(AgentMemory memory, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteExpiredAsync(CancellationToken cancellationToken = default);
}
