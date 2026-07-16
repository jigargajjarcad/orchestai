using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OrchestAI.API.RateLimiting;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.API;

public sealed class RateLimiterPartitioningTests
{
    private static HttpContext CreateContext(Guid? tenantId, int requestsPerMinute, string path = "/api/v1/tasks")
    {
        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(tenantId);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetSnapshot(It.IsAny<Guid>()))
            .Returns(new ResolvedTenantLimits(requestsPerMinute, 5, 5, 50, 50m, 500m, 100));

        var services = new ServiceCollection();
        services.AddSingleton(accessorMock.Object);
        services.AddSingleton(limitsProviderMock.Object);
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Path = path;
        return context;
    }

    [Fact]
    public void BuildGlobalLimiter_ExemptPath_NeverRejects()
    {
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var context = CreateContext(tenantId: null, requestsPerMinute: 1, path: "/health");

        for (var i = 0; i < 100; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue();
        }
    }

    [Fact]
    public void BuildGlobalLimiter_SingleTenantExceedsBucket_SubsequentRequestRejected()
    {
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var context = CreateContext(Guid.NewGuid(), requestsPerMinute: 3);

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue();
        }

        using var overLimitLease = limiter.AttemptAcquire(context);
        overLimitLease.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void BuildGlobalLimiter_TwoDifferentTenants_HaveIndependentBuckets()
    {
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var tenantAContext = CreateContext(Guid.NewGuid(), requestsPerMinute: 1);
        var tenantBContext = CreateContext(Guid.NewGuid(), requestsPerMinute: 1);

        using var firstLeaseA = limiter.AttemptAcquire(tenantAContext);
        firstLeaseA.IsAcquired.Should().BeTrue();
        using var secondLeaseA = limiter.AttemptAcquire(tenantAContext);
        secondLeaseA.IsAcquired.Should().BeFalse("tenant A has exhausted its own bucket");

        using var firstLeaseB = limiter.AttemptAcquire(tenantBContext);
        firstLeaseB.IsAcquired.Should().BeTrue(
            "tenant B's bucket must be completely independent of tenant A's — a shared/global bucket would incorrectly reject this too");
    }

    [Fact]
    public void BuildGlobalLimiter_NoAmbientTenant_NeverRejects()
    {
        // TenantAuthenticationMiddleware always runs before the rate limiter and rejects (401)
        // any request with no resolvable tenant before it ever reaches the limiter — this case
        // is defensive, not expected to occur in the real pipeline.
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var context = CreateContext(tenantId: null, requestsPerMinute: 1);

        using var lease = limiter.AttemptAcquire(context);

        lease.IsAcquired.Should().BeTrue();
    }
}
