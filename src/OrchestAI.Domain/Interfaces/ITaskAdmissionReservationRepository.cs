using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

// Plain reads and release — the atomic admission WRITE (CAS + concurrency/budget check +
// insert, all in one transaction) lives on IOrchestrationAdmissionRepository (Task 6), not
// here, because that atomicity only holds if every step of it shares one transaction.
public interface ITaskAdmissionReservationRepository
{
    Task<TaskAdmissionReservation?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);

    // Idempotent: deleting an already-released (or never-existing) reservation is a no-op, not
    // an error — Task 7's try/finally must be safe to call this on every exit path without
    // first checking whether admission actually succeeded.
    Task ReleaseAsync(Guid taskId, CancellationToken cancellationToken = default);
}
