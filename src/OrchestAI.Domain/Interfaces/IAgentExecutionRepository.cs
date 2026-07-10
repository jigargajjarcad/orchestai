using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
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

    // Resolves which AgentExecution rows match post-hoc trace-selection criteria (Week 9). Only
    // Completed executions are eligible — a Pending/Running/Failed execution has no OutputResult
    // to score. When explicitTraceIds is non-empty it is used exclusively (date range ignored);
    // otherwise from/to/agentType filter. TotalMatched is the full match count before `limit` is
    // applied, so callers can detect and reject an over-cap selection instead of silently
    // truncating it.
    Task<TraceSelectionResult> SelectForPostHocScoringAsync(
        DateTimeOffset? from, DateTimeOffset? to, AgentType? agentType,
        IReadOnlyCollection<Guid>? explicitTraceIds, int limit, CancellationToken cancellationToken = default);
}
