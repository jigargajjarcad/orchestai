using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Agents;

public sealed class AsyncLocalTaskToolCallBudget : ITaskToolCallBudget
{
    private static readonly AsyncLocal<Counter?> Ambient = new();

    public IDisposable BeginScope(int maxToolCalls)
    {
        var previous = Ambient.Value;
        Ambient.Value = new Counter(maxToolCalls);
        return new RestoreScope(previous);
    }

    public ToolCallBudgetCheck TryIncrement()
    {
        var counter = Ambient.Value;
        if (counter is null)
            return new ToolCallBudgetCheck(true, 0, int.MaxValue);

        var newCount = Interlocked.Increment(ref counter.Count);
        return new ToolCallBudgetCheck(newCount <= counter.Max, newCount, counter.Max);
    }

    private sealed class Counter(int max)
    {
        public int Count;
        public readonly int Max = max;
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Counter? _previous;
        private bool _disposed;

        public RestoreScope(Counter? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}
