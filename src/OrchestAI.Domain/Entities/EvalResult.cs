using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// Immutable after creation — a score is a point-in-time record of what the scorer
// concluded when the run executed, never re-derived from current scorer logic on read.
public sealed class EvalResult : ITenantScoped
{
    private EvalResult() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EvalRunId { get; private set; }
    public Guid? EvalCaseId { get; private set; }
    public Guid? AgentExecutionId { get; private set; }
    public EvalScorerType ScorerType { get; private set; }
    public string ScorerVersion { get; private set; } = string.Empty;
    public decimal Score { get; private set; }
    public bool Passed { get; private set; }
    public string ScorerOutput { get; private set; } = string.Empty;
    public string? Rubric { get; private set; }
    public DateTimeOffset ScoredAt { get; private set; }

    public EvalRun Run { get; private set; } = null!;
    public EvalCase? Case { get; private set; }

    public static EvalResult Create(
        Guid evalRunId,
        Guid? evalCaseId,
        Guid? agentExecutionId,
        EvalScorerType scorerType,
        string scorerVersion,
        decimal score,
        bool passed,
        string scorerOutput,
        string? rubric = null)
    {
        return new EvalResult
        {
            Id = Guid.NewGuid(),
            EvalRunId = evalRunId,
            EvalCaseId = evalCaseId,
            AgentExecutionId = agentExecutionId,
            ScorerType = scorerType,
            ScorerVersion = scorerVersion,
            Score = score,
            Passed = passed,
            ScorerOutput = scorerOutput,
            Rubric = rubric,
            ScoredAt = DateTimeOffset.UtcNow
        };
    }
}
