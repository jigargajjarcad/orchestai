namespace OrchestAI.Domain.Models;

// Carries what a scorer needs beyond the case/output pair to attribute its own cost correctly —
// specifically, LlmJudgeScorer's judge call must write a CostLedger row tagged Source=Eval and
// linked back to the triggering EvalRun, and CostLedger.OrchestrationTaskId is NOT NULL, so the
// per-case OrchestrationTaskId the live invocation already created must travel with the score
// request. RuleBasedScorer ignores this entirely. See ADR-012.
public sealed record EvalScoringContext(Guid OrchestrationTaskId, Guid EvalRunId);
