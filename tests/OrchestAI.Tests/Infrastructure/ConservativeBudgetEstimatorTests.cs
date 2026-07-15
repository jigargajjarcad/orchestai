using FluentAssertions;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Budgeting;

namespace OrchestAI.Tests.Infrastructure;

public sealed class ConservativeBudgetEstimatorTests
{
    [Fact]
    public async Task EstimateWorstCaseCostAsync_MultipliesAgentsByToolCallsByAssumedCost()
    {
        var estimator = new ConservativeBudgetEstimator(
            Options.Create(new AbuseProtectionOptions { AssumedCostPerToolCallUsd = 0.10m }));
        var limits = new ResolvedTenantLimits(120, 5, 4, 20, 50m, 500m, 100);

        var estimate = await estimator.EstimateWorstCaseCostAsync(limits);

        estimate.Should().Be(4 * 20 * 0.10m);
    }
}
