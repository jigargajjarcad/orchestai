using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Neither ExecuteUpdateAsync nor FOR UPDATE translate against EF Core's InMemory provider — same
// limitation TenantFilterExecuteDeleteTests documents. Runs against the real local dev Postgres.
//
// DEVIATION FROM THE PLAN'S LITERAL TEST CODE, deliberate and confirmed by actually running this
// suite (not just reading it): the plan's brief reused TenantFilterExecuteDeleteTests'
// TransactionScopedDbContextFactory verbatim — one shared physical connection/transaction across
// every DbContext in the test, rolled back at the end. That pattern works for every OTHER
// real-Postgres test in this suite because the method under test there never opens its own
// transaction; it just participates in the ambient one via Database.UseTransaction(). Task 6's
// OrchestrationAdmissionRepository is the first repository in this codebase to call
// ctx.Database.BeginTransactionAsync() itself, and EF Core's RelationalConnection.EnsureNoTransactions()
// throws "The connection is already in a transaction and cannot participate in another
// transaction" the instant that happens on a DbContext already enlisted via UseTransaction() —
// confirmed empirically (all 5 tests failed identically with that exception before this fix).
// Nested BeginTransactionAsync-on-an-ambient-transaction is not something Npgsql/EF Core support.
//
// The fix: give the repository a REAL per-call IDbContextFactory — a fresh physical connection
// every CreateDbContext() call, exactly mirroring production's AddDbContextFactory registration
// (DependencyInjection.cs) — so its own BeginTransactionAsync/CommitAsync/RollbackAsync run on a
// genuinely independent transaction. This is not just a workaround: it is what makes the FOR
// UPDATE row lock and the rollback assertions actually meaningful (a lock and a rollback proven
// only within one already-uncommitted outer transaction proves nothing about real cross-connection
// behavior). The cost is that seed data is now genuinely committed rather than rolled back, so each
// test explicitly deletes what it created in a finally block (CleanupAsync).
//
// Seed-failure safety (Task 6 review fix): seeding is split into two separately-awaited,
// individually-atomic steps — SeedTenantAsync then SeedTaskAsync, each wrapping exactly one
// SaveChangesAsync call — and BOTH calls happen inside the test's own try block, assigning to
// nullable locals declared before the try. This matters because each step commits for real: if
// SeedTaskAsync threw after SeedTenantAsync had already committed the tenant row, and the tenant
// seed call sat outside try (as it briefly did during development), the tenant row would leak
// permanently with no finally block to catch it — the never-actually-workable outer-transaction
// design the brief specified had no such gap (an uncommitted transaction cleans itself up
// regardless of where a failure occurs), but this real-connections design does need the explicit
// guard. Because `tenant = await SeedTenantAsync(...)` only assigns after that call's single
// SaveChangesAsync has itself atomically committed-or-thrown, `tenant` is reliably non-null in
// finally exactly when a tenant row exists to clean up, and likewise for `task`. Every test uses a
// fresh random-suffixed tenant, so cross-test data never collides even if a cleanup step were
// skipped on a prior failure.
public sealed class OrchestrationAdmissionRepositoryTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    private sealed class RealDbContextFactory(DbContextOptions<AppDbContext> options, ICurrentTenantAccessor accessor)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options, accessor);

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static (RealDbContextFactory Factory, AsyncLocalCurrentTenantAccessor Accessor) SetUp()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return (new RealDbContextFactory(options, accessor), accessor);
    }

    // SCOPE (Task 6 review fix — narrowed from an earlier, overstated "FK-safe order" claim):
    // this only cleans up the tables the 5 tests in THIS FILE actually seed — Tenants, Users,
    // OrchestrationTasks, TaskAdmissionReservations — via the two Restrict FKs relevant to them
    // (OrchestrationTask -> Tenant, OrchestrationTask -> User; TaskAdmissionReservations cascade
    // from OrchestrationTasks, so deleting tasks first clears those automatically). It is NOT a
    // general-purpose "delete everything under a tenant" helper. A targeted FK audit during Task 6
    // review found several OTHER entities also carry a Restrict FK to Tenant with no cleanup path
    // here: CostRollup, EvalRun, EvalSuite, EvalCase, EvalResult, AgentMemory. None of that data
    // exists in this file's tests, so nothing is broken today — but if a future real-Postgres
    // transaction test (Task 11 is the likely candidate) copies this harness and seeds any of
    // those entity types, deleting the Tenant here will throw a real FK-violation
    // DbUpdateException, not silently orphan rows. Extend this method (or add a parallel cleanup
    // step) for any new entity types before reusing it as-is.
    private static async Task CleanupAsync(RealDbContextFactory factory, Guid tenantId, params Guid[] userIds)
    {
        await using var ctx = factory.CreateDbContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"OrchestrationTasks\" WHERE \"TenantId\" = {tenantId}");
        foreach (var userId in userIds)
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Users\" WHERE \"Id\" = {userId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Tenants\" WHERE \"Id\" = {tenantId}");
    }

    // Each of these two seed helpers wraps exactly one SaveChangesAsync call, so each is
    // individually atomic (commits fully or throws with nothing committed) — see the class-level
    // comment on why callers must await and capture each one separately, both inside their own
    // try block, rather than composing them into one un-guarded helper call outside try.
    private static async Task<Tenant> SeedTenantAsync(RealDbContextFactory factory, string suffix)
    {
        var tenant = Tenant.Create($"Acme-{suffix}", $"acme-{suffix}");
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Tenants.Add(tenant);
        await ctx.SaveChangesAsync();
        return tenant;
    }

    private static async Task<OrchestrationTask> SeedTaskAsync(
        RealDbContextFactory factory, AsyncLocalCurrentTenantAccessor accessor, Guid tenantId, string suffix)
    {
        using var _ = accessor.SetTenant(tenantId);
        await using var ctx = await factory.CreateDbContextAsync();
        var user = TestUserFactory.Create($"admit-{suffix}@test.local");
        var task = OrchestrationTask.Create(user.Id, "T", "P", false);
        ctx.Users.Add(user);
        ctx.OrchestrationTasks.Add(task);
        await ctx.SaveChangesAsync();
        return task;
    }

    [Fact]
    public async Task TryAdmitAsync_WithinLimits_TransitionsTaskAndInsertsReservation()
    {
        var (factory, accessor) = SetUp();
        var suffix = Guid.NewGuid().ToString("N");
        Tenant? tenant = null;
        OrchestrationTask? task = null;
        try
        {
            tenant = await SeedTenantAsync(factory, suffix);
            task = await SeedTaskAsync(factory, accessor, tenant.Id, suffix);

            var repository = new OrchestrationAdmissionRepository(factory);

            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeTrue();

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reloadedTask = await verifyCtx.OrchestrationTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
            reloadedTask.Status.Should().Be(OrchestrationTaskStatus.Running);
            var reservation = await verifyCtx.TaskAdmissionReservations
                .IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TaskId == task.Id);
            reservation.Should().NotBeNull();
            reservation!.ReservedCostUsd.Should().Be(2m);
        }
        finally
        {
            if (tenant is not null)
                await CleanupAsync(factory, tenant.Id, task?.UserId ?? Guid.Empty);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_TaskAlreadyRunning_RejectsAndLeavesNoReservation()
    {
        var (factory, accessor) = SetUp();
        var suffix = Guid.NewGuid().ToString("N");
        Tenant? tenant = null;
        OrchestrationTask? task = null;
        try
        {
            tenant = await SeedTenantAsync(factory, suffix);
            task = await SeedTaskAsync(factory, accessor, tenant.Id, suffix);

            using (accessor.SetTenant(tenant.Id))
            {
                await using var ctx = await factory.CreateDbContextAsync();
                var trackedTask = await ctx.OrchestrationTasks.FirstAsync(t => t.Id == task.Id);
                trackedTask.MarkRunning();
                await ctx.SaveChangesAsync();
            }

            var repository = new OrchestrationAdmissionRepository(factory);
            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeFalse();
            result.FailureReason.Should().Be(AdmissionFailureReason.TaskNotPending);

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reservation = await verifyCtx.TaskAdmissionReservations
                .IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TaskId == task.Id);
            reservation.Should().BeNull();
        }
        finally
        {
            if (tenant is not null)
                await CleanupAsync(factory, tenant.Id, task?.UserId ?? Guid.Empty);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_ConcurrencyLimitAlreadyReached_RollsBackTaskStateToo()
    {
        var (factory, accessor) = SetUp();
        var suffix = Guid.NewGuid().ToString("N");
        Tenant? tenant = null;
        OrchestrationTask? task = null;
        var otherUserId = Guid.Empty;
        try
        {
            tenant = await SeedTenantAsync(factory, suffix);
            task = await SeedTaskAsync(factory, accessor, tenant.Id, suffix);

            using (accessor.SetTenant(tenant.Id))
            {
                await using var ctx = await factory.CreateDbContextAsync();
                var user = TestUserFactory.Create($"other-{suffix}@test.local");
                var otherTask = OrchestrationTask.Create(user.Id, "Other", "P", false);
                ctx.Users.Add(user);
                ctx.OrchestrationTasks.Add(otherTask);
                await ctx.SaveChangesAsync();
                otherUserId = user.Id;

                // Pre-seed one active reservation, filling the (deliberately tiny) limit of 1.
                ctx.TaskAdmissionReservations.Add(TaskAdmissionReservation.Create(otherTask.Id, 1m));
                await ctx.SaveChangesAsync();
            }

            var repository = new OrchestrationAdmissionRepository(factory);
            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 1, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeFalse();
            result.FailureReason.Should().Be(AdmissionFailureReason.ConcurrencyExceeded);

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reloadedTask = await verifyCtx.OrchestrationTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
            reloadedTask.Status.Should().Be(OrchestrationTaskStatus.Pending,
                "a rejected admission must roll back the task-state CAS too — all-or-nothing, never left Running with no reservation");
        }
        finally
        {
            if (tenant is not null)
                await CleanupAsync(factory, tenant.Id, task?.UserId ?? Guid.Empty, otherUserId);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_BudgetWouldBeExceeded_RollsBackTaskStateToo()
    {
        var (factory, accessor) = SetUp();
        var suffix = Guid.NewGuid().ToString("N");
        Tenant? tenant = null;
        OrchestrationTask? task = null;
        try
        {
            tenant = await SeedTenantAsync(factory, suffix);
            task = await SeedTaskAsync(factory, accessor, tenant.Id, suffix);

            var repository = new OrchestrationAdmissionRepository(factory);

            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 10m,
                    dailyBudgetUsd: 10m, actualDailySpendUsd: 5m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 5m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeFalse();
            result.FailureReason.Should().Be(AdmissionFailureReason.BudgetExceeded);

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reloadedTask = await verifyCtx.OrchestrationTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
            reloadedTask.Status.Should().Be(OrchestrationTaskStatus.Pending);
        }
        finally
        {
            if (tenant is not null)
                await CleanupAsync(factory, tenant.Id, task?.UserId ?? Guid.Empty);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_ReservationOlderThanStaleness_ExcludedFromConcurrencyCount()
    {
        var (factory, accessor) = SetUp();
        var suffix = Guid.NewGuid().ToString("N");
        Tenant? tenant = null;
        OrchestrationTask? task = null;
        var staleUserId = Guid.Empty;
        try
        {
            tenant = await SeedTenantAsync(factory, suffix);
            task = await SeedTaskAsync(factory, accessor, tenant.Id, suffix);

            using (accessor.SetTenant(tenant.Id))
            {
                await using var ctx = await factory.CreateDbContextAsync();
                var user = TestUserFactory.Create($"stale-{suffix}@test.local");
                var staleTask = OrchestrationTask.Create(user.Id, "Stale", "P", false);
                ctx.Users.Add(user);
                ctx.OrchestrationTasks.Add(staleTask);
                await ctx.SaveChangesAsync();
                staleUserId = user.Id;

                // Simulate a crashed reservation from 60 minutes ago (30-minute staleness window)
                // — a process crash means the normal try/finally release (Task 7) never ran.
                await ctx.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "TaskAdmissionReservations" ("TaskId", "TenantId", "ReservedCostUsd", "CreatedAt")
                    VALUES ({staleTask.Id}, {tenant.Id}, 1.0, {DateTimeOffset.UtcNow.AddMinutes(-60)})
                    """);
            }

            var repository = new OrchestrationAdmissionRepository(factory);
            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 1, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeTrue(
                "a reservation older than the staleness window must be excluded from the concurrency count — this is the crash-recovery mechanism, not a reconciliation service");
        }
        finally
        {
            if (tenant is not null)
                await CleanupAsync(factory, tenant.Id, task?.UserId ?? Guid.Empty, staleUserId);
        }
    }
}
