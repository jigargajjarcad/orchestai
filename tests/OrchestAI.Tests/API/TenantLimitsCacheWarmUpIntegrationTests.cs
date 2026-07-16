using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.API.Middleware;
using OrchestAI.API.RateLimiting;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.API;

// Proves Bug #1 from the final whole-branch review (ADR-015 implementation note #9,
// "cold-cache-at-first-request") is fixed: a tenant's very first HTTP-level request — cold
// ITenantLimitsProvider cache AND cold PartitionedRateLimiter partition, exactly as both are on
// process start for a brand-new tenant — is rate-limited according to THAT tenant's configured
// RequestsPerMinute, not TenantLimitsDefaults.RequestsPerMinute (120).
//
// This deliberately does NOT pre-warm the cache before the limiter runs — that would prove
// nothing, since GetSnapshot always returns a warm value once anything has warmed it. Instead it
// reproduces the actual Program.cs pipeline order: TenantAuthenticationMiddleware runs first and
// (after this fix) calls ITenantLimitsProvider.GetAsync as a side effect of authenticating the
// request; only THEN, inside `next` (standing in for the downstream UseRateLimiter() middleware),
// does RateLimiterSetup.BuildGlobalLimiter()'s partition-key factory run for the first time for
// this tenant and read GetSnapshot(). See TenantAuthenticationMiddlewareTests.cs for the
// middleware's own unit tests and RateLimiterPartitioningTests.cs for the limiter's.
public sealed class TenantLimitsCacheWarmUpIntegrationTests
{
    [Fact]
    public async Task ColdCacheTenant_FirstRequestThroughMiddleware_RateLimiterEnforcesConfiguredLimit_NotSystemDefault()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");
        const int configuredRequestsPerMinute = 3; // deliberately far below the 120 system default
        var limitsRow = TenantLimits.Create(
            tenant.Id, configuredRequestsPerMinute, null, null, null, null, null, null);

        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(limitsRow);
        var repoServices = new ServiceCollection();
        repoServices.AddSingleton(repoMock.Object);
        var repoServiceProvider = repoServices.BuildServiceProvider();

        // The real, concrete EfTenantLimitsProvider — not a mock — so the middleware's GetAsync
        // call has a genuine caching side effect that GetSnapshot can later observe. The same
        // instance is used by both the middleware and (via RequestServices, below) the limiter,
        // exactly as the same singleton is shared across the real DI container.
        var tenantLimitsProvider = new EfTenantLimitsProvider(
            repoServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TenantLimitsDefaults()),
            Options.Create(new AbuseProtectionOptions()));

        // Precondition: prove the cache is genuinely cold before anything runs, or this test
        // would not actually be exercising Bug #1.
        tenantLimitsProvider.GetSnapshot(tenant.Id).RequestsPerMinute.Should().Be(120,
            "the cache must be cold before the middleware runs, or this test would not be exercising Bug #1 at all");

        var hasher = new Mock<IApiKeyHasher>();
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        var apiKeyRepo = new Mock<IApiKeyRepository>();
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        apiKeyRepo.Setup(r => r.UpdateAsync(apiKey, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tenantRepo = new Mock<ITenantRepository>();
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var tenantAccessor = new AsyncLocalCurrentTenantAccessor();

        var middleware = new TenantAuthenticationMiddleware(
            hasher.Object, apiKeyRepo.Object, tenantRepo.Object, tenantAccessor, tenantLimitsProvider,
            NullLogger<TenantAuthenticationMiddleware>.Instance);

        // The same HttpContext travels through both the middleware and (inside `next`) the rate
        // limiter, exactly as it does through the real ASP.NET Core pipeline. RequestServices
        // exposes the SAME accessor/provider instances the middleware uses, since
        // RateLimiterSetup.BuildGlobalLimiter reads them via httpContext.RequestServices.
        var limiterServices = new ServiceCollection();
        limiterServices.AddSingleton<ICurrentTenantAccessor>(tenantAccessor);
        limiterServices.AddSingleton<ITenantLimitsProvider>(tenantLimitsProvider);
        var limiterServiceProvider = limiterServices.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = limiterServiceProvider };
        context.Request.Path = "/api/v1/tasks";
        context.Request.Headers.Authorization = "Bearer orch_live_pk123.secret";

        // Built before the request runs, exactly like the real app (BuildGlobalLimiter() runs
        // once at startup). Building it early does NOT create any tenant bucket — the partition
        // factory only runs lazily, the first time AttemptAcquire sees that partition key, which
        // happens below, inside `next` — i.e. after the middleware's cache warm-up has already run.
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var acquiredCount = 0;
        var rejectedOnAttempt = -1;

        await middleware.InvokeAsync(context, _ =>
        {
            for (var i = 0; i < configuredRequestsPerMinute + 1; i++)
            {
                using var lease = limiter.AttemptAcquire(context);
                if (lease.IsAcquired)
                    acquiredCount++;
                else if (rejectedOnAttempt == -1)
                    rejectedOnAttempt = i;
            }

            return Task.CompletedTask;
        });

        acquiredCount.Should().Be(configuredRequestsPerMinute,
            "the tenant's configured RequestsPerMinute (3), warmed into the cache by the middleware before the limiter ever ran, must govern the bucket created on this cold-cache first request");
        rejectedOnAttempt.Should().Be(configuredRequestsPerMinute,
            "the request immediately after the configured limit is exhausted must be rejected — if the system default (120) had leaked into this bucket instead, all 4 attempts here would have been acquired");
    }
}
