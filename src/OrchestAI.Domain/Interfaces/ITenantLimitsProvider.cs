using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// The one place every enforcement point (rate limiter, concurrency check, budget validator,
// orchestrator cap, queue manager) reads TenantLimits through — see DESIGN_PRINCIPLES.md
// "Single-choke-point enforcement" and ADR-015 confirmation #8.
public interface ITenantLimitsProvider
{
    Task<ResolvedTenantLimits> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    // Cache-only, synchronous — the ONLY caller is the rate limiter's partition-key factory,
    // which the RateLimitPartition API requires to be synchronous. Returns system defaults for
    // a tenant not yet cached (safe: system defaults are the fail-closed conservative baseline,
    // never "unlimited"). Every other call site must use GetAsync.
    ResolvedTenantLimits GetSnapshot(Guid tenantId);
}
