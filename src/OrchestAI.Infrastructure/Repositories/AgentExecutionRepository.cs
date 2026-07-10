using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class AgentExecutionRepository : IAgentExecutionRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AgentExecutionRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<AgentExecution?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.AgentExecutions
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(
        AgentExecution execution,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.AgentExecutions.AddAsync(execution, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        AgentExecution execution,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        ctx.AgentExecutions.Update(execution);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentExecutionErrorStat>> GetErrorStatsAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await ctx.AgentExecutions
            .Where(e => e.OrchestrationTask.UserId == userId
                && e.CreatedAt >= fromUtc && e.CreatedAt < toUtc)
            .Select(e => new AgentExecutionErrorStat(e.Id, e.AgentType, e.Status, e.ErrorCategory, e.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TraceSelectionResult> SelectForPostHocScoringAsync(
        DateTimeOffset? from, DateTimeOffset? to, AgentType? agentType,
        IReadOnlyCollection<Guid>? explicitTraceIds, int limit, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var query = ctx.AgentExecutions.Where(e => e.Status == ExecutionStatus.Completed);

        if (explicitTraceIds is { Count: > 0 })
        {
            query = query.Where(e => explicitTraceIds.Contains(e.Id));
        }
        else
        {
            if (from.HasValue) query = query.Where(e => e.CreatedAt >= from.Value);
            if (to.HasValue) query = query.Where(e => e.CreatedAt <= to.Value);
        }

        if (agentType.HasValue)
            query = query.Where(e => e.AgentType == agentType.Value);

        var totalMatched = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var ids = await query
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new TraceSelectionResult(ids, totalMatched);
    }
}
