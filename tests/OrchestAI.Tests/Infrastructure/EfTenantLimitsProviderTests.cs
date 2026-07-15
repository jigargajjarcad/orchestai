using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EfTenantLimitsProviderTests
{
    private static EfTenantLimitsProvider CreateProvider(Mock<ITenantLimitsRepository> repoMock, int refreshSeconds = 30)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repoMock.Object);
        var provider = services.BuildServiceProvider();

        return new EfTenantLimitsProvider(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TenantLimitsDefaults()),
            Options.Create(new AbuseProtectionOptions { TenantLimitsCacheRefreshSeconds = refreshSeconds }));
    }

    [Fact]
    public async Task GetAsync_NoRow_ReturnsSystemDefaults()
    {
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantLimits?)null);
        var provider = CreateProvider(repoMock);

        var resolved = await provider.GetAsync(Guid.NewGuid());

        resolved.RequestsPerMinute.Should().Be(120);
        resolved.MaxConcurrentTasks.Should().Be(5);
        resolved.DailyCostBudgetUsd.Should().Be(50m);
    }

    [Fact]
    public async Task GetAsync_PartialRow_MergesRowValuesWithDefaults()
    {
        var tenantId = Guid.NewGuid();
        var row = TenantLimits.Create(tenantId, requestsPerMinute: 500, null, null, null, null, null, null);
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var provider = CreateProvider(repoMock);

        var resolved = await provider.GetAsync(tenantId);

        resolved.RequestsPerMinute.Should().Be(500);
        resolved.MaxConcurrentTasks.Should().Be(5, "unset fields fall back to defaults");
    }

    [Fact]
    public async Task GetAsync_CalledTwiceWithinRefreshWindow_OnlyQueriesRepositoryOnce()
    {
        var tenantId = Guid.NewGuid();
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync((TenantLimits?)null);
        var provider = CreateProvider(repoMock, refreshSeconds: 300);

        await provider.GetAsync(tenantId);
        await provider.GetAsync(tenantId);

        repoMock.Verify(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GetSnapshot_TenantNeverCached_ReturnsSystemDefaults()
    {
        var repoMock = new Mock<ITenantLimitsRepository>();
        var provider = CreateProvider(repoMock);

        var resolved = provider.GetSnapshot(Guid.NewGuid());

        resolved.RequestsPerMinute.Should().Be(120);
        repoMock.Verify(r => r.GetByTenantIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "GetSnapshot must never hit the database — it is cache-only by construction");
    }

    [Fact]
    public async Task GetSnapshot_AfterGetAsync_ReturnsCachedValue()
    {
        var tenantId = Guid.NewGuid();
        var row = TenantLimits.Create(tenantId, requestsPerMinute: 999, null, null, null, null, null, null);
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var provider = CreateProvider(repoMock);

        await provider.GetAsync(tenantId);
        var snapshot = provider.GetSnapshot(tenantId);

        snapshot.RequestsPerMinute.Should().Be(999);
    }

    [Fact]
    public async Task GetAsync_ConcurrentCallsDuringColdCache_CoalesceIntoSingleRepositoryCall()
    {
        var tenantId = Guid.NewGuid();
        var row = TenantLimits.Create(tenantId, requestsPerMinute: 777, null, null, null, null, null, null);
        // Held open until both GetAsync calls have been started, so the second call is
        // guaranteed to observe a cold cache and block on the single-flight lock rather than
        // the DB call, proving the guard — not scheduling luck — is what limits this to one call.
        var gate = new TaskCompletionSource<TenantLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>())).Returns(gate.Task);
        var provider = CreateProvider(repoMock);

        var task1 = provider.GetAsync(tenantId);
        var task2 = provider.GetAsync(tenantId);

        gate.SetResult(row);
        var results = await Task.WhenAll(task1, task2);

        results[0].RequestsPerMinute.Should().Be(777);
        results[1].RequestsPerMinute.Should().Be(777);
        repoMock.Verify(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once,
            "concurrent GetAsync calls for the same tenant during a cache miss must coalesce into a single DB round trip");
    }
}
