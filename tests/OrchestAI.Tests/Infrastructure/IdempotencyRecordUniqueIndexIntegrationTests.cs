using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Task 4 code review fix: proves, against the REAL Postgres unique (TenantId, IdempotencyKey)
// index, the two gaps a mocked-repository or InMemory-provider test cannot see — the InMemory
// provider used by CreateOrchestrationTaskIdempotencyTests never enforces unique constraints at
// all (same documented limitation as CostRollupUniqueIndexIntegrationTests /
// TenantFilterExecuteDeleteTests):
//   1. Critical: GetByKeyAsync correctly reports an expired key as "not found" (its filter is
//      read-only), but nothing used to DELETE the stale expired row — so the very next reuse of
//      that key deterministically collided with it on insert instead of succeeding.
//   2. Important: a still-valid conflicting row (the accepted concurrent-race case from the
//      brief's design note) used to surface as an unhandled DbUpdateException/500 rather than the
//      typed IdempotencyKeyConflictException IdempotencyRecordRepository.AddAsync now throws.
// Runs against the real local dev Postgres (docker-compose.yml), same pattern as
// CostRollupUniqueIndexIntegrationTests / TenantFilterExecuteDeleteTests. Every test wraps its
// work in one transaction that is always rolled back, so the shared dev database is left
// untouched regardless of pass/fail.
public sealed class IdempotencyRecordUniqueIndexIntegrationTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    // Same shared-connection/shared-transaction factory as TenantFilterExecuteDeleteTests — every
    // fresh-context-per-call repository operation must be visible to every other one within the
    // test, and the whole thing rolls back as one unit at the end.
    private sealed class TransactionScopedDbContextFactory(
        DbContextOptions<AppDbContext> options, NpgsqlTransaction transaction, ICurrentTenantAccessor accessor)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var ctx = new AppDbContext(options, accessor);
            ctx.Database.UseTransaction(transaction);
            return ctx;
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task ReusingAnExpiredKey_DeletesTheStaleRowAndSucceeds()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection)
                .AddInterceptors(new UpdatedAtInterceptor(), new TenantScopingInterceptor(accessor))
                .Options;
            var factory = new TransactionScopedDbContextFactory(options, transaction, accessor);

            var tenant = Tenant.Create("Idempotency Test Tenant", $"idem-tenant-{Guid.NewGuid():N}");
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Tenants.Add(tenant);
                await ctx.SaveChangesAsync();
            }

            const string key = "expired-reuse-key";
            Guid userId;
            using (accessor.SetTenant(tenant.Id))
            {
                await using var ctx = await factory.CreateDbContextAsync();
                var user = User.Create($"idem-expired-{Guid.NewGuid():N}@test.local", "Test User");
                ctx.Users.Add(user);

                // Seed an EXPIRED record already occupying this tenant's (TenantId, IdempotencyKey)
                // slot. TaskId is an arbitrary Guid — IdempotencyRecordConfiguration has no FK from
                // TaskId to OrchestrationTasks, so this doesn't need a real task behind it; only the
                // physical row needs to exist to collide with the unique index.
                var expiredRecord = IdempotencyRecord.Create(key, Guid.NewGuid(), "irrelevant-hash", TimeSpan.FromHours(-1));
                ctx.IdempotencyRecords.Add(expiredRecord);

                await ctx.SaveChangesAsync();
                userId = user.Id;
            }

            CreateOrchestrationTaskResponse response;
            using (accessor.SetTenant(tenant.Id))
            {
                var taskRepository = new OrchestrationTaskRepository(factory);
                var idempotencyRepository = new IdempotencyRecordRepository(factory);
                var handler = new CreateOrchestrationTaskHandler(
                    taskRepository,
                    idempotencyRepository,
                    Options.Create(new AbuseProtectionOptions { IdempotencyKeyTtlHours = 24 }),
                    NullLogger<CreateOrchestrationTaskHandler>.Instance);

                // Act: reusing the same key for a brand-new submission must succeed (201-equivalent,
                // a new task created) rather than throwing — this is the deterministic Critical bug:
                // before the fix, this collided on the still-present expired row every time.
                response = await handler.Handle(
                    new CreateOrchestrationTaskCommand(userId, "New Task After Expiry", "New Prompt", false, key),
                    CancellationToken.None);
            }

            response.Status.Should().Be("Pending");
            response.Title.Should().Be("New Task After Expiry");

            using (accessor.SetTenant(tenant.Id))
            {
                await using var verifyCtx = await factory.CreateDbContextAsync();

                var task = await verifyCtx.OrchestrationTasks.FirstOrDefaultAsync(t => t.Id == response.Id);
                task.Should().NotBeNull("the handler must have actually persisted the new task, not just returned a response");

                var records = await verifyCtx.IdempotencyRecords.Where(r => r.IdempotencyKey == key).ToListAsync();
                records.Should().ContainSingle(
                    "the stale expired row must be gone, replaced by exactly one new record");
                records.Single().TaskId.Should().Be(response.Id);
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task InsertConflictingWithANonExpiredRecord_ThrowsIdempotencyKeyConflictExceptionWithTheWinningTaskId()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection)
                .AddInterceptors(new UpdatedAtInterceptor(), new TenantScopingInterceptor(accessor))
                .Options;
            var factory = new TransactionScopedDbContextFactory(options, transaction, accessor);

            var tenant = Tenant.Create("Idempotency Race Tenant", $"idem-race-tenant-{Guid.NewGuid():N}");
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Tenants.Add(tenant);
                await ctx.SaveChangesAsync();
            }

            const string key = "race-key";
            var winningTaskId = Guid.NewGuid();

            using (accessor.SetTenant(tenant.Id))
            {
                // Simulates the "winner" of a concurrent race: a still-valid (NOT expired) record
                // already committed for this key before our insert below runs.
                await using var ctx = await factory.CreateDbContextAsync();
                var winningRecord = IdempotencyRecord.Create(key, winningTaskId, "winning-hash", TimeSpan.FromHours(24));
                ctx.IdempotencyRecords.Add(winningRecord);
                await ctx.SaveChangesAsync();
            }

            using (accessor.SetTenant(tenant.Id))
            {
                var idempotencyRepository = new IdempotencyRecordRepository(factory);

                // The "loser": a different record for the SAME key, which must collide on the real
                // unique index — this exercises IdempotencyRecordRepository.AddAsync's DbUpdateException
                // catch/re-fetch/classify logic against the actual Npgsql PostgresException shape
                // (SqlState 23505, ConstraintName), which cannot be proven with a mock or InMemory.
                var losingRecord = IdempotencyRecord.Create(key, Guid.NewGuid(), "losing-hash", TimeSpan.FromHours(24));

                var act = () => idempotencyRepository.AddAsync(losingRecord, CancellationToken.None);

                var exception = await act.Should().ThrowAsync<IdempotencyKeyConflictException>(
                    "a still-valid conflicting record must surface as a typed conflict, not an unhandled DbUpdateException");
                exception.Which.ExistingTaskId.Should().Be(winningTaskId);
            }

            using (accessor.SetTenant(tenant.Id))
            {
                await using var verifyCtx = await factory.CreateDbContextAsync();
                var records = await verifyCtx.IdempotencyRecords.Where(r => r.IdempotencyKey == key).ToListAsync();
                records.Should().ContainSingle(r => r.TaskId == winningTaskId,
                    "the winning record must be untouched — the loser's insert must not have been retried or partially applied");
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
