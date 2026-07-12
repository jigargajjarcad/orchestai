namespace OrchestAI.Application.Exceptions;

// Thrown by TenantScopingInterceptor (Task 5) when a write would cross a tenant boundary —
// either no tenant context is resolved, or an entity already carries a TenantId that doesn't
// match the current ambient tenant. Controllers map this to 403 Forbidden (Task 9) — this is a
// security-boundary violation, not an ordinary validation error.
public sealed class TenantContextViolationException : Exception
{
    public TenantContextViolationException(string message) : base(message) { }
}
