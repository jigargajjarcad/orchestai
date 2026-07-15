using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Exceptions;

// Thrown by every synchronous enforcement point (admission's concurrency/budget checks — Task
// 6/7; queue backpressure — Task 10) that needs to reject an in-flight HTTP request with the
// unified 429 contract. Carries TenantId explicitly because the ambient ICurrentTenantAccessor
// scope has already unwound by the time global exception handling runs — see Task 2's
// investigation note. NOT used for AgentCapExceeded (Task 8), which happens inside a detached
// background dispatch with no HTTP response to write to, and is handled inline there instead.
public sealed class TenantLimitExceededException : Exception
{
    public Guid TenantId { get; }
    public RejectionReason Reason { get; }
    public int RetryAfterSeconds { get; }
    public string DetailsJson { get; }
    public string? TraceId { get; }

    public TenantLimitExceededException(
        Guid tenantId,
        RejectionReason reason,
        string message,
        int retryAfterSeconds,
        string detailsJson,
        string? traceId = null)
        : base(message)
    {
        TenantId = tenantId;
        Reason = reason;
        RetryAfterSeconds = retryAfterSeconds;
        DetailsJson = detailsJson;
        TraceId = traceId;
    }
}
