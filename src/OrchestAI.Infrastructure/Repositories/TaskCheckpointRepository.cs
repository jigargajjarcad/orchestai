using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class TaskCheckpointRepository : ITaskCheckpointRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TaskCheckpointRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<IReadOnlyList<TaskCheckpoint>> GetByTaskIdAsync(
        Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.TaskCheckpoints
            .Where(c => c.OrchestrationTaskId == taskId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(
        TaskCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await ctx.TaskCheckpoints
            .FirstOrDefaultAsync(
                c => c.OrchestrationTaskId == checkpoint.OrchestrationTaskId
                    && c.AgentType == checkpoint.AgentType,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await ctx.TaskCheckpoints.AddAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.UpdateOutput(
                checkpoint.AgentExecutionId, checkpoint.Output,
                checkpoint.InputTokens, checkpoint.OutputTokens, checkpoint.CostUsd);
        }

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteByTaskIdAsync(
        Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.TaskCheckpoints
            .Where(c => c.OrchestrationTaskId == taskId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
