using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.API.Middleware;

// Missing/malformed/unknown/revoked/expired key -> 401. Valid key, suspended tenant -> 403.
// Sets the ambient ICurrentTenantAccessor scope for the request's downstream pipeline only —
// the scope is disposed (cleared) the instant this middleware returns. See ADR-014.
//
// Deliberately lives in OrchestAI.API, not OrchestAI.Infrastructure: IMiddleware (like
// IAsyncActionFilter, Task 8's RequireAdminSecretFilter) is ASP.NET Core HTTP-pipeline glue,
// not a persistence/cross-cutting concern. See LayeringTests.Infrastructure_DoesNotDependOnAspNetCoreHttp.
public sealed class TenantAuthenticationMiddleware : IMiddleware
{
    private static readonly TimeSpan LastUsedAtDebounceInterval = TimeSpan.FromMinutes(10);
    private const string BearerPrefix = "Bearer ";

    private readonly IApiKeyHasher _hasher;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ITenantLimitsProvider _tenantLimitsProvider;
    private readonly ILogger<TenantAuthenticationMiddleware> _logger;

    public TenantAuthenticationMiddleware(
        IApiKeyHasher hasher,
        IApiKeyRepository apiKeyRepository,
        ITenantRepository tenantRepository,
        ICurrentTenantAccessor tenantAccessor,
        ITenantLimitsProvider tenantLimitsProvider,
        ILogger<TenantAuthenticationMiddleware> logger)
    {
        _hasher = hasher;
        _apiKeyRepository = apiKeyRepository;
        _tenantRepository = tenantRepository;
        _tenantAccessor = tenantAccessor;
        _tenantLimitsProvider = tenantLimitsProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (IsExemptPath(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var rawKey = header[BearerPrefix.Length..].Trim();
        var parsed = _hasher.Parse(rawKey);
        if (parsed is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var apiKey = await _apiKeyRepository.GetByPublicKeyIdAsync(parsed.PublicKeyId, context.RequestAborted).ConfigureAwait(false);
        if (apiKey is null || !apiKey.IsUsable() || !_hasher.Verify(parsed.RawSecret, apiKey.HashedSecret))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var tenant = await _tenantRepository.GetByIdAsync(apiKey.TenantId, context.RequestAborted).ConfigureAwait(false);
        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (tenant.Status != TenantStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Items["ApiKeyId"] = apiKey.Id;

        await MaybeRecordUsageAsync(apiKey, context.RequestAborted).ConfigureAwait(false);

        // Warm ITenantLimitsProvider's DB-backed cache for this tenant *before* the request
        // reaches the rate limiter (UseRateLimiter runs after this middleware — see Program.cs).
        // Without this, RateLimiterSetup.BuildGlobalLimiter's partition-key factory calls the
        // cache-only, synchronous ITenantLimitsProvider.GetSnapshot() on this tenant's first-ever
        // request, gets the system-default limits back (cache miss), and System.Threading
        // .RateLimiting.PartitionedRateLimiter permanently bakes that default into the tenant's
        // token-bucket for the life of the process — later cache warms from GetAsync elsewhere
        // (admission/dispatch/enqueue) can't retroactively fix an already-created bucket. A
        // tenant that only ever hits read-only endpoints would never warm the cache at all under
        // the old code, so their configured RequestsPerMinute would never be enforced. See
        // ADR-015 implementation note #9 ("cold-cache-at-first-request", fixed) and #10
        // ("bucket immutability after limits change", accepted limitation, NOT fixed here).
        await _tenantLimitsProvider.GetAsync(tenant.Id, context.RequestAborted).ConfigureAwait(false);

        using (_tenantAccessor.SetTenant(tenant.Id))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private static bool IsExemptPath(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/swagger") ||
        path.StartsWithSegments("/api/v1/admin");

    private async Task MaybeRecordUsageAsync(ApiKey apiKey, CancellationToken cancellationToken)
    {
        if (apiKey.LastUsedAt is { } lastUsed && DateTimeOffset.UtcNow - lastUsed < LastUsedAtDebounceInterval)
            return;

        try
        {
            apiKey.RecordUsage();
            await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort — a failed usage-timestamp write must never fail authentication itself.
            _logger.LogWarning(ex, "Failed to record API key usage timestamp for key {PublicKeyId}", apiKey.PublicKeyId);
        }
    }
}
