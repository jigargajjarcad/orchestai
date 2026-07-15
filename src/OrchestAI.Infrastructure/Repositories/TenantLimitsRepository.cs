using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class TenantLimitsRepository : ITenantLimitsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TenantLimitsRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<TenantLimits?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.TenantLimits
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(TenantLimits limits, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ctx.TenantLimits
            .FirstOrDefaultAsync(x => x.TenantId == limits.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
            ctx.TenantLimits.Add(limits);
        else
            ctx.Entry(existing).CurrentValues.SetValues(limits);

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
