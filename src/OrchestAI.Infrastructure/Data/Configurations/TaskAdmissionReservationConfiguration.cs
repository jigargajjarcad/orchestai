using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class TaskAdmissionReservationConfiguration : IEntityTypeConfiguration<TaskAdmissionReservation>
{
    public void Configure(EntityTypeBuilder<TaskAdmissionReservation> builder)
    {
        builder.ToTable("TaskAdmissionReservations");
        builder.HasKey(x => x.TaskId);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.Property(x => x.ReservedCostUsd).HasColumnType("decimal(18,6)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<OrchestrationTask>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
