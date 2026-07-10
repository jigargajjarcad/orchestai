using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalResultRepository
{
    Task<IReadOnlyList<EvalResult>> GetByRunIdAsync(Guid evalRunId, CancellationToken cancellationToken = default);
    Task AddAsync(EvalResult result, CancellationToken cancellationToken = default);

    // Idempotency check for post-hoc scoring (Week 9). Returns the subset of `agentExecutionIds`
    // already scored by this exact (scorer, version) via a prior post-hoc result. EvalCaseId IS
    // NULL marks a post-hoc-origin row — a live case-based result never collides here because it
    // always carries a real EvalCaseId.
    Task<IReadOnlyList<Guid>> GetScoredAgentExecutionIdsAsync(
        IReadOnlyCollection<Guid> agentExecutionIds, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default);

    // Supersede primitive for ForceRescore (Week 9) — deletes any prior post-hoc result for this
    // exact (trace, scorer, version) tuple so the worker can insert a fresh one without violating
    // the partial unique index. No-op if nothing existed (covers first-time scoring under
    // ForceRescore, which never checked existence in the first place). See ADR-013 confirmation #3.
    Task DeletePostHocResultAsync(
        Guid agentExecutionId, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default);
}
