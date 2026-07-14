using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Proves the tenant query filter is respected even when an entity is reached via a navigation
// property (Include/ThenInclude) from another tenant-scoped entity, not just when queried
// directly as the root of a LINQ query — EF Core applies each entity's own filter to it
// regardless of how it's reached, as long as both ends are correctly tenant-scoped and
// consistent (which TenantScopingInterceptor guarantees at write time).
public sealed class TenantFilterNavigationJoinTests
{
    // See TenantQueryFilterTests.TestDbContextFactory: PooledDbContextFactory<TContext> has no
    // constructor overload that accepts AppDbContext's ICurrentTenantAccessor dependency, so this
    // reuses the same assembly-shared minimal IDbContextFactory<AppDbContext> test double. The
    // TenantScopingInterceptor (Task 5) must be registered here too, exactly as
    // TenantScopingInterceptorTests.BuildFactory does — without it, newly-added entities are never
    // stamped with the ambient TenantId and the query filter correctly (but confusingly) excludes
    // them entirely, which looks like a filter bug but is actually a missing-interceptor bug.
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task IncludeAgentExecutions_ForATenantATask_NeverPullsInTenantBsExecutions()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("nav-join@test.local");

        Guid taskAId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            taskAId = task.Id;
        }

        // A row that (if the filter were somehow bypassed on the navigation side) could
        // incorrectly appear via a join if AgentExecution.OrchestrationTaskId were reused —
        // seeded under tenant B, on ITS OWN task, to confirm cross-tenant isolation holds even
        // when both sides of a relationship are populated.
        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant B task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
        }

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var tasksWithExecutions = await ctx.OrchestrationTasks
                .Include(t => t.AgentExecutions)
                .ToListAsync();

            tasksWithExecutions.Should().ContainSingle(t => t.Id == taskAId);
            tasksWithExecutions.SelectMany(t => t.AgentExecutions).Should()
                .OnlyContain(e => e.TenantId == tenantAId, "Include()'d navigation rows must be filtered exactly like a direct query would be");
        }
    }
}
