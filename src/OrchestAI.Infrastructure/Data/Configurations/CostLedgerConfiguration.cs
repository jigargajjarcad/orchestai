using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class CostLedgerConfiguration : IEntityTypeConfiguration<CostLedger>
{
    public void Configure(EntityTypeBuilder<CostLedger> builder)
    {
        builder.ToTable("CostLedger");

        builder.HasKey(cl => cl.Id);

        builder.Property(cl => cl.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(cl => cl.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(cl => cl.OrchestrationTaskId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(cl => cl.AgentExecutionId)
            .HasColumnType("uuid");

        builder.Property(cl => cl.Source)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(CostSource.Production)
            .HasConversion<string>();

        builder.Property(cl => cl.EvalRunId)
            .HasColumnType("uuid");

        builder.Property(cl => cl.Model)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cl => cl.InputTokens)
            .IsRequired();

        builder.Property(cl => cl.OutputTokens)
            .IsRequired();

        builder.Property(cl => cl.CostUsd)
            .IsRequired()
            .HasColumnType("decimal(10,6)");

        builder.Property(cl => cl.RecordedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(cl => cl.OrchestrationTask)
            .WithMany()
            .HasForeignKey(cl => cl.OrchestrationTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cl => cl.AgentExecution)
            .WithMany()
            .HasForeignKey(cl => cl.AgentExecutionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(cl => new { cl.OrchestrationTaskId, cl.RecordedAt })
            .IsDescending(false, true);

        builder.HasIndex(cl => cl.TenantId);

        builder.HasIndex(cl => cl.Source);
        builder.HasIndex(cl => cl.EvalRunId);
    }
}
