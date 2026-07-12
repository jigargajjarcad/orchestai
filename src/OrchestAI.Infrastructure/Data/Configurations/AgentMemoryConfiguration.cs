using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class AgentMemoryConfiguration : IEntityTypeConfiguration<AgentMemory>
{
    public void Configure(EntityTypeBuilder<AgentMemory> builder)
    {
        builder.ToTable("AgentMemories");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(m => m.UserId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(m => m.AgentType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(m => m.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(m => m.Value)
            .IsRequired();

        builder.Property(m => m.Importance)
            .IsRequired()
            .HasDefaultValue(5);

        builder.Property(m => m.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(m => m.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(m => m.ExpiresAt)
            .HasColumnType("timestamptz");

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.TenantId);

        builder.HasIndex(m => new { m.UserId, m.AgentType, m.Key })
            .IsUnique();

        builder.HasIndex(m => new { m.UserId, m.AgentType });

        builder.HasIndex(m => new { m.UserId, m.Importance })
            .IsDescending(false, true);
    }
}
