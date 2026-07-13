using System.Threading.Channels;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Eval;

// Same primitive InMemoryOrchestrationEventBus uses (System.Threading.Channels) — an
// unbounded, single-process work queue. Good enough for Week 8's "don't run scoring inline
// with the HTTP request" requirement; a durable queue is out of scope until eval volume
// justifies surviving a process restart.
public sealed class InMemoryEvalRunQueue : IEvalRunQueue
{
    private readonly Channel<EvalRunQueueItem> _channel = Channel.CreateUnbounded<EvalRunQueueItem>();

    public async Task EnqueueAsync(Guid evalRunId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(new EvalRunQueueItem(evalRunId, tenantId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<EvalRunQueueItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
}
