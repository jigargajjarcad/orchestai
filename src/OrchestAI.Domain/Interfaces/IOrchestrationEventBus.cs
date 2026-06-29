using OrchestAI.Domain.Events;

namespace OrchestAI.Domain.Interfaces;

public interface IOrchestrationEventBus
{
    void Publish(Guid taskId, SseEvent sseEvent);
    IAsyncEnumerable<SseEvent> SubscribeAsync(Guid taskId, CancellationToken cancellationToken);
}
