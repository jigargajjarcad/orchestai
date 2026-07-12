namespace OrchestAI.Domain.Interfaces;

// Ambient current-tenant context, read by AppDbContext's global query filters and
// TenantScopingInterceptor, set once per HTTP request (auth middleware) or once per
// background-worker job (explicitly, from the job's persisted TenantId). See ADR-014.
public interface ICurrentTenantAccessor
{
    Guid? TenantId { get; }

    // Sets the ambient tenant for the duration of the returned scope; disposing restores
    // whatever value was ambient before (supports nesting, though nesting isn't expected).
    IDisposable SetTenant(Guid tenantId);
}
