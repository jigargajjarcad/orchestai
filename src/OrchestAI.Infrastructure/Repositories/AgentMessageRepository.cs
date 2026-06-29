using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class AgentMessageRepository : IAgentMessageRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AgentMessageRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task AddAsync(AgentMessage message, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.AgentMessages.AddAsync(message, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentMessage>> GetByExecutionIdAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.AgentMessages
            .Where(m => m.AgentExecutionId == executionId)
            .OrderBy(m => m.SequenceOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
