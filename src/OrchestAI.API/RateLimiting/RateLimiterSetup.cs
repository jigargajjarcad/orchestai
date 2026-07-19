using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OrchestAI.API.ExceptionHandling;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.API.RateLimiting;

public static class RateLimiterSetup
{
    public static IServiceCollection AddTenantRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = BuildGlobalLimiter();
            options.OnRejected = OnRejectedAsync;
        });

        return services;
    }

    // Exposed public and static deliberately, so the partitioning/exemption logic (the part
    // actually worth proving) is directly testable without a WebApplicationFactory/TestServer —
    // see RateLimiterPartitioningTests.
    public static PartitionedRateLimiter<HttpContext> BuildGlobalLimiter()
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            if (IsExemptPath(httpContext.Request.Path))
                return RateLimitPartition.GetNoLimiter("exempt");

            var tenantAccessor = httpContext.RequestServices.GetRequiredService<ICurrentTenantAccessor>();
            var tenantId = tenantAccessor.TenantId;
            if (tenantId is null)
                // TenantAuthenticationMiddleware (which runs before this limiter — see Program.cs)
                // already rejects any request with no resolvable tenant. Defensive, not expected.
                return RateLimitPartition.GetNoLimiter("no-tenant");

            var limitsProvider = httpContext.RequestServices.GetRequiredService<ITenantLimitsProvider>();
            var limits = limitsProvider.GetSnapshot(tenantId.Value);

            return RateLimitPartition.GetTokenBucketLimiter(tenantId.Value.ToString(), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = limits.RequestsPerMinute,
                TokensPerPeriod = limits.RequestsPerMinute,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfterSeconds = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

        var responder = context.HttpContext.RequestServices.GetRequiredService<RejectionResponder>();
        await responder.RespondToRateLimitAsync(context.HttpContext, retryAfterSeconds, cancellationToken).ConfigureAwait(false);
    }

    public static bool IsExemptPath(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/swagger") ||
        path.StartsWithSegments("/api/v1/admin") ||
        // Keep this clause textually identical to TenantAuthenticationMiddleware.IsExemptPath's
        // own "/stream" exemption (added in Task 1, Phase 1 architecture/product validation, so
        // EventSource's inability to send an Authorization header doesn't 401 every browser
        // client) — a future change to one without the other reintroduces exactly this bug,
        // either in reverse or by leaving the stream endpoint unrate-limited.
        (path.Value?.EndsWith("/stream", StringComparison.OrdinalIgnoreCase) ?? false);
}
