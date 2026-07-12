using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Data.Interceptors;

// Mirrors UpdatedAtInterceptor's shape exactly, but enforces a security boundary instead of a
// convenience timestamp: TenantId is stamped on every new ITenantScoped entity from the ambient
// ICurrentTenantAccessor, and any attempt to persist a mismatched or later-changed TenantId is
// rejected outright rather than silently corrected. See ADR-014 confirmation #3.
public sealed class TenantScopingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentTenantAccessor _tenantAccessor;

    public TenantScopingInterceptor(ICurrentTenantAccessor tenantAccessor)
    {
        _tenantAccessor = tenantAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnforceTenantScoping(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        EnforceTenantScoping(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnforceTenantScoping(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            var tenantProperty = entry.Property(nameof(ITenantScoped.TenantId));

            if (entry.State is EntityState.Added)
            {
                var suppliedTenantId = (Guid)(tenantProperty.CurrentValue ?? Guid.Empty);

                if (suppliedTenantId != Guid.Empty)
                {
                    if (_tenantAccessor.TenantId is not { } activeTenantId || suppliedTenantId != activeTenantId)
                        throw new TenantContextViolationException(
                            $"Attempted to persist a new {entry.Entity.GetType().Name} with TenantId " +
                            $"{suppliedTenantId}, which does not match the current tenant context.");

                    continue;
                }

                if (_tenantAccessor.TenantId is not { } tenantId)
                    throw new TenantContextViolationException(
                        $"Cannot persist a new {entry.Entity.GetType().Name} — no tenant context is resolved.");

                tenantProperty.CurrentValue = tenantId;
            }
            else if (entry.State is EntityState.Modified && tenantProperty.IsModified)
            {
                throw new TenantContextViolationException(
                    $"TenantId on an existing {entry.Entity.GetType().Name} must never change after creation.");
            }
        }
    }
}
