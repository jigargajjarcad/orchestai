namespace OrchestAI.Domain.Interfaces;

// Marker + accessor for every entity that must be isolated per tenant. TenantId has NO public
// setter and is never a parameter on any Create(...) factory — except the two audited
// system-writer factories, ApiKey.Create and CostRollup.Create (both already reviewed under
// ADR-014 in DECISIONS.md); no request-driven application factory ever takes it. For every
// other entity, the only writer is TenantScopingInterceptor (Task 5), which sets it via
// entry.Property(...).CurrentValue, exactly like UpdatedAtInterceptor stamps UpdatedAt. This
// closes the "client-supplied TenantId" attack surface at the design level, not just at
// runtime-check level.
public interface ITenantScoped
{
    // Every implementation MUST declare this as `{ get; private set; }` (or stricter) — never
    // a public/internal setter, and never a Create(...) parameter. This privacy is load-bearing:
    // TenantScopingInterceptor's tamper check relies on nothing but EF materialization and the
    // interceptor itself ever being able to place a value here, and the interceptor's own
    // KNOWN LIMITATION comment (EnforceTenantScoping, Modified branch) explains exactly what
    // silently breaks for the disconnected-Update() repository pattern if that stops being
    // true. Adding a setter to any implementing entity requires revisiting ADR-014's
    // interceptor section first, not just a review of that one entity.
    Guid TenantId { get; }
}
