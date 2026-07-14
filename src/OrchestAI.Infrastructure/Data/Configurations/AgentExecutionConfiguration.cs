using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class AgentExecutionConfiguration : IEntityTypeConfiguration<AgentExecution>
{
    public void Configure(EntityTypeBuilder<AgentExecution> builder)
    {
        builder.ToTable("AgentExecutions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(e => e.OrchestrationTaskId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(e => e.AgentType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(ExecutionStatus.Pending)
            .HasConversion<string>();

        builder.Property(e => e.InputPrompt)
            .IsRequired();

        builder.Property(e => e.OutputResult);

        builder.Property(e => e.InputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.OutputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.CostUsd)
            .IsRequired()
            .HasColumnType("decimal(10,6)")
            .HasDefaultValue(0m);

        builder.Property(e => e.ErrorMessage);

        builder.Property(e => e.ErrorCategory)
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(e => e.SpanId)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValueSql("substring(replace(gen_random_uuid()::text, '-', ''), 1, 16)");

        builder.Property(e => e.ParentSpanId)
            .HasMaxLength(16);

        builder.Property(e => e.MemoriesInjectedCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.EvalRunId)
            .HasColumnType("uuid");

        builder.Property(e => e.StartedAt)
            .HasColumnType("timestamptz");

        builder.Property(e => e.CompletedAt)
            .HasColumnType("timestamptz");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(e => e.OrchestrationTask)
            .WithMany(t => t.AgentExecutions)
            .HasForeignKey(e => e.OrchestrationTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TenantId);

        builder.HasIndex(e => e.OrchestrationTaskId);

        builder.HasIndex(e => e.SpanId)
            .IsUnique();

        builder.HasIndex(e => e.ParentSpanId);

        builder.HasIndex(e => new { e.AgentType, e.CreatedAt });

        builder.HasIndex(e => new { e.Status, e.ErrorCategory });

        builder.HasIndex(e => e.EvalRunId);
    }
}
