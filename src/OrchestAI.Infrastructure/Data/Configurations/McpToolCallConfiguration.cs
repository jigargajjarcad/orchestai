using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class McpToolCallConfiguration : IEntityTypeConfiguration<McpToolCall>
{
    public void Configure(EntityTypeBuilder<McpToolCall> builder)
    {
        builder.ToTable("McpToolCalls");

        builder.HasKey(tc => tc.Id);

        builder.Property(tc => tc.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(tc => tc.AgentExecutionId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(tc => tc.ToolName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(tc => tc.InputParameters)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'");

        builder.Property(tc => tc.OutputResult);

        builder.Property(tc => tc.Success)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(tc => tc.ErrorMessage);

        builder.Property(tc => tc.DurationMs);

        builder.Property(tc => tc.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(tc => tc.AgentExecution)
            .WithMany(e => e.ToolCalls)
            .HasForeignKey(tc => tc.AgentExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
