using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// One row per (Date, UserId, AgentType, Model). Populated by CostRollupAggregationService —
// see ADR-011. Recomputed (not incrementally accumulated) on every aggregation tick so a
// re-run of the job for the same day is idempotent.
public sealed class CostRollup : ITenantScoped
{
    private CostRollup() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid UserId { get; private set; }
    public AgentType AgentType { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public int ExecutionCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CostRollup Create(
        DateOnly date, Guid userId, AgentType agentType, string model,
        int inputTokens, int outputTokens, decimal costUsd, int executionCount)
    {
        return new CostRollup
        {
            Id = Guid.NewGuid(),
            Date = date,
            UserId = userId,
            AgentType = agentType,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd,
            ExecutionCount = executionCount,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void ReplaceValues(int inputTokens, int outputTokens, decimal costUsd, int executionCount)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        ExecutionCount = executionCount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
