using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Events;

public sealed class InMemoryOrchestrationEventBus : IOrchestrationEventBus
{
    private readonly ConcurrentDictionary<Guid, Channel<SseEvent>> _channels = new();

    private Channel<SseEvent> GetOrCreateChannel(Guid taskId)
        => _channels.GetOrAdd(taskId, _ => Channel.CreateBounded<SseEvent>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            }));

    public void Publish(Guid taskId, SseEvent sseEvent)
    {
        var channel = GetOrCreateChannel(taskId);
        channel.Writer.TryWrite(sseEvent);

        if (sseEvent.Event is "task_completed" or "task_failed")
            channel.Writer.TryComplete();
    }

    public async IAsyncEnumerable<SseEvent> SubscribeAsync(
        Guid taskId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = GetOrCreateChannel(taskId);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return evt;
        }

        _channels.TryRemove(taskId, out _);
    }
}
