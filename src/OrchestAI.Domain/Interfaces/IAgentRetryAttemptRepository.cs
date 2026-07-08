using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IAgentRetryAttemptRepository
{
    Task AddAsync(AgentRetryAttempt attempt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentRetryAttempt>> GetByAgentExecutionIdAsync(
        Guid agentExecutionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentRetryAttempt>> GetByAgentExecutionIdsAsync(
        IReadOnlyCollection<Guid> agentExecutionIds, CancellationToken cancellationToken = default);
}
