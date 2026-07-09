using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalResultRepository
{
    Task<IReadOnlyList<EvalResult>> GetByRunIdAsync(Guid evalRunId, CancellationToken cancellationToken = default);
    Task AddAsync(EvalResult result, CancellationToken cancellationToken = default);
}
