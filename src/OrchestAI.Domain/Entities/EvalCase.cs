using System.Text.Json;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class EvalCase : ITenantScoped
{
    private EvalCase() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SuiteId { get; private set; }
    public string InputPayload { get; private set; } = string.Empty;
    public string ExpectedCriteria { get; private set; } = string.Empty;
    public EvalScorerType ScorerType { get; private set; }
    public decimal RegressionThreshold { get; private set; }
    public string Tags { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public EvalSuite Suite { get; private set; } = null!;

    public static EvalCase Create(
        Guid suiteId,
        string inputPayload,
        string expectedCriteria,
        EvalScorerType scorerType,
        decimal regressionThreshold,
        string tags = "")
    {
        return new EvalCase
        {
            Id = Guid.NewGuid(),
            SuiteId = suiteId,
            InputPayload = inputPayload,
            ExpectedCriteria = expectedCriteria,
            ScorerType = scorerType,
            RegressionThreshold = regressionThreshold,
            Tags = tags,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    // Builds a transient, never-persisted EvalCase so post-hoc scoring can reuse IEvalScorer
    // unchanged when there's no predefined EvalCase to point to — see ADR-013 confirmation #2.
    // Always LlmJudge: RuleBasedScorer requires machine-checkable ExpectedCriteria tied to a
    // specific predefined case, which doesn't exist for arbitrary historical traces.
    public static EvalCase CreateEphemeral(string rubric, decimal? passThreshold)
    {
        var criteria = passThreshold.HasValue
            ? JsonSerializer.Serialize(new { rubric, passThreshold = passThreshold.Value })
            : JsonSerializer.Serialize(new { rubric });

        return new EvalCase
        {
            Id = Guid.Empty,
            SuiteId = Guid.Empty,
            InputPayload = string.Empty,
            ExpectedCriteria = criteria,
            ScorerType = EvalScorerType.LlmJudge,
            RegressionThreshold = 0m,
            Tags = "posthoc-ephemeral",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
