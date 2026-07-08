using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalSuiteConfiguration : IEntityTypeConfiguration<EvalSuite>
{
    public void Configure(EntityTypeBuilder<EvalSuite> builder)
    {
        builder.ToTable("EvalSuites");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Description)
            .IsRequired();

        builder.Property(s => s.TargetAgentType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasMany(s => s.Cases)
            .WithOne(c => c.Suite)
            .HasForeignKey(c => c.SuiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.TargetAgentType);
    }
}
