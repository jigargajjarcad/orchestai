using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface ITenantLimitsRepository
{
    Task<TenantLimits?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task UpsertAsync(TenantLimits limits, CancellationToken cancellationToken = default);
}
