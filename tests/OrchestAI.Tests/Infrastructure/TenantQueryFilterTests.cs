using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class TenantQueryFilterTests
{
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        // AppDbContext's constructor now takes ICurrentTenantAccessor as a second parameter.
        // EF Core's PooledDbContextFactory<TContext> only supports a (DbContextOptions<TContext>,
        // int poolSize) constructor — it has no hook for extra constructor parameters — so it
        // cannot construct a context with a non-options dependency. TestDbContextFactory below is
        // a minimal IDbContextFactory<AppDbContext> that just calls `new AppDbContext(options,
        // tenantAccessor)` per request; it forgoes real EF context pooling, which is a performance
        // optimization the InMemory provider's tests never needed anyway.
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    private static async Task<(Guid TenantAId, Guid TenantBId, Guid TaskAId, Guid TaskBId)> SeedTwoTenants(
        IDbContextFactory<AppDbContext> factory, AsyncLocalCurrentTenantAccessor accessor)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("tenant-filter@test.local");

        Guid taskAId, taskBId;
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
        }

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            // TenantScopingInterceptor (Task 5) is what stamps TenantId on real writes from the
            // ambient accessor; it doesn't exist on this branch yet (Task 4 runs before Task 5 in
            // execution order), so OrchestrationTask.Create(...) alone leaves TenantId at its
            // Guid.Empty default (see TenantScopedEntitiesTests). Stamp it directly here via
            // reflection — test-only scaffolding standing in for Task 5's interceptor — so this
            // test can prove the READ-side filter (Task 4's actual scope) in isolation.
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt").WithTestTenant(tenantAId);
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskAId = task.Id;
        }

        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant B task", "prompt").WithTestTenant(tenantBId);
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskBId = task.Id;
        }

        return (tenantAId, tenantBId, taskAId, taskBId);
    }

    [Fact]
    public async Task Query_WithTenantAScope_OnlySeesTenantARows()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var (tenantAId, _, taskAId, taskBId) = await SeedTwoTenants(factory, accessor);

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var tasks = await ctx.OrchestrationTasks.ToListAsync();

            tasks.Should().ContainSingle(t => t.Id == taskAId);
            tasks.Should().NotContain(t => t.Id == taskBId);
        }
    }

    [Fact]
    public async Task Query_WithNoTenantScope_ReturnsNoRows()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        await SeedTwoTenants(factory, accessor);

        // Deliberately no accessor.SetTenant(...) call — simulates the fail-closed case.
        await using var ctx = await factory.CreateDbContextAsync();
        var tasks = await ctx.OrchestrationTasks.ToListAsync();

        tasks.Should().BeEmpty("no tenant context resolved must mean zero rows, never all rows");
    }

    [Fact]
    public async Task Query_ByIdForForeignTenantRow_ReturnsNull()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var (tenantAId, _, _, taskBId) = await SeedTwoTenants(factory, accessor);

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var found = await ctx.OrchestrationTasks.FirstOrDefaultAsync(t => t.Id == taskBId);

            found.Should().BeNull("looking up tenant B's row by ID while scoped to tenant A must not leak it");
        }
    }
}

// Minimal IDbContextFactory<AppDbContext> for tests: EF Core's PooledDbContextFactory<TContext>
// only exposes a (DbContextOptions<TContext>, int poolSize) constructor, with no way to pass
// AppDbContext's ICurrentTenantAccessor dependency through to pooled instances. This creates a
// fresh AppDbContext per call — the same "fresh context per call" shape every real repository
// already uses via IDbContextFactory<AppDbContext>, just without connection pooling, which the
// InMemory provider gets no benefit from in tests anyway. Shared (internal, assembly-scoped)
// across every test file in OrchestAI.Tests.Infrastructure that builds an AppDbContext factory.
internal sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options, ICurrentTenantAccessor tenantAccessor)
    : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options, tenantAccessor);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

// Test-only stand-in for TenantScopingInterceptor (Task 5), which does not exist yet on this
// branch. Directly sets the private-setter TenantId property via reflection — the same technique
// TestUserFactory (CostLedgerRepositoryEvalFilterTests.cs) already uses for other private-setter
// properties in this test project. Production code must never do this; the interceptor is the
// only legitimate writer of TenantId outside of EF materialization.
internal static class TestTenantStampingExtensions
{
    public static T WithTestTenant<T>(this T entity, Guid tenantId) where T : ITenantScoped
    {
        typeof(T).GetProperty(nameof(ITenantScoped.TenantId))!.SetValue(entity, tenantId);
        return entity;
    }
}
