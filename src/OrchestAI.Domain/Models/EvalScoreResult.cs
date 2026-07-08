namespace OrchestAI.Domain.Models;

public sealed record EvalScoreResult(
    decimal Score,
    bool Passed,
    string ScorerVersion,
    string ScorerOutputJson
);
