using System.Collections.Concurrent;
using System.Threading.Channels;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Eval;

// Same underlying primitive as before (System.Threading.Channels) — one global unbounded
// channel, shared by live-suite eval runs and post-hoc scoring alike, which is what makes
// per-tenant FIFO ordering automatic (a subsequence of one FIFO sequence is FIFO, with no
// reordering possible). What's new in Week 11 is a per-tenant depth counter: EnqueueAsync
// checks it against TenantLimits.MaxQueueDepth before writing, DequeueAsync decrements it once
// EvalRunBackgroundWorker claims the item — queue depth means "waiting," a different concern
// from MaxConcurrentTasks (concurrent task EXECUTION). See ADR-015 for the accepted
// check-then-increment race tradeoff (not a CAS like the admission transaction — the failure
// direction here is "very slightly over capacity for one item," not a cost/security overshoot).
public sealed class InMemoryEvalRunQueue : IEvalRunQueue
{
    private readonly Channel<EvalRunQueueItem> _channel = Channel.CreateUnbounded<EvalRunQueueItem>();
    private readonly ConcurrentDictionary<Guid, int> _depthByTenant = new();
    private readonly ITenantLimitsProvider _limitsProvider;

    public InMemoryEvalRunQueue(ITenantLimitsProvider limitsProvider) => _limitsProvider = limitsProvider;

    public async Task EnqueueAsync(Guid evalRunId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var limits = await _limitsProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var currentDepth = _depthByTenant.GetOrAdd(tenantId, 0);

        if (currentDepth >= limits.MaxQueueDepth)
        {
            var detailsJson = $$"""{"limit":{{limits.MaxQueueDepth}},"actual":{{currentDepth}},"queueDepth":{{currentDepth}}}""";
            throw new TenantLimitExceededException(
                tenantId, RejectionReason.QueueBackpressure,
                "Tenant background-work queue is at capacity — try again later.",
                retryAfterSeconds: 30, detailsJson: detailsJson);
        }

        _depthByTenant.AddOrUpdate(tenantId, 1, (_, current) => current + 1);
        await _channel.Writer.WriteAsync(new EvalRunQueueItem(evalRunId, tenantId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<EvalRunQueueItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var item = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        _depthByTenant.AddOrUpdate(item.TenantId, 0, (_, current) => Math.Max(0, current - 1));
        return item;
    }
}
