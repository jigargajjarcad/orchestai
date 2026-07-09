using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalRunRepository
{
    Task<EvalRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Most recent runs for a suite, newest first — powers the baseline-run dropdown.
    Task<IReadOnlyList<EvalRun>> GetBySuiteIdAsync(
        Guid suiteId, CancellationToken cancellationToken = default);

    Task AddAsync(EvalRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(EvalRun run, CancellationToken cancellationToken = default);
}
