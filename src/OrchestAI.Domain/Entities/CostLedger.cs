using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class CostLedger : ITenantScoped
{
    private CostLedger() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrchestrationTaskId { get; private set; }
    public Guid? AgentExecutionId { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public CostSource Source { get; private set; }
    public Guid? EvalRunId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public OrchestrationTask OrchestrationTask { get; private set; } = null!;
    public AgentExecution? AgentExecution { get; private set; }

    public static CostLedger Create(
        Guid orchestrationTaskId,
        string model,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        Guid? agentExecutionId = null,
        CostSource source = CostSource.Production,
        Guid? evalRunId = null)
    {
        return new CostLedger
        {
            Id = Guid.NewGuid(),
            OrchestrationTaskId = orchestrationTaskId,
            AgentExecutionId = agentExecutionId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd,
            Source = source,
            EvalRunId = evalRunId,
            RecordedAt = DateTimeOffset.UtcNow
        };
    }
}
