using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalRunConfiguration : IEntityTypeConfiguration<EvalRun>
{
    public void Configure(EntityTypeBuilder<EvalRun> builder)
    {
        builder.ToTable("EvalRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.SuiteId)
            .HasColumnType("uuid");

        builder.Property(r => r.Source)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(EvalRunSource.LiveSuite)
            .HasConversion<string>();

        builder.Property(r => r.TriggeredAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(EvalRunStatus.Pending)
            .HasConversion<string>();

        builder.Property(r => r.BaselineRunId)
            .HasColumnType("uuid");

        builder.Property(r => r.SubjectVersion)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Rubric)
            .HasColumnType("text");

        builder.Property(r => r.SelectionCriteriaJson)
            .HasColumnType("jsonb");

        builder.Property(r => r.SkippedAlreadyScoredCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.ForceRescore)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.ErrorMessage);

        builder.Property(r => r.CompletedAt)
            .HasColumnType("timestamptz");

        builder.HasOne(r => r.Suite)
            .WithMany()
            .HasForeignKey(r => r.SuiteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-referencing FK to the baseline run — restrict delete so a baseline can't be
        // dropped out from under a run that still points at it (the comparison would silently
        // lose meaning). Deleting the *dependent* (newer) run is unaffected.
        builder.HasOne(r => r.BaselineRun)
            .WithMany()
            .HasForeignKey(r => r.BaselineRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.SuiteId);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.Source);
    }
}
