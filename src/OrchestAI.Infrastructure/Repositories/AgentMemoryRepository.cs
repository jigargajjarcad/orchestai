using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class AgentMemoryRepository : IAgentMemoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public AgentMemoryRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<IReadOnlyList<AgentMemory>> GetRelevantAsync(
        Guid userId, AgentType agentType, int maxEntries = 10, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        return await ctx.AgentMemories
            .Where(m => m.UserId == userId && m.AgentType == agentType
                && (m.ExpiresAt == null || m.ExpiresAt > now))
            .OrderByDescending(m => m.Importance)
            .Take(maxEntries)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentMemory>> GetAllForUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.AgentMemories
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Importance)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentMemory>> GetAllForUserAndAgentTypeAsync(
        Guid userId, AgentType agentType, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.AgentMemories
            .Where(m => m.UserId == userId && m.AgentType == agentType)
            .OrderByDescending(m => m.Importance)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AgentMemory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await ctx.AgentMemories
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(AgentMemory memory, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = await ctx.AgentMemories
            .FirstOrDefaultAsync(
                m => m.UserId == memory.UserId && m.AgentType == memory.AgentType && m.Key == memory.Key,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
            await ctx.AgentMemories.AddAsync(memory, cancellationToken).ConfigureAwait(false);
        else
            existing.UpdateValue(memory.Value, memory.Importance);

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await ctx.AgentMemories
            .Where(m => m.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        await ctx.AgentMemories
            .Where(m => m.ExpiresAt != null && m.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
