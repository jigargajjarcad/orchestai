using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class AgentRetryAttemptConfiguration : IEntityTypeConfiguration<AgentRetryAttempt>
{
    public void Configure(EntityTypeBuilder<AgentRetryAttempt> builder)
    {
        builder.ToTable("AgentRetryAttempts");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.AgentExecutionId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.AttemptNumber)
            .IsRequired();

        builder.Property(r => r.DelayMs)
            .IsRequired();

        builder.Property(r => r.Reason)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(r => r.AgentExecution)
            .WithMany()
            .HasForeignKey(r => r.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.TenantId);

        builder.HasIndex(r => r.AgentExecutionId);
    }
}
