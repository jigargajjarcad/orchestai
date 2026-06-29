using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IOrchestrationTaskRepository
{
    Task<OrchestrationTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrchestrationTask?> GetByIdWithExecutionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrchestrationTask?> GetByIdWithExecutionsAndMessagesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrchestrationTask?> GetByIdWithExecutionsMessagesAndToolCallsAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(OrchestrationTask task, CancellationToken cancellationToken = default);
    Task UpdateAsync(OrchestrationTask task, CancellationToken cancellationToken = default);
}
