using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Eval;

public sealed class EvalScorerFactory : IEvalScorerFactory
{
    private readonly IReadOnlyDictionary<EvalScorerType, IEvalScorer> _scorersByType;

    public EvalScorerFactory(IEnumerable<IEvalScorer> scorers)
    {
        _scorersByType = scorers.ToDictionary(s => s.ScorerType);
    }

    public IEvalScorer Resolve(EvalScorerType scorerType) =>
        _scorersByType.TryGetValue(scorerType, out var scorer)
            ? scorer
            : throw new InvalidOperationException($"No IEvalScorer registered for '{scorerType}'.");
}
