using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// Isolates the worst-case cost estimate used for budget reservation behind one seam — see
// ADR-015 confirmation #8. Deliberately conservative for this week (ConservativeBudgetEstimator
// over-reserves rather than risks overspend); a smarter estimator can replace it later without
// touching admission logic, since every caller depends only on this interface.
public interface IBudgetEstimator
{
    Task<decimal> EstimateWorstCaseCostAsync(ResolvedTenantLimits limits, CancellationToken cancellationToken = default);
}
