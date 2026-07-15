using System.Text.Json;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.API.ExceptionHandling;

// The single place that builds every 429 response and writes the corresponding RejectionEvent —
// called from two entry points (the rate limiter's OnRejected callback, and
// TenantLimitExceededExceptionHandler) so there is exactly one place, not five, that decides
// what a rejection response looks like. See Global Constraints and ADR-015.
public sealed class RejectionResponder
{
    private readonly IRejectionEventRepository _rejectionEventRepository;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ILogger<RejectionResponder> _logger;

    public RejectionResponder(
        IRejectionEventRepository rejectionEventRepository,
        ICurrentTenantAccessor tenantAccessor,
        ILogger<RejectionResponder> logger)
    {
        _rejectionEventRepository = rejectionEventRepository;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    // Runs inside the live request's ambient tenant scope — UseRateLimiter() is registered
    // after TenantAuthenticationMiddleware (Task 9), so ICurrentTenantAccessor.TenantId is valid.
    public async Task RespondToRateLimitAsync(HttpContext context, int retryAfterSeconds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantAccessor.TenantId
            ?? throw new InvalidOperationException(
                "Rate limiter rejected a request with no ambient tenant — UseRateLimiter() must run after TenantAuthenticationMiddleware.");
        var apiKeyId = context.Items.TryGetValue("ApiKeyId", out var raw) && raw is Guid id ? id : (Guid?)null;
        var detailsJson = JsonSerializer.Serialize(new { limit = "requests_per_minute", retryAfterSeconds });

        await WriteAsync(
            context, tenantId, RejectionReason.RateLimited, "Rate limit exceeded.",
            retryAfterSeconds, detailsJson, apiKeyId, traceId: null, cancellationToken).ConfigureAwait(false);
    }

    // The ambient tenant scope has already unwound by the time global exception handling runs
    // (see Task 2's investigation note) — TenantId comes from the exception, and the scope is
    // explicitly re-opened just long enough for the RejectionEvent write to be stamped normally.
    public async Task RespondToExceptionAsync(
        HttpContext context, TenantLimitExceededException exception, CancellationToken cancellationToken)
    {
        using (_tenantAccessor.SetTenant(exception.TenantId))
        {
            await WriteAsync(
                context, exception.TenantId, exception.Reason, exception.Message,
                exception.RetryAfterSeconds, exception.DetailsJson, apiKeyId: null, exception.TraceId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(
        HttpContext context,
        Guid tenantId,
        RejectionReason reason,
        string detail,
        int retryAfterSeconds,
        string detailsJson,
        Guid? apiKeyId,
        string? traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rejectionEvent = RejectionEvent.Create(reason, context.TraceIdentifier, traceId, apiKeyId, detailsJson);
            await _rejectionEventRepository.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Logging the rejection must never prevent the caller from receiving the 429 itself.
            _logger.LogWarning(ex, "Failed to persist RejectionEvent for tenant {TenantId}, reason {Reason}", tenantId, reason);
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        context.Response.ContentType = "application/problem+json";

        var body = new
        {
            type = "https://orchestai/problems/rate-limited",
            title = "Too Many Requests",
            status = 429,
            detail,
            reason = reason.ToString(),
            retryAfterSeconds
        };

        await context.Response.WriteAsJsonAsync(body, cancellationToken).ConfigureAwait(false);
    }
}
