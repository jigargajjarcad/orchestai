using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class TaskCheckpointConfiguration : IEntityTypeConfiguration<TaskCheckpoint>
{
    public void Configure(EntityTypeBuilder<TaskCheckpoint> builder)
    {
        builder.ToTable("TaskCheckpoints");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(c => c.OrchestrationTaskId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(c => c.AgentType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(c => c.AgentExecutionId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(c => c.Output)
            .IsRequired();

        builder.Property(c => c.InputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(c => c.OutputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(c => c.CostUsd)
            .IsRequired()
            .HasColumnType("decimal(10,6)")
            .HasDefaultValue(0m);

        builder.Property(c => c.CheckpointedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(c => c.OrchestrationTask)
            .WithMany()
            .HasForeignKey(c => c.OrchestrationTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.TenantId);

        builder.HasIndex(c => new { c.OrchestrationTaskId, c.AgentType })
            .IsUnique();
    }
}
