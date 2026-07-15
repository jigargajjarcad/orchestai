using FluentAssertions;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Tests.Domain;

public sealed class TaskAdmissionReservationTests
{
    [Fact]
    public void Create_SetsTaskIdAndReservedCost()
    {
        var taskId = Guid.NewGuid();

        var reservation = TaskAdmissionReservation.Create(taskId, 12.50m);

        reservation.TaskId.Should().Be(taskId);
        reservation.ReservedCostUsd.Should().Be(12.50m);
        reservation.CreatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }
}
