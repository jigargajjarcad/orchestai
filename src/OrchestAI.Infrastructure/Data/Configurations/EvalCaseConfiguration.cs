using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalCaseConfiguration : IEntityTypeConfiguration<EvalCase>
{
    public void Configure(EntityTypeBuilder<EvalCase> builder)
    {
        builder.ToTable("EvalCases");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.SuiteId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(c => c.InputPayload)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(c => c.ExpectedCriteria)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(c => c.ScorerType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(c => c.RegressionThreshold)
            .IsRequired()
            .HasColumnType("decimal(5,4)");

        builder.Property(c => c.Tags)
            .IsRequired()
            .HasMaxLength(500)
            .HasDefaultValue(string.Empty);

        builder.Property(c => c.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(c => c.SuiteId);
    }
}
