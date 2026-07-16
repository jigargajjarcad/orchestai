namespace OrchestAI.Domain.Exceptions;

// Thrown deep inside AgentBase.InvokeToolAsync when the task-wide tool-call cap would be
// exceeded. Deliberately NOT TenantLimitExceededException — that type is reserved for the
// synchronous-HTTP-rejection contract (Task 2); this one is always caught internally by
// AgentBase.ExecuteAsync's existing catch-all, which converts it into a normal Failed
// AgentExecutionResult, reusing the existing failure-aggregation path in StartOrchestrationHandler.
public sealed class AgentCapExceededException : Exception
{
    public AgentCapExceededException(string message) : base(message) { }
}
