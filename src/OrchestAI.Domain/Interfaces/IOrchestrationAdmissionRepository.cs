using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// The one place the admission transaction (task-state CAS + concurrency-slot count + budget
// reservation, all inside one DB transaction with a per-tenant row lock) is implemented. See
// ADR-015 and Task 6's investigation note for why this is deliberately one method, not a
// composition of smaller repository calls.
public interface IOrchestrationAdmissionRepository
{
    Task<AdmissionResult> TryAdmitAsync(
        Guid taskId,
        Guid tenantId,
        int maxConcurrentTasks,
        decimal reservationAmountUsd,
        decimal dailyBudgetUsd,
        decimal actualDailySpendUsd,
        decimal monthlyBudgetUsd,
        decimal actualMonthlySpendUsd,
        TimeSpan reservationStaleness,
        CancellationToken cancellationToken = default);
}
