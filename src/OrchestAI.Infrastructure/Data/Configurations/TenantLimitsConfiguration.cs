using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class TenantLimitsConfiguration : IEntityTypeConfiguration<TenantLimits>
{
    public void Configure(EntityTypeBuilder<TenantLimits> builder)
    {
        builder.ToTable("TenantLimits");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.Property(x => x.DailyCostBudgetUsd).HasColumnType("decimal(18,6)");
        builder.Property(x => x.MonthlyCostBudgetUsd).HasColumnType("decimal(18,6)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
