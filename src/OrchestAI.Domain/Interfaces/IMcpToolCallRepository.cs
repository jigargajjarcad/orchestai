using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IMcpToolCallRepository
{
    Task AddAsync(McpToolCall toolCall, CancellationToken cancellationToken = default);

    // One row per tool call owned (transitively, via its AgentExecution) by userId, CreatedAt
    // within [from, to] inclusive (UTC calendar days) — the input to error-rate monitoring.
    Task<IReadOnlyList<McpToolCallErrorStat>> GetErrorStatsAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
