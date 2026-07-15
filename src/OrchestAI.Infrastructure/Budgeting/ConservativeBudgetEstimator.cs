using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Budgeting;

public sealed class ConservativeBudgetEstimator : IBudgetEstimator
{
    private readonly decimal _assumedCostPerToolCallUsd;

    public ConservativeBudgetEstimator(IOptions<AbuseProtectionOptions> options)
        => _assumedCostPerToolCallUsd = options.Value.AssumedCostPerToolCallUsd;

    public Task<decimal> EstimateWorstCaseCostAsync(ResolvedTenantLimits limits, CancellationToken cancellationToken = default)
    {
        var estimate = limits.MaxAgentsPerTask * limits.MaxToolCallsPerTask * _assumedCostPerToolCallUsd;
        return Task.FromResult(estimate);
    }
}
