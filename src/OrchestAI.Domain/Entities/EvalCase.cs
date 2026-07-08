using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class EvalCase
{
    private EvalCase() { }

    public Guid Id { get; private set; }
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
}
