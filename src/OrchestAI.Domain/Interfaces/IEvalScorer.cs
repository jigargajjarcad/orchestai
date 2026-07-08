using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalScorer
{
    EvalScorerType ScorerType { get; }

    Task<EvalScoreResult> ScoreAsync(
        EvalCase evalCase,
        string actualOutput,
        EvalScoringContext context,
        CancellationToken cancellationToken = default);
}
