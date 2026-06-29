using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IAgentExecutionRepository
{
    Task<AgentExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(AgentExecution execution, CancellationToken cancellationToken = default);
    Task UpdateAsync(AgentExecution execution, CancellationToken cancellationToken = default);
}
