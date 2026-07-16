using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// AsyncLocal-backed, mirrors ICurrentTenantAccessor's proven shape — scoped once per task's
// background dispatch (StartOrchestrationHandler), shared safely across parallel sub-agents
// forked via Task.WhenAll: each fork gets a reference to the same counter instance (AsyncLocal
// copy-on-fork semantics), and increments are Interlocked. See Task 8 and ADR-015.
public interface ITaskToolCallBudget
{
    // Opens a new scope with the given cap; disposing restores whatever scope (if any) was
    // ambient before. Opened once per task, before any sub-agent is dispatched.
    IDisposable BeginScope(int maxToolCalls);

    // Atomically increments the call count for the ambient scope. Returns Allowed: true with no
    // scope open at all (uncapped — callers outside a StartOrchestrationHandler-managed
    // dispatch, e.g. eval/post-hoc scoring agent runs, are intentionally not capped by this
    // mechanism this week).
    ToolCallBudgetCheck TryIncrement();
}
