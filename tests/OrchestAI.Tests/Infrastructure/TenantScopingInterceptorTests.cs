using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class TenantScopingInterceptorTests
{
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        // See TenantQueryFilterTests.TestDbContextFactory: PooledDbContextFactory<TContext> has
        // no hook for AppDbContext's ICurrentTenantAccessor dependency, so this test uses the
        // shared minimal IDbContextFactory<AppDbContext> instead.
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task SaveChanges_WithTenantScope_StampsTenantIdOnNewEntity()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var user = TestUserFactory.Create("interceptor-stamp@test.local");

        Guid taskId;
        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "title", "prompt");
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskId = task.Id;
        }

        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var persisted = await ctx.OrchestrationTasks.SingleAsync(t => t.Id == taskId);
            persisted.TenantId.Should().Be(tenantId);
        }
    }

    [Fact]
    public async Task SaveChanges_NoTenantScope_ThrowsAndDoesNotPersist()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var user = TestUserFactory.Create("interceptor-noscope@test.local");

        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync(); // User is not ITenantScoped — this must still succeed.

        var task = OrchestrationTask.Create(user.Id, "title", "prompt");
        ctx.OrchestrationTasks.Add(task);
        var taskId = task.Id;

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<TenantContextViolationException>(
            "no ambient tenant is set, so persisting a new tenant-scoped entity must be rejected, never silently defaulted");

        await using var verifyCtx = await factory.CreateDbContextAsync();
        var persisted = await verifyCtx.OrchestrationTasks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == taskId);
        persisted.Should().BeNull(
            "the failed SaveChangesAsync must not have persisted the rejected entity, regardless of query filters");
    }

    [Fact]
    public async Task SaveChanges_NewEntityWithMismatchedPreSetTenantId_ThrowsAndDoesNotOverwrite()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var user = TestUserFactory.Create("interceptor-presetmismatch@test.local");

        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "title", "prompt");
            ctx.OrchestrationTasks.Add(task);
            ctx.Entry(task).Property("TenantId").CurrentValue = otherTenantId;

            var act = async () => await ctx.SaveChangesAsync();

            await act.Should().ThrowAsync<TenantContextViolationException>(
                "a new entity with a pre-set TenantId that mismatches the ambient tenant must be rejected, never overwritten");
        }
    }

    [Fact]
    public async Task SaveChanges_ExistingEntity_TenantIdCannotBeChanged()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var user = TestUserFactory.Create("interceptor-immutable@test.local");

        Guid taskId;
        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "title", "prompt");
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskId = task.Id;
        }

        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = await ctx.OrchestrationTasks.SingleAsync(t => t.Id == taskId);
            var otherTenantId = Guid.NewGuid();
            ctx.Entry(task).Property("TenantId").CurrentValue = otherTenantId;

            var act = async () => await ctx.SaveChangesAsync();

            await act.Should().ThrowAsync<TenantContextViolationException>(
                "TenantId must never change once an entity is created");
        }
    }

    [Fact]
    public async Task SaveChanges_DetachedEntityUpdatedViaRepositoryPattern_DoesNotFalsePositive()
    {
        // Every repository in this codebase (EvalRunRepository, OrchestrationTaskRepository,
        // AgentExecutionRepository, ApiKeyRepository, ...) uses IDbContextFactory — a fresh
        // DbContext per call — so a normal "fetch, mutate the CLR object, then
        // ctx.Set<T>().Update(entity)" status-update flow always attaches a previously-untracked
        // entity graph. EF Core's Update() marks EVERY scalar property (including the unchanged
        // TenantId) as IsModified=true in that case, since it has no original snapshot to diff
        // against. This test proves that no longer trips the interceptor's tamper check.
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var user = TestUserFactory.Create("interceptor-detached-update@test.local");

        Guid taskId;
        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "title", "prompt");
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskId = task.Id;
        }

        using (accessor.SetTenant(tenantId))
        {
            // Simulates OrchestrationTaskRepository.UpdateAsync exactly: a fresh context, a
            // detached entity instance (fetched via a separate context, mirroring the repository
            // pattern), attached and marked Modified via Update() — not mutated in place on an
            // already-tracked instance.
            await using var fetchCtx = await factory.CreateDbContextAsync();
            var detachedTask = await fetchCtx.OrchestrationTasks.SingleAsync(t => t.Id == taskId);

            await using var updateCtx = await factory.CreateDbContextAsync();
            detachedTask.MarkRunning();
            updateCtx.OrchestrationTasks.Update(detachedTask);

            var act = async () => await updateCtx.SaveChangesAsync();

            await act.Should().NotThrowAsync(
                "TenantId's value is unchanged — only EF's disconnected-update tracking flagged it as dirty, which must not be treated as a real tenant change");
        }
    }

    [Fact]
    public async Task SaveChanges_NonTenantScopedEntity_IsUnaffected()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var user = TestUserFactory.Create("interceptor-nonscoped@test.local");

        // Deliberately no accessor.SetTenant(...) — User isn't ITenantScoped, so this must
        // succeed even with zero ambient tenant context.
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Users.Add(user);
        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
