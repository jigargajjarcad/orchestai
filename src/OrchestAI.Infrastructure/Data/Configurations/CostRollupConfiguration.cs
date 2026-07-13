using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class CostRollupConfiguration : IEntityTypeConfiguration<CostRollup>
{
    public void Configure(EntityTypeBuilder<CostRollup> builder)
    {
        builder.ToTable("CostRollups");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.Date)
            .IsRequired()
            .HasColumnType("date");

        builder.Property(r => r.UserId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.AgentType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(r => r.Model)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.InputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.OutputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.CostUsd)
            .IsRequired()
            .HasColumnType("decimal(10,6)")
            .HasDefaultValue(0m);

        builder.Property(r => r.ExecutionCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.Date, r.UserId, r.AgentType, r.Model })
            .IsUnique();

        builder.HasIndex(r => r.TenantId);

        builder.HasIndex(r => new { r.UserId, r.Date });
    }
}
