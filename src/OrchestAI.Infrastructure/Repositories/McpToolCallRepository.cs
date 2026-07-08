using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class McpToolCallRepository : IMcpToolCallRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public McpToolCallRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task AddAsync(McpToolCall toolCall, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.McpToolCalls.AddAsync(toolCall, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpToolCallErrorStat>> GetErrorStatsAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await ctx.McpToolCalls
            .Where(tc => tc.AgentExecution.OrchestrationTask.UserId == userId
                && tc.CreatedAt >= fromUtc && tc.CreatedAt < toUtc)
            .Select(tc => new McpToolCallErrorStat(tc.ToolName, tc.Success, tc.ErrorCategory, tc.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
