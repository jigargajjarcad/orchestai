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

    // True only inside CostRollupBackgroundService's per-tick operation (Task 12) — the ONE
    // audited, narrow exception where TenantScopingInterceptor skips its normal auto-stamp/reject
    // enforcement, because this job legitimately writes many different tenants' rows in one
    // batch. See ADR-014 confirmation #5b. Grep for BeginSystemWriteScope call sites to audit
    // this remains the only caller.
    bool IsSystemWriteScope { get; }
    IDisposable BeginSystemWriteScope();
}
