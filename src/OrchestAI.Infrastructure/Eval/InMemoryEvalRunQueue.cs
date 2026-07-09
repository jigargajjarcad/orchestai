using System.Threading.Channels;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Eval;

// Same primitive InMemoryOrchestrationEventBus uses (System.Threading.Channels) — an
// unbounded, single-process work queue. Good enough for Week 8's "don't run scoring inline
// with the HTTP request" requirement; a durable queue is out of scope until eval volume
// justifies surviving a process restart.
public sealed class InMemoryEvalRunQueue : IEvalRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public async Task EnqueueAsync(Guid evalRunId, CancellationToken cancellationToken = default) =>
        await _channel.Writer.WriteAsync(evalRunId, cancellationToken).ConfigureAwait(false);

    public async Task<Guid> DequeueAsync(CancellationToken cancellationToken = default) =>
        await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
}
