namespace OrchestAI.Infrastructure.Configuration;

public sealed class EvalOptions
{
    public const string SectionName = "Eval";

    // "provider/model", same qualified format as AgentOptions.Models — e.g.
    // "anthropic/claude-haiku-4-5-20251001". A cheap, fast model is deliberately the default:
    // judge calls happen once per eval case per run, at real provider cost (see ADR-012).
    public string JudgeModel { get; init; } = "anthropic/claude-haiku-4-5-20251001";

    // Used when an EvalCase's ExpectedCriteria JSON for an LlmJudge case omits its own
    // "passThreshold" — see RuleBasedScorer/LlmJudgeScorer's ExpectedCriteria shape.
    public decimal DefaultJudgePassThreshold { get; init; } = 0.7m;
}
