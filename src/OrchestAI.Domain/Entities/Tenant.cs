using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class Tenant
{
    // The well-known backfill/system tenant (Task 6). Structurally unauthenticatable at two
    // independent layers: CreateApiKeyHandler (Task 8) rejects it explicitly, and a Postgres
    // CHECK constraint on ApiKeys.TenantId (Task 6, CK_ApiKeys_TenantId_NotDefault) refuses the
    // row even from a raw SQL insert. Defined here, in Domain, so both Infrastructure (the
    // migration/seeder) and Application (the CreateApiKeyHandler guard) reference one source of
    // truth without Application depending on Infrastructure (see Global Constraints and
    // LayeringTests).
    public static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Tenant() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }

    public static Tenant Create(string name, string slug)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Suspend()
    {
        Status = TenantStatus.Suspended;
        SuspendedAt = DateTimeOffset.UtcNow;
    }

    public void Reactivate()
    {
        Status = TenantStatus.Active;
        SuspendedAt = null;
    }
}
