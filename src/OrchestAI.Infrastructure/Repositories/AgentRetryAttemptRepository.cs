using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class AgentRetryAttemptRepository : IAgentRetryAttemptRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AgentRetryAttemptRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task AddAsync(AgentRetryAttempt attempt, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.AgentRetryAttempts.AddAsync(attempt, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentRetryAttempt>> GetByAgentExecutionIdAsync(
        Guid agentExecutionId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.AgentRetryAttempts
            .Where(r => r.AgentExecutionId == agentExecutionId)
            .OrderBy(r => r.AttemptNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentRetryAttempt>> GetByAgentExecutionIdsAsync(
        IReadOnlyCollection<Guid> agentExecutionIds, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.AgentRetryAttempts
            .Where(r => agentExecutionIds.Contains(r.AgentExecutionId))
            .OrderBy(r => r.AttemptNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
