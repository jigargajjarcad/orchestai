using FluentAssertions;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Tests.Domain;

public sealed class TenantLimitsTests
{
    [Fact]
    public void Create_AllFieldsNull_ProducesNullRow()
    {
        var tenantId = Guid.NewGuid();
        var limits = TenantLimits.Create(tenantId, null, null, null, null, null, null, null);

        limits.TenantId.Should().Be(tenantId);
        limits.RequestsPerMinute.Should().BeNull();
        limits.MaxConcurrentTasks.Should().BeNull();
        limits.MaxAgentsPerTask.Should().BeNull();
        limits.MaxToolCallsPerTask.Should().BeNull();
        limits.DailyCostBudgetUsd.Should().BeNull();
        limits.MonthlyCostBudgetUsd.Should().BeNull();
        limits.MaxQueueDepth.Should().BeNull();
    }

    [Fact]
    public void Update_ChangesAllFieldsAndBumpsUpdatedAt()
    {
        var limits = TenantLimits.Create(Guid.NewGuid(), null, null, null, null, null, null, null);
        var before = limits.UpdatedAt;

        limits.Update(200, 10, 8, 100, 75m, 750m, 200);

        limits.RequestsPerMinute.Should().Be(200);
        limits.MaxConcurrentTasks.Should().Be(10);
        limits.MaxAgentsPerTask.Should().Be(8);
        limits.MaxToolCallsPerTask.Should().Be(100);
        limits.DailyCostBudgetUsd.Should().Be(75m);
        limits.MonthlyCostBudgetUsd.Should().Be(750m);
        limits.MaxQueueDepth.Should().Be(200);
        limits.UpdatedAt.Should().BeOnOrAfter(before);
    }
}
