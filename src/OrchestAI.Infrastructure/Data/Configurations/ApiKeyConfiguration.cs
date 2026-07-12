using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

// Minimal stub — Task 6 owns the full migration/column/index configuration for this table
// (including the CK_ApiKeys_TenantId_NotDefault check constraint). This exists only so
// Task 4's AppDbContext.OnModelCreating (which references TenantConfiguration/ApiKeyConfiguration)
// compiles standalone, ahead of Task 6's execution.
public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(k => k.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(k => k.PublicKeyId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(k => k.HashedSecret)
            .IsRequired();

        builder.Property(k => k.DisplayName)
            .HasMaxLength(255);

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

        builder.HasIndex(k => k.PublicKeyId)
            .IsUnique();

        builder.HasIndex(k => k.TenantId);
    }
}
