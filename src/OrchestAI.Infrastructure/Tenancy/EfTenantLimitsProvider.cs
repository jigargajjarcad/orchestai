using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Tenancy;

// Mirrors IModelPricingCache's shape (Week 7): a DB-backed value cached in memory with a short
// refresh interval, because the rate limiter needs a synchronous read on every request and the
// other four enforcement points need a "dashboard-speed," not full-scan, read.
//
// Singleton, so it can't take ITenantLimitsRepository directly (repositories are Scoped by
// convention). Resolves it through a scope on each cache-miss instead — the same fix
// ModelPricingCache uses for the identical Singleton-needs-Scoped-repository shape.
public sealed class EfTenantLimitsProvider : ITenantLimitsProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TenantLimitsDefaults _defaults;
    private readonly TimeSpan _refreshInterval;
    private readonly ConcurrentDictionary<Guid, (ResolvedTenantLimits Limits, DateTimeOffset CachedAt)> _cache = new();

    public EfTenantLimitsProvider(
        IServiceScopeFactory scopeFactory,
        IOptions<TenantLimitsDefaults> defaults,
        IOptions<AbuseProtectionOptions> abuseProtectionOptions)
    {
        _scopeFactory = scopeFactory;
        _defaults = defaults.Value;
        _refreshInterval = TimeSpan.FromSeconds(abuseProtectionOptions.Value.TenantLimitsCacheRefreshSeconds);
    }

    public async Task<ResolvedTenantLimits> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(tenantId, out var cached) && DateTimeOffset.UtcNow - cached.CachedAt < _refreshInterval)
            return cached.Limits;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITenantLimitsRepository>();
        var row = await repository.GetByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var resolved = Resolve(row);
        _cache[tenantId] = (resolved, DateTimeOffset.UtcNow);
        return resolved;
    }

    public ResolvedTenantLimits GetSnapshot(Guid tenantId) =>
        _cache.TryGetValue(tenantId, out var cached) ? cached.Limits : Resolve(null);

    private ResolvedTenantLimits Resolve(Domain.Entities.TenantLimits? row) => new(
        row?.RequestsPerMinute ?? _defaults.RequestsPerMinute,
        row?.MaxConcurrentTasks ?? _defaults.MaxConcurrentTasks,
        row?.MaxAgentsPerTask ?? _defaults.MaxAgentsPerTask,
        row?.MaxToolCallsPerTask ?? _defaults.MaxToolCallsPerTask,
        row?.DailyCostBudgetUsd ?? _defaults.DailyCostBudgetUsd,
        row?.MonthlyCostBudgetUsd ?? _defaults.MonthlyCostBudgetUsd,
        row?.MaxQueueDepth ?? _defaults.MaxQueueDepth);
}
