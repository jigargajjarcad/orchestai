using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IAgentExecutionRepository
{
    Task<AgentExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(AgentExecution execution, CancellationToken cancellationToken = default);
    Task UpdateAsync(AgentExecution execution, CancellationToken cancellationToken = default);

    // One row per execution owned by userId, CreatedAt within [from, to] inclusive (UTC
    // calendar days) — the input to error-rate monitoring. See ADR-011.
    Task<IReadOnlyList<AgentExecutionErrorStat>> GetErrorStatsAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
