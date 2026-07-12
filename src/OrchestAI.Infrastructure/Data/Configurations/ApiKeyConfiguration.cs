using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        // Schema-level backstop for the "default tenant can never authenticate" invariant
        // (Global Constraints, ADR-014 confirmation #8): CreateApiKeyHandler (Task 8) rejects
        // this at the application layer, but a CHECK constraint means the guarantee holds even
        // against a raw SQL insert, a future code path that bypasses the handler, or a bug in
        // the handler itself — the DB refuses the row unconditionally, not "as long as every
        // caller remembers to check." See Tenant.DefaultTenantId (Task 1).
        builder.ToTable("ApiKeys", t => t.HasCheckConstraint(
            "CK_ApiKeys_TenantId_NotDefault",
            "\"TenantId\" <> '00000000-0000-0000-0000-000000000001'"));

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(k => k.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(k => k.PublicKeyId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(k => k.HashedSecret)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(k => k.DisplayName)
            .HasMaxLength(200);

        builder.Property(k => k.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(k => k.LastUsedAt)
            .HasColumnType("timestamptz");

        builder.Property(k => k.RevokedAt)
            .HasColumnType("timestamptz");

        builder.Property(k => k.ExpiresAt)
            .HasColumnType("timestamptz");

        builder.HasOne(k => k.Tenant)
            .WithMany()
            .HasForeignKey(k => k.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(k => k.PublicKeyId).IsUnique();
        builder.HasIndex(k => k.TenantId);
    }
}
