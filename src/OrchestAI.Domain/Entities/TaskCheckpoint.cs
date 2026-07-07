using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class TaskCheckpoint
{
    private TaskCheckpoint() { }

    public Guid Id { get; private set; }
    public Guid OrchestrationTaskId { get; private set; }
    public AgentType AgentType { get; private set; }
    public Guid AgentExecutionId { get; private set; }
    public string Output { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public DateTimeOffset CheckpointedAt { get; private set; }

    public OrchestrationTask OrchestrationTask { get; private set; } = null!;

    public void UpdateOutput(
        Guid agentExecutionId, string output, int inputTokens, int outputTokens, decimal costUsd)
    {
        AgentExecutionId = agentExecutionId;
        Output = output;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        CheckpointedAt = DateTimeOffset.UtcNow;
    }

    public static TaskCheckpoint Create(
        Guid orchestrationTaskId,
        AgentType agentType,
        Guid agentExecutionId,
        string output,
        int inputTokens,
        int outputTokens,
        decimal costUsd)
    {
        return new TaskCheckpoint
        {
            Id = Guid.NewGuid(),
            OrchestrationTaskId = orchestrationTaskId,
            AgentType = agentType,
            AgentExecutionId = agentExecutionId,
            Output = output,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd,
            CheckpointedAt = DateTimeOffset.UtcNow
        };
    }
}
