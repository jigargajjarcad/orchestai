using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalRunQueue
{
    Task EnqueueAsync(Guid evalRunId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<EvalRunQueueItem> DequeueAsync(CancellationToken cancellationToken = default);
}
