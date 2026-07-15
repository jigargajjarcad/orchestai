using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class RejectionEventConfiguration : IEntityTypeConfiguration<RejectionEvent>
{
    public void Configure(EntityTypeBuilder<RejectionEvent> builder)
    {
        builder.ToTable("RejectionEvents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
