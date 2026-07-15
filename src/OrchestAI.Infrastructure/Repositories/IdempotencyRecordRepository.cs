using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class IdempotencyRecordRepository : IIdempotencyRecordRepository
{
    // Must match IdempotencyRecordConfiguration's HasIndex(...).IsUnique() name exactly — used to
    // distinguish "this specific idempotency conflict" from any other unique-violation a future
    // caller of AddAsync might somehow hit, so we never swallow an unrelated constraint error.
    private const string UniqueIndexName = "IX_IdempotencyRecords_TenantId_IdempotencyKey";

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public IdempotencyRecordRepository(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<IdempotencyRecord?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.IdempotencyRecords
            .Where(r => r.IdempotencyKey == idempotencyKey && r.ExpiresAt > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // Review fix (Task 4): a plain unconditional insert here left two real gaps against the
    // unique (TenantId, IdempotencyKey) index, both invisible to GetByKeyAsync's expiry filter
    // because that filter only affects READS:
    //   1. TTL-reuse (deterministic, Critical): GetByKeyAsync correctly reports an expired key as
    //      "not found," so the handler proceeds to create a new task and insert a new record —
    //      but the stale expired row is still physically present, so this insert collides with it
    //      on the very next reuse of that key, every time.
    //   2. Concurrent race (accepted-but-unhandled, Important): two simultaneous first-uses of the
    //      same brand-new key both pass the handler's "not found" check before either commits —
    //      the index correctly accepts only one, but the loser got an unhandled DbUpdateException
    //      instead of the brief's stated "one extra task" outcome.
    // Both are now handled here, entirely inside Infrastructure, so the Application layer never
    // needs EF/Npgsql-specific knowledge: on a unique-index hit, re-fetch the conflicting row
    // WITHOUT the expiry filter (bypassing GetByKeyAsync, not the tenant filter — the ambient
    // tenant scope still applies) to tell the two cases apart.
    public async Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            await InsertAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsIdempotencyKeyUniqueViolation(ex))
        {
            var conflicting = await GetConflictingRecordIncludingExpiredAsync(record.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (conflicting is not null && conflicting.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                // Case 1 — TTL reuse: the stale mapping is gone once deleted; retry the insert
                // exactly once. A second conflict here (a doubly-rare race landing in the tiny
                // window between the delete and this retry) is deliberately NOT retried again —
                // it propagates as an ordinary DbUpdateException rather than looping.
                await DeleteAsync(conflicting.Id, cancellationToken).ConfigureAwait(false);
                await InsertAsync(record, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Case 2 — concurrent race: a still-valid record already claimed this key. Surface a
            // typed exception carrying the winning TaskId so CreateOrchestrationTaskHandler can
            // return that task instead of the caller ever seeing a 500. (If the conflicting row
            // vanished between the failed insert and this re-fetch — e.g. its own TTL expired and
            // something else cleaned it up in that instant — there is no winning task to point to;
            // that is treated as the same unresolved-conflict condition rather than silently
            // dropped.)
            throw new IdempotencyKeyConflictException(
                conflicting?.TaskId ?? throw new InvalidOperationException(
                    $"Unique-index conflict on Idempotency-Key '{record.IdempotencyKey}' could not be resolved — " +
                    "the conflicting row was not found on re-fetch."));
        }
    }

    private async Task InsertAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.IdempotencyRecords.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // Deliberately does NOT apply GetByKeyAsync's `ExpiresAt > now` predicate — the whole point is
    // to see the conflicting row regardless of expiry. Still respects the ambient tenant query
    // filter (this is not IgnoreQueryFilters()); the unique index itself is per-tenant, so the
    // conflict this is resolving can only ever be within the current tenant scope anyway.
    private async Task<IdempotencyRecord?> GetConflictingRecordIncludingExpiredAsync(
        string idempotencyKey, CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.IdempotencyRecords
            .Where(r => r.IdempotencyKey == idempotencyKey)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.IdempotencyRecords
            .Where(r => r.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsIdempotencyKeyUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg
        && pg.ConstraintName == UniqueIndexName;
}
