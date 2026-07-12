namespace OrchestAI.Domain.Interfaces;

// Marker + accessor for every entity that must be isolated per tenant. TenantId has NO public
// setter and is never a parameter on any Create(...) factory — the only writer is
// TenantScopingInterceptor (Task 5), which sets it via entry.Property(...).CurrentValue,
// exactly like UpdatedAtInterceptor stamps UpdatedAt. This closes the "client-supplied
// TenantId" attack surface at the design level, not just at runtime-check level.
public interface ITenantScoped
{
    Guid TenantId { get; }
}
