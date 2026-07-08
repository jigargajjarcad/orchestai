using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalResultConfiguration : IEntityTypeConfiguration<EvalResult>
{
    public void Configure(EntityTypeBuilder<EvalResult> builder)
    {
        builder.ToTable("EvalResults");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.EvalRunId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.EvalCaseId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.AgentExecutionId)
            .HasColumnType("uuid");

        builder.Property(r => r.ScorerType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(r => r.ScorerVersion)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.Score)
            .IsRequired()
            .HasColumnType("decimal(5,4)");

        builder.Property(r => r.Passed)
            .IsRequired();

        builder.Property(r => r.ScorerOutput)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(r => r.ScoredAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(r => r.Run)
            .WithMany()
            .HasForeignKey(r => r.EvalRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Case)
            .WithMany()
            .HasForeignKey(r => r.EvalCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.EvalRunId, r.EvalCaseId })
            .IsUnique();

        builder.HasIndex(r => r.AgentExecutionId);
    }
}
