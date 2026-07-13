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
            else if (entry.State is EntityState.Modified)
            {
                // Deliberately compares OriginalValue against CurrentValue rather than trusting
                // tenantProperty.IsModified: every repository in this codebase uses
                // IDbContextFactory (a fresh DbContext per call), so the standard "fetch in one
                // context, mutate the CLR object, then ctx.Set<T>().Update(entity) in a new
                // context" pattern attaches a previously-untracked entity graph. EF Core's
                // Update() marks EVERY scalar property IsModified=true on such a graph (it has no
                // baseline to diff against) — including TenantId, even when its value is
                // identical to what's already persisted. Trusting IsModified here would reject
                // every legitimate status update (EvalRun.MarkRunning/Completed/Failed,
                // OrchestrationTask transitions, etc.) the instant tenant scoping went live.
                // OriginalValue still equals CurrentValue in that false-positive case (EF seeds
                // both from the same CLR value at attach time), so this comparison only trips for
                // an actual attempted change — e.g. the in-context tampering
                // TenantScopingInterceptorTests.SaveChanges_ExistingEntity_TenantIdCannotBeChanged
                // exercises, where the entity was tracked from a real DB fetch and its TenantId
                // was then overwritten in place.
                //
                // KNOWN LIMITATION — read before touching this check: it only detects a changed
                // TenantId for an entity that was already being tracked in THIS SAME DbContext
                // before the change (fetched and mutated in-context, no intervening Update()
                // call). For the disconnected ctx.Set<T>().Update(entity) pattern above — this
                // codebase's standard repository convention, fetch in context A, Update() in
                // context B — EF seeds OriginalValue as a copy of whatever CurrentValue already is
                // on the CLR object at attach time, not from the real database row. So
                // OriginalValue != CurrentValue can never be true for that path, regardless of
                // what TenantId was set to before Update() was called: this check is a structural
                // no-op there. See
                // TenantScopingInterceptorTests.SaveChanges_DetachedEntityWithTamperedTenantId_DoesNotThrow_DocumentingKnownLimitation,
                // which pins this down as an accepted, tested gap rather than an unnoticed one.
                //
                // This is safe ONLY because nothing today can present a tampered TenantId to the
                // disconnected-Update() path in the first place: TenantId has a private setter on
                // every ITenantScoped entity, and the only factory methods that ever set it
                // (ApiKey.Create, CostRollup.Create — both already reviewed under ADR-014) never
                // flow through this Modified/disconnected-Update() branch. In other words, this
                // defense is conditional on the private-setter invariant holding across every
                // ITenantScoped entity forever — it is not an independently-enforced runtime
                // guarantee for the disconnected-update case. Adding a public/internal setter to
                // any ITenantScoped.TenantId, or introducing a new factory/mutation path that sets
                // it post-construction, would silently reopen this gap; re-verify against the
                // fresh-DB-read hardening option (rejected here on cost grounds — an extra
                // round-trip per tenant-scoped update) if that assumption ever changes.
                var currentValue = (Guid)(tenantProperty.CurrentValue ?? Guid.Empty);
                var originalValue = (Guid)(tenantProperty.OriginalValue ?? Guid.Empty);

                if (currentValue != originalValue)
                    throw new TenantContextViolationException(
                        $"TenantId on an existing {entry.Entity.GetType().Name} must never change after creation.");
            }
        }
    }
}
