using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Tenancy;

// Backs GET {id}/stream's browser-EventSource-compatible auth (Task 1, Phase 1 architecture/
// product validation): EventSource cannot send an Authorization header, so this mints a
// short-lived, single-use, (tenantId, taskId)-bound opaque ticket instead. Singleton — it owns
// its own in-memory ticket store (an IMemoryCache instance dedicated to this issuer, not the
// shared/global one), so it must never be registered Scoped/Transient. See
// DependencyInjection.AddInfrastructure.
public sealed class InMemoryTaskStreamTicketIssuer : ITaskStreamTicketIssuer, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private readonly bool _ownsCache;

    // Production entry point: 60 seconds is long enough to cover the round-trip from minting the
    // ticket (POST {id}/stream-ticket) to the browser opening the EventSource connection, and
    // short enough that a leaked ticket in browser history/logs is useless within a minute.
    public InMemoryTaskStreamTicketIssuer() : this(new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromSeconds(60))
    {
        _ownsCache = true;
    }

    // Test seam: an injectable cache and TTL let tests exercise expiry with a short real TTL
    // instead of waiting 60 real seconds or introducing a fake clock abstraction nothing else in
    // this codebase currently uses for IMemoryCache-backed time-based caches.
    public InMemoryTaskStreamTicketIssuer(IMemoryCache cache, TimeSpan ttl)
    {
        _cache = cache;
        _ttl = ttl;
    }

    public string Issue(Guid tenantId, Guid taskId)
    {
        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _cache.Set(ticket, (TenantId: tenantId, TaskId: taskId), _ttl);
        return ticket;
    }

    public bool TryConsume(string? ticket, Guid taskId, out Guid tenantId)
    {
        tenantId = Guid.Empty;

        if (string.IsNullOrEmpty(ticket))
            return false;

        if (!_cache.TryGetValue(ticket, out (Guid TenantId, Guid TaskId) entry))
            return false;

        // Remove unconditionally on any lookup hit — single-use even if the taskId doesn't
        // match, so a wrong-task guess can't be retried against the correct task afterwards.
        _cache.Remove(ticket);

        if (entry.TaskId != taskId)
            return false;

        tenantId = entry.TenantId;
        return true;
    }

    public void Dispose()
    {
        if (_ownsCache)
            _cache.Dispose();
    }
}
