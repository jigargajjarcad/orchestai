using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class OrchestrationTaskRepository : IOrchestrationTaskRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public OrchestrationTaskRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<OrchestrationTask?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.OrchestrationTasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OrchestrationTask?> GetByIdWithExecutionsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.OrchestrationTasks
            .Include(t => t.AgentExecutions)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OrchestrationTask?> GetByIdWithExecutionsAndMessagesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.OrchestrationTasks
            .Include(t => t.AgentExecutions)
                .ThenInclude(e => e.Messages)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OrchestrationTask?> GetByIdWithExecutionsMessagesAndToolCallsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.OrchestrationTasks
            .Include(t => t.AgentExecutions)
                .ThenInclude(e => e.Messages)
            .Include(t => t.AgentExecutions)
                .ThenInclude(e => e.ToolCalls)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(
        OrchestrationTask task,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.OrchestrationTasks.AddAsync(task, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        OrchestrationTask task,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        ctx.OrchestrationTasks.Update(task);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
