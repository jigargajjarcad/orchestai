using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

// Tenant/ApiKey are NOT ITenantScoped — they are the identity/management layer, globally
// visible to admin-bootstrap and auth-middleware code only, never filtered by the tenant query
// filter (there's no "current tenant" to scope a tenant lookup to before one is resolved).
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
}
