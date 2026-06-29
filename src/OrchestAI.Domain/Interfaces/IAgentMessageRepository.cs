using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IAgentMessageRepository
{
    Task AddAsync(AgentMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentMessage>> GetByExecutionIdAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);
}
