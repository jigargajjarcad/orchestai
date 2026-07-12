using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class OrchestrationTaskConfiguration : IEntityTypeConfiguration<OrchestrationTask>
{
    public void Configure(EntityTypeBuilder<OrchestrationTask> builder)
    {
        builder.ToTable("OrchestrationTasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(t => t.UserId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(t => t.UserPrompt)
            .IsRequired();

        builder.Property(t => t.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(OrchestrationTaskStatus.Pending)
            .HasConversion<string>();

        builder.Property(t => t.FinalResult);

        builder.Property(t => t.TotalInputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(t => t.TotalOutputTokens)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(t => t.TotalCostUsd)
            .IsRequired()
            .HasColumnType("decimal(10,6)")
            .HasDefaultValue(0m);

        builder.Property(t => t.ErrorMessage);

        builder.Property(t => t.RequireApproval)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.ApprovalStatus)
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(t => t.ApprovalRequestedAt)
            .HasColumnType("timestamptz");

        builder.Property(t => t.ApprovedAt)
            .HasColumnType("timestamptz");

        builder.Property(t => t.ApprovalNote);

        builder.Property(t => t.TraceId)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValueSql("replace(gen_random_uuid()::text, '-', '')");

        builder.Property(t => t.ResumedAt)
            .HasColumnType("timestamptz");

        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.CompletedAt)
            .HasColumnType("timestamptz");

        builder.HasOne(t => t.User)
            .WithMany(u => u.Tasks)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.AgentExecutions)
            .WithOne(e => e.OrchestrationTask)
            .HasForeignKey(e => e.OrchestrationTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.UserId, t.CreatedAt })
            .IsDescending(false, true);

        builder.HasIndex(t => t.TenantId);

        builder.HasIndex(t => t.Status);

        builder.HasIndex(t => t.TraceId)
            .IsUnique();
    }
}
