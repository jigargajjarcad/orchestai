namespace OrchestAI.Application.Configuration;

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

    // Hard ceiling on RequestPostHocScoringCommand.MaxTraces — a caller-supplied cap smaller than
    // this is fine, but a single post-hoc request can never ask for more than this many judge
    // calls, regardless of what the caller passes. See ADR-013 confirmation #4.
    public int MaxPostHocTracesPerRequestCeiling { get; init; } = 500;
}
