namespace OrchestAI.Domain.Exceptions;

// Thrown for any agent-level execution cap being exceeded: from AgentBase.InvokeToolAsync when
// the task-wide tool-call cap (MaxToolCallsPerTask) would be exceeded, and from
// AgentBase.ExecuteAsync's main loop when MaxAgenticIterations is exhausted before the model
// reaches a final answer (see docs/phase3-domain-notes.md for the live-run finding that surfaced
// the latter). Deliberately NOT TenantLimitExceededException — that type is reserved for the
// synchronous-HTTP-rejection contract (Task 2); this one is always caught internally by
// AgentBase.ExecuteAsync's existing catch-all, which converts it into a normal Failed
// AgentExecutionResult, reusing the existing failure-aggregation path in StartOrchestrationHandler.
public sealed class AgentCapExceededException : Exception
{
    public AgentCapExceededException(string message) : base(message) { }
}
