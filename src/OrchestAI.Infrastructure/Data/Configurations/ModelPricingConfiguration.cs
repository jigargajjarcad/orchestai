using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class ModelPricingConfiguration : IEntityTypeConfiguration<ModelPricing>
{
    public void Configure(EntityTypeBuilder<ModelPricing> builder)
    {
        builder.ToTable("ModelPricing");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.Model)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.InputPerMillion)
            .IsRequired()
            .HasColumnType("decimal(10,4)");

        builder.Property(p => p.OutputPerMillion)
            .IsRequired()
            .HasColumnType("decimal(10,4)");

        builder.Property(p => p.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(p => p.Model)
            .IsUnique();
    }
}
