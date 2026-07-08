using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class ModelPricingRepository : IModelPricingRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ModelPricingRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<ModelPricing?> GetByModelAsync(string model, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.ModelPricing
            .FirstOrDefaultAsync(p => p.Model == model, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.ModelPricing
            .OrderBy(p => p.Model)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(ModelPricing pricing, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await ctx.ModelPricing
            .FirstOrDefaultAsync(p => p.Model == pricing.Model, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
            await ctx.ModelPricing.AddAsync(pricing, cancellationToken).ConfigureAwait(false);
        else
            existing.UpdatePricing(pricing.InputPerMillion, pricing.OutputPerMillion);

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
