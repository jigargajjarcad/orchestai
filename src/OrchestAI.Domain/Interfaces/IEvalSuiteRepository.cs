using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalSuiteRepository
{
    Task<EvalSuite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EvalSuite?> GetByIdWithCasesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EvalSuite>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(EvalSuite suite, CancellationToken cancellationToken = default);
    Task AddCaseAsync(EvalCase evalCase, CancellationToken cancellationToken = default);
}
