namespace OrchestAI.Domain.Interfaces;

public interface IEvalRunQueue
{
    Task EnqueueAsync(Guid evalRunId, CancellationToken cancellationToken = default);
    Task<Guid> DequeueAsync(CancellationToken cancellationToken = default);
}
