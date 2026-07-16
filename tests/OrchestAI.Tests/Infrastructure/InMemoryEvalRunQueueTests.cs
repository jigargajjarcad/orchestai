using FluentAssertions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class InMemoryEvalRunQueueTests
{
    private static InMemoryEvalRunQueue CreateQueue(int maxQueueDepth)
    {
        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 5, 50, 50m, 500m, maxQueueDepth));
        return new InMemoryEvalRunQueue(limitsProviderMock.Object);
    }

    [Fact]
    public async Task EnqueueAsync_UnderLimit_Succeeds()
    {
        var queue = CreateQueue(maxQueueDepth: 2);
        var tenantId = Guid.NewGuid();

        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);
        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);
    }

    [Fact]
    public async Task EnqueueAsync_AtLimit_ThrowsTenantLimitExceededExceptionWithQueueBackpressureReason()
    {
        var queue = CreateQueue(maxQueueDepth: 1);
        var tenantId = Guid.NewGuid();
        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        var act = () => queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        var exception = await act.Should().ThrowAsync<TenantLimitExceededException>();
        exception.Which.Reason.Should().Be(RejectionReason.QueueBackpressure);
        exception.Which.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task DequeueAsync_DecrementsDepth_AllowingSubsequentEnqueue()
    {
        var queue = CreateQueue(maxQueueDepth: 1);
        var tenantId = Guid.NewGuid();
        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        await queue.DequeueAsync();
        var act = () => queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnqueueAsync_TwoTenants_HaveIndependentDepthCounters()
    {
        var queue = CreateQueue(maxQueueDepth: 1);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await queue.EnqueueAsync(Guid.NewGuid(), tenantA);

        var act = () => queue.EnqueueAsync(Guid.NewGuid(), tenantB);

        await act.Should().NotThrowAsync(
            "tenant B's queue depth must be completely independent of tenant A's — a shared/global counter would incorrectly reject this too");
    }

    [Fact]
    public async Task DequeueAsync_ReturnsItemsInFifoOrderAcrossInterleavedTenants()
    {
        var queue = CreateQueue(maxQueueDepth: 10);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await queue.EnqueueAsync(first, tenantA);
        await queue.EnqueueAsync(second, tenantB);
        await queue.EnqueueAsync(third, tenantA);

        (await queue.DequeueAsync()).EvalRunId.Should().Be(first);
        (await queue.DequeueAsync()).EvalRunId.Should().Be(second);
        (await queue.DequeueAsync()).EvalRunId.Should().Be(third);
    }
}
