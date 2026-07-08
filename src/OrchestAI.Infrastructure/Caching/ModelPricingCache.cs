using Microsoft.Extensions.DependencyInjection;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Caching;

// Singleton — pricing changes rarely, so every agent turn doesn't need a DB round trip.
// Resolves IModelPricingRepository through a scope on each refresh since repositories are
// registered Scoped by convention (they only capture IDbContextFactory, so this is safe).
public sealed class ModelPricingCache : IModelPricingCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyDictionary<string, ModelPricing> _cache = new Dictionary<string, ModelPricing>();
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ModelPricingCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<ModelPricing?> GetAsync(string model, CancellationToken cancellationToken = default)
    {
        if (DateTimeOffset.UtcNow >= _expiresAt)
            await RefreshAsync(cancellationToken).ConfigureAwait(false);

        return _cache.TryGetValue(model, out var pricing) ? pricing : null;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow < _expiresAt)
                return; // another caller already refreshed while we waited on the lock

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IModelPricingRepository>();
            var all = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

            _cache = all.ToDictionary(p => p.Model, p => p);
            _expiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
