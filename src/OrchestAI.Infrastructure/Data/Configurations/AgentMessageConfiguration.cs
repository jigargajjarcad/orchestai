using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class AgentMessageConfiguration : IEntityTypeConfiguration<AgentMessage>
{
    public void Configure(EntityTypeBuilder<AgentMessage> builder)
    {
        builder.ToTable("AgentMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(m => m.AgentExecutionId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(m => m.Role)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion<string>();

        builder.Property(m => m.Content)
            .IsRequired();

        builder.Property(m => m.SequenceOrder)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(m => m.AgentExecution)
            .WithMany(e => e.Messages)
            .HasForeignKey(m => m.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.TenantId);
    }
}
