using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface ITaskCheckpointRepository
{
    Task<IReadOnlyList<TaskCheckpoint>> GetByTaskIdAsync(
        Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Upsert by (OrchestrationTaskId, AgentType) — one checkpoint per agent per task.</summary>
    Task UpsertAsync(
        TaskCheckpoint checkpoint, CancellationToken cancellationToken = default);

    Task DeleteByTaskIdAsync(
        Guid taskId, CancellationToken cancellationToken = default);
}
