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
    public void BuildGlobalLimiter_ExemptPathWithRealTenant_BypassesTenantBucket()
    {
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var context = CreateContext(Guid.NewGuid(), requestsPerMinute: 1, path: "/health");

        // A real tenant with a bucket of just 1 request/minute would be exhausted after the first
        // acquire if the exempt-path check weren't actually short-circuiting before the tenant
        // partition is even resolved. This isolates IsExemptPath from the separate null-tenant
        // fallback (which BuildGlobalLimiter_ExemptPath_NeverRejects above cannot distinguish from).
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

    // Demonstrates Bug #2 from the final whole-branch review (ADR-015 implementation note #9,
    // "bucket immutability after a limits change") — an ACCEPTED, NAMED ARCHITECTURAL LIMITATION,
    // deliberately NOT fixed by the cache-warming change that closes Bug #1 (see
    // TenantLimitsCacheWarmUpIntegrationTests). This test exists specifically so nobody reads
    // "cache-warming fix landed" and assumes the whole gap is closed.
    //
    // The mechanism is more precise than "GetSnapshot is only read once": BuildGlobalLimiter's
    // outer partition-key-resolver delegate (which calls GetSnapshot) actually runs on EVERY
    // single AttemptAcquire call, cold or warm — proven below by the call-count assertions. What
    // never re-runs is the INNER TokenBucketRateLimiterOptions factory nested inside
    // RateLimitPartition.GetTokenBucketLimiter — System.Threading.RateLimiting's
    // PartitionedRateLimiter only invokes that inner factory (and so only ever bakes a
    // TokenLimit/TokensPerPeriod into an actual bucket) the first time it sees a given partition
    // key; every subsequent AttemptAcquire for that key reuses the existing TokenBucketRateLimiter
    // instance and silently discards whatever RateLimitPartition the resolver just built. So even
    // though this test's GetSnapshot mock keeps returning a live, up-to-date value after the
    // simulated admin change, that freshly-read value has zero effect on the tenant's
    // already-created bucket — confirming the immutability is a structural property of
    // PartitionedRateLimiter's own partition-caching, not a staleness problem any caching layer
    // (this fix's or otherwise) could paper over.
    [Fact]
    public void BuildGlobalLimiter_TenantLimitsChangedAfterBucketCreated_ChangeHasNoEffectOnExistingBucket()
    {
        var tenantId = Guid.NewGuid();
        var currentRequestsPerMinute = 3;
        var getSnapshotCallCount = 0;

        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(tenantId);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetSnapshot(tenantId))
            .Returns(() =>
            {
                getSnapshotCallCount++;
                return new ResolvedTenantLimits(currentRequestsPerMinute, 5, 5, 50, 50m, 500m, 100);
            });

        var services = new ServiceCollection();
        services.AddSingleton(accessorMock.Object);
        services.AddSingleton(limitsProviderMock.Object);
        var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Path = "/api/v1/tasks";

        var limiter = RateLimiterSetup.BuildGlobalLimiter();

        // Exhaust the tenant's original 3-request bucket. This is what actually creates the
        // bucket (via the inner TokenBucketRateLimiterOptions factory, on the very first call).
        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue();
        }

        using var exhaustedLease = limiter.AttemptAcquire(context);
        exhaustedLease.IsAcquired.Should().BeFalse();
        getSnapshotCallCount.Should().Be(4,
            "the outer partition-key-resolver delegate calls GetSnapshot on every single AttemptAcquire, not just the first — the staleness is not in this read");

        // Simulate an admin PUT .../limits call landing (e.g. via SetTenantLimitsCommand) that
        // raises this tenant's configured RequestsPerMinute from 3 to 100, and the underlying
        // cache having already refreshed to reflect it — GetSnapshot will genuinely return 100
        // from this point on, proven by the call-count assertion below.
        currentRequestsPerMinute = 100;

        // If cache-warming (Bug #1's fix) had ANY effect on Bug #2, this next acquire would
        // succeed against the new, much larger limit. It does not: the bucket created above is
        // permanently governed by the ORIGINAL limit (3, already exhausted) for the rest of the
        // process's lifetime, even though the read backing it is fully live and up to date.
        using var afterChangeLease = limiter.AttemptAcquire(context);
        afterChangeLease.IsAcquired.Should().BeFalse(
            "the existing bucket was created against the old 3-req/min limit and PartitionedRateLimiter never rebuilds an existing partition's bucket, even though the tenant is now configured for 100 req/min and GetSnapshot faithfully reports it — this is Bug #2, an accepted architectural limitation, not something the cache-warming fix addresses");
        getSnapshotCallCount.Should().Be(5,
            "GetSnapshot WAS called again and DID return the new value (100) — proving this isn't a stale-read problem at all; PartitionedRateLimiter simply never reconsiders an already-existing partition's bucket regardless of what the resolver returns");
    }
}
