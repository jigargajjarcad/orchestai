using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class ApiKeyRepository : IApiKeyRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ApiKeyRepository(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiKey?> GetByPublicKeyIdAsync(string publicKeyId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.ApiKeys.FirstOrDefaultAsync(k => k.PublicKeyId == publicKeyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.ApiKeys.AddAsync(apiKey, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ctx.ApiKeys.Update(apiKey);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
