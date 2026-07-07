using System.Collections.Concurrent;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Events;

// In-memory per-taskId signal — works for a single Railway instance. See DECISIONS.md ADR-004.
public sealed class InMemoryApprovalGateway : IApprovalGateway
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public async Task WaitForApprovalAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(taskId, _ => new SemaphoreSlim(0, 1));
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gates.TryRemove(taskId, out _);
            gate.Dispose();
        }
    }

    public void Signal(Guid taskId, bool approved)
    {
        var gate = _gates.GetOrAdd(taskId, _ => new SemaphoreSlim(0, 1));
        gate.Release();
    }
}
