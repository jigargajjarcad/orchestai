using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class CostLedgerRepository : ICostLedgerRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public CostLedgerRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task AddAsync(CostLedger ledger, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.CostLedger.AddAsync(ledger, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CostLedgerAggregate>> GetDailyAggregatesAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Join server-side (indexed FKs), then group client-side over the bounded, already-filtered
        // result — avoids relying on provider translation of DateOnly.FromDateTime inside a GroupBy.
        var raw = await ctx.CostLedger
            .Where(c => c.RecordedAt >= fromUtc && c.RecordedAt < toUtc
                && c.AgentExecutionId != null && c.Source == CostSource.Production)
            .Join(ctx.OrchestrationTasks, c => c.OrchestrationTaskId, t => t.Id,
                (c, t) => new { c.RecordedAt, t.UserId, c.AgentExecutionId, c.Model, c.InputTokens, c.OutputTokens, c.CostUsd })
            .Join(ctx.AgentExecutions, x => x.AgentExecutionId!.Value, e => e.Id,
                (x, e) => new { x.RecordedAt, x.UserId, e.AgentType, x.Model, x.InputTokens, x.OutputTokens, x.CostUsd })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return raw
            .GroupBy(x => (Date: DateOnly.FromDateTime(x.RecordedAt.UtcDateTime), x.UserId, x.AgentType, x.Model))
            .Select(g => new CostLedgerAggregate(
                g.Key.Date, g.Key.UserId, g.Key.AgentType, g.Key.Model,
                g.Sum(x => x.InputTokens), g.Sum(x => x.OutputTokens), g.Sum(x => x.CostUsd), g.Count()))
            .ToList();
    }

    public async Task<IReadOnlyList<CostLedger>> GetByTaskIdAsync(
        Guid orchestrationTaskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.CostLedger
            .Where(c => c.OrchestrationTaskId == orchestrationTaskId)
            .OrderBy(c => c.RecordedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
