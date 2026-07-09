using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalScorerFactory
{
    IEvalScorer Resolve(EvalScorerType scorerType);
}
