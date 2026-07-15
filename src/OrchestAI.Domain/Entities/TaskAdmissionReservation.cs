using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// Operational state, not audit state (see DESIGN_PRINCIPLES.md "Operational state vs. audit
// state") — a temporary capacity hold covering both the concurrency slot and the budget
// reservation for one in-flight task admission. TaskId is the primary key (1:1 with
// OrchestrationTask, see TaskAdmissionReservationConfiguration). Released in full (deleted)
// when the task reaches a terminal state; a row surviving past
// AbuseProtectionOptions.ReservationStalenessMinutes is excluded from admission math (crash
// recovery — see ADR-015). ITenantScoped: always created inside the live ambient-tenant scope
// of the /start request, never given TenantId directly.
public sealed class TaskAdmissionReservation : ITenantScoped
{
    private TaskAdmissionReservation() { }

    public Guid TaskId { get; private set; }
    public Guid TenantId { get; private set; }
    public decimal ReservedCostUsd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static TaskAdmissionReservation Create(Guid taskId, decimal reservedCostUsd)
    {
        return new TaskAdmissionReservation
        {
            TaskId = taskId,
            ReservedCostUsd = reservedCostUsd,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
