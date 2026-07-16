using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Genuinely concurrent — each parallel call opens its OWN transaction against the real local
// dev Postgres, exactly like two simultaneous HTTP requests would in production. Rows are
// committed for real and explicitly cleaned up in a finally (see this task's note on why the
// TenantFilterExecuteDeleteTests-style single-rolled-back-transaction pattern can't be used
// here — it would trivially serialize both "concurrent" calls on one shared connection).
// Requires the local dev Postgres up and migrated through Task 10.
public sealed class AdmissionConcurrencyRaceTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    private static IDbContextFactory<AppDbContext> CreateRealFactory(ICurrentTenantAccessor accessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(accessor);
        services.AddSingleton<TenantScopingInterceptor>();
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(ConnectionString);
            options.AddInterceptors(sp.GetRequiredService<TenantScopingInterceptor>());
        });
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private static async Task<(Tenant Tenant, Guid UserId)> SeedTenantAsync(
        IDbContextFactory<AppDbContext> factory, ICurrentTenantAccessor accessor, string suffix)
    {
        var tenant = Tenant.Create($"Race-{suffix}", $"race-{suffix}");
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
        }

        Guid userId;
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var user = TestUserFactory.Create($"race-{suffix}@test.local");
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            userId = user.Id;
        }

        return (tenant, userId);
    }

    // Bug fix (confirmed empirically, not a flaky-race signal): the brief's original version of
    // this helper deleted Users (via a subquery over OrchestrationTasks) BEFORE deleting
    // OrchestrationTasks itself. Since OrchestrationTasks.UserId carries
    // FK_OrchestrationTasks_Users_UserId (OrchestrationTasks is the child, Users the parent), that
    // order throws a 23503 foreign_key_violation on every single run — reproduced identically 5/5
    // times, i.e. deterministic, not intermittent, so this is a delete-order defect in the test's
    // own cleanup code, not evidence of anything wrong in OrchestrationAdmissionRepository's
    // locking. Fixed to match the exact convention already established by
    // OrchestrationAdmissionRepositoryTests.CleanupAsync in this same directory: take the UserIds
    // explicitly (each test already has them in scope from SeedTenantAsync) and delete
    // OrchestrationTasks before Users.
    private static async Task CleanUpAsync(IDbContextFactory<AppDbContext> factory, Guid tenantId, params Guid[] userIds)
    {
        await using var ctx = await factory.CreateDbContextAsync();
        // Explicit, dependency-order deletes rather than relying on unverified cascade
        // configuration — safe regardless of what each entity's configuration actually specifies.
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"TaskAdmissionReservations\" WHERE \"TenantId\" = {tenantId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"OrchestrationTasks\" WHERE \"TenantId\" = {tenantId}");
        foreach (var userId in userIds)
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Users\" WHERE \"Id\" = {userId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Tenants\" WHERE \"Id\" = {tenantId}");
    }

    [Fact]
    public async Task TryAdmitAsync_TwoConcurrentAdmissionsSameTask_ExactlyOneSucceeds()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = CreateRealFactory(accessor);
        var (tenant, userId) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));

        OrchestrationTask task;
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            task = OrchestrationTask.Create(userId, "T", "P", false);
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
        }

        try
        {
            var repository = new OrchestrationAdmissionRepository(factory);

            Task<AdmissionResult> AdmitAsync() => Task.Run(async () =>
            {
                using (accessor.SetTenant(tenant.Id))
                {
                    return await repository.TryAdmitAsync(
                        task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 1m,
                        dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                        reservationStaleness: TimeSpan.FromMinutes(30));
                }
            });

            var results = await Task.WhenAll(AdmitAsync(), AdmitAsync());

            results.Count(r => r.Admitted).Should().Be(1,
                "two simultaneous admission attempts against the same Pending task must never both succeed — the CAS must serialize them");
            results.Count(r => !r.Admitted && r.FailureReason == AdmissionFailureReason.TaskNotPending).Should().Be(1);
        }
        finally
        {
            await CleanUpAsync(factory, tenant.Id, userId);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_TwoConcurrentAdmissionsWouldTogetherExceedBudget_ExactlyOneSucceeds()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = CreateRealFactory(accessor);
        var (tenant, userId) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));

        OrchestrationTask taskA, taskB;
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            taskA = OrchestrationTask.Create(userId, "A", "P", false);
            taskB = OrchestrationTask.Create(userId, "B", "P", false);
            ctx.OrchestrationTasks.AddRange(taskA, taskB);
            await ctx.SaveChangesAsync();
        }

        try
        {
            var repository = new OrchestrationAdmissionRepository(factory);

            // Each individually fits within a $10 budget (actual spend $0) — but $6 + $6 = $12
            // together exceeds it. A naive read-then-check-then-write would let both pass.
            Task<AdmissionResult> AdmitAsync(Guid taskId) => Task.Run(async () =>
            {
                using (accessor.SetTenant(tenant.Id))
                {
                    return await repository.TryAdmitAsync(
                        taskId, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 6m,
                        dailyBudgetUsd: 10m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                        reservationStaleness: TimeSpan.FromMinutes(30));
                }
            });

            var results = await Task.WhenAll(AdmitAsync(taskA.Id), AdmitAsync(taskB.Id));

            results.Count(r => r.Admitted).Should().Be(1,
                "two simultaneous admissions that individually fit but together exceed the budget must not both be admitted — " +
                "this is the atomic-reservation proof, not just evidence that a single-request check exists");
            results.Count(r => !r.Admitted && r.FailureReason == AdmissionFailureReason.BudgetExceeded).Should().Be(1);
        }
        finally
        {
            await CleanUpAsync(factory, tenant.Id, userId);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_TenantAAtConcurrencyLimit_DoesNotAffectTenantBsAdmission()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = CreateRealFactory(accessor);
        var (tenantA, userIdA) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));
        var (tenantB, userIdB) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));

        OrchestrationTask taskA, alreadyRunningTaskA, taskB;
        using (accessor.SetTenant(tenantA.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            taskA = OrchestrationTask.Create(userIdA, "A", "P", false);
            alreadyRunningTaskA = OrchestrationTask.Create(userIdA, "A-running", "P", false);
            ctx.OrchestrationTasks.AddRange(taskA, alreadyRunningTaskA);
            await ctx.SaveChangesAsync();
            ctx.TaskAdmissionReservations.Add(TaskAdmissionReservation.Create(alreadyRunningTaskA.Id, 1m));
            await ctx.SaveChangesAsync();
        }
        using (accessor.SetTenant(tenantB.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            taskB = OrchestrationTask.Create(userIdB, "B", "P", false);
            ctx.OrchestrationTasks.Add(taskB);
            await ctx.SaveChangesAsync();
        }

        try
        {
            var repository = new OrchestrationAdmissionRepository(factory);

            AdmissionResult resultA, resultB;
            using (accessor.SetTenant(tenantA.Id))
            {
                resultA = await repository.TryAdmitAsync(
                    taskA.Id, tenantA.Id, maxConcurrentTasks: 1, reservationAmountUsd: 1m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }
            using (accessor.SetTenant(tenantB.Id))
            {
                resultB = await repository.TryAdmitAsync(
                    taskB.Id, tenantB.Id, maxConcurrentTasks: 1, reservationAmountUsd: 1m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            resultA.Admitted.Should().BeFalse("tenant A is already at its own concurrency limit");
            resultB.Admitted.Should().BeTrue(
                "tenant B has no reservations at all — tenant A's exhausted limit must never leak into tenant B's admission");
        }
        finally
        {
            await CleanUpAsync(factory, tenantA.Id, userIdA);
            await CleanUpAsync(factory, tenantB.Id, userIdB);
        }
    }
}
