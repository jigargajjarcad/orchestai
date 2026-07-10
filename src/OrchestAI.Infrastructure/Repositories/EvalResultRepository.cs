using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class EvalResultRepository : IEvalResultRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EvalResultRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<IReadOnlyList<EvalResult>> GetByRunIdAsync(
        Guid evalRunId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalResults
            .Where(r => r.EvalRunId == evalRunId)
            .OrderBy(r => r.ScoredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(EvalResult result, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.EvalResults.AddAsync(result, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> GetScoredAgentExecutionIdsAsync(
        IReadOnlyCollection<Guid> agentExecutionIds, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalResults
            .Where(r => r.EvalCaseId == null
                && r.AgentExecutionId != null
                && agentExecutionIds.Contains(r.AgentExecutionId!.Value)
                && r.ScorerType == scorerType
                && r.ScorerVersion == scorerVersion)
            .Select(r => r.AgentExecutionId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeletePostHocResultAsync(
        Guid agentExecutionId, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ctx.EvalResults
            .Where(r => r.EvalCaseId == null
                && r.AgentExecutionId == agentExecutionId
                && r.ScorerType == scorerType
                && r.ScorerVersion == scorerVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing.Count == 0) return;

        ctx.EvalResults.RemoveRange(existing);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
