using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class CostRollupRepository : ICostRollupRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentTenantAccessor _tenantAccessor;

    public CostRollupRepository(IDbContextFactory<AppDbContext> contextFactory, ICurrentTenantAccessor tenantAccessor)
    {
        _contextFactory = contextFactory;
        _tenantAccessor = tenantAccessor;
    }

    public async Task UpsertAsync(CostRollup rollup, CancellationToken cancellationToken = default)
    {
        if (!_tenantAccessor.IsSystemWriteScope)
            throw new InvalidOperationException(
                "UpsertAsync must only be called from within a system-write scope (CostRollupBackgroundService).");

        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // IgnoreQueryFilters() is required here: this method runs inside BeginSystemWriteScope(),
        // where the ambient TenantId is deliberately null (see CostRollupBackgroundService), so the
        // normal tenant query filter (TenantId == ambient) can never match any row. Without this,
        // the "existing" lookup below always returns null even when a row for this exact
        // (Date, TenantId, UserId, AgentType, Model) tuple already exists, causing every re-roll to
        // attempt an INSERT and violate the unique index on the second tick. rollup.TenantId (set
        // explicitly by CostRollup.Create from the authoritative per-row aggregate) disambiguates
        // across tenants since the ambient TenantId isn't available to filter by here.
        var existing = await ctx.CostRollups
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.Date == rollup.Date && r.TenantId == rollup.TenantId && r.UserId == rollup.UserId
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
        if (!_tenantAccessor.IsSystemWriteScope)
            throw new InvalidOperationException(
                "GetLastRolledUpDateAsync must only be called from within a system-write scope (CostRollupBackgroundService).");

        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        // IgnoreQueryFilters() for the same reason as UpsertAsync above: the ambient TenantId is
        // null in this scope, so the normal filter would always yield zero rows and this job would
        // never advance past its "nothing rolled up yet" starting point, re-scanning the full
        // MaxCatchUpDays window on every tick. This is deliberately cross-tenant — the latest date
        // with ANY tenant's rollup determines the trailing lookback window for the whole batch.
        return await ctx.CostRollups
            .IgnoreQueryFilters()
            .OrderByDescending(r => r.Date)
            .Select(r => (DateOnly?)r.Date)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
