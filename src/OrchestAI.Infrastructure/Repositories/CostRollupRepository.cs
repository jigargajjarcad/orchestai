using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class CostRollupRepository : ICostRollupRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public CostRollupRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task UpsertAsync(CostRollup rollup, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await ctx.CostRollups
            .FirstOrDefaultAsync(
                r => r.Date == rollup.Date && r.UserId == rollup.UserId
                    && r.AgentType == rollup.AgentType && r.Model == rollup.Model,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await ctx.CostRollups.AddAsync(rollup, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existing.ReplaceValues(rollup.InputTokens, rollup.OutputTokens, rollup.CostUsd, rollup.ExecutionCount);
        }

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CostRollup>> GetByDateRangeAsync(
        DateOnly from, DateOnly to, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var query = ctx.CostRollups.Where(r => r.Date >= from && r.Date <= to);
        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);

        return await query
            .OrderBy(r => r.Date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DateOnly?> GetLastRolledUpDateAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.CostRollups
            .OrderByDescending(r => r.Date)
            .Select(r => (DateOnly?)r.Date)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
