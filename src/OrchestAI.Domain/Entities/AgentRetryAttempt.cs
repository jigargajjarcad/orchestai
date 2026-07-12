using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class AgentRetryAttempt : ITenantScoped
{
    private AgentRetryAttempt() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentExecutionId { get; private set; }
    public int AttemptNumber { get; private set; }
    public int DelayMs { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public AgentExecution AgentExecution { get; private set; } = null!;

    public static AgentRetryAttempt Create(
        Guid agentExecutionId, int attemptNumber, int delayMs, string reason)
    {
        return new AgentRetryAttempt
        {
            Id = Guid.NewGuid(),
            AgentExecutionId = agentExecutionId,
            AttemptNumber = attemptNumber,
            DelayMs = delayMs,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
