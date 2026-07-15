using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class RejectionEventRepository : IRejectionEventRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public RejectionEventRepository(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task AddAsync(RejectionEvent rejectionEvent, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.RejectionEvents.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RejectionEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.RejectionEvents
            .OrderByDescending(r => r.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
