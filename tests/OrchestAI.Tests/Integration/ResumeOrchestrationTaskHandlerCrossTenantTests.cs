using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.ResumeOrchestrationTask;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;
using OrchestAI.Tests.Infrastructure;

namespace OrchestAI.Tests.Integration;

// Proves confirmation #3's claim for THIS specific command with a REAL AppDbContext/query filter,
// not a mocked repository: no new ownership-check code is needed here because the tenant query
// filter (Task 4) already makes a foreign tenant's TaskId invisible to
// GetByIdWithExecutionsAsync, which the handler already null-checks into a NotFoundException.
// Unlike the original version of this test file, this one seeds two tenants' data into a real
// InMemory AppDbContext, constructs the REAL OrchestrationTaskRepository against a real
// IDbContextFactory<AppDbContext>, and calls the real handler — so if AppDbContext's tenant
// filter were ever broken or removed, this test would fail (see the companion "filter neutralized"
// proof run manually as part of this fix; ConflictException would surface instead of
// NotFoundException because the foreign task would become visible and its default Pending status
// would fail the handler's Failed-only resume check).
public sealed class ResumeOrchestrationTaskHandlerCrossTenantTests
{
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        // See TenantQueryFilterTests.TestDbContextFactory: PooledDbContextFactory<TContext> has no
        // hook for AppDbContext's ICurrentTenantAccessor dependency, so this test uses the shared
        // minimal IDbContextFactory<AppDbContext> instead.
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task Handle_TaskIdInvisibleUnderCurrentTenantFilter_ThrowsNotFound()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("resume-cross-tenant@test.local");

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            await seedCtx.SaveChangesAsync();
        }

        Guid taskAId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            // TenantScopingInterceptor (Task 5) is what stamps TenantId on real writes from the
            // ambient accessor; stamp it directly here via reflection — the same test-only
            // scaffolding TenantQueryFilterTests uses — so this test can prove the READ-side
            // filter (Task 4's actual scope) in isolation.
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt").WithTestTenant(tenantAId);
            var orchestratorExecution = AgentExecution
                .Create(task.Id, OrchestAI.Domain.Enums.AgentType.Orchestrator, "plan prompt")
                .WithTestTenant(tenantAId);

            ctx.OrchestrationTasks.Add(task);
            ctx.AgentExecutions.Add(orchestratorExecution);
            await ctx.SaveChangesAsync();
            taskAId = task.Id;
        }

        var taskRepository = new OrchestrationTaskRepository(factory);
        var handler = new ResumeOrchestrationTaskHandler(
            taskRepository,
            Mock.Of<IOrchestratorAgent>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<ITaskCheckpointRepository>(),
            Mock.Of<IOrchestrationEventBus>(),
            NullLogger<ResumeOrchestrationTaskHandler>.Instance);

        // Ambient scope is Tenant B — the request's caller — while the task actually belongs to
        // Tenant A. The real OrchestrationTaskRepository runs its query through the real,
        // filtered AppDbContext; it is the filter itself, not any mock, that must make Tenant A's
        // task invisible here.
        using (accessor.SetTenant(tenantBId))
        {
            var act = async () => await handler.Handle(
                new ResumeOrchestrationTaskCommand(taskAId), CancellationToken.None);

            await act.Should().ThrowAsync<NotFoundException>();
        }
    }

    [Fact]
    public async Task Handle_TaskIdVisibleUnderOwningTenantFilter_DoesNotThrowNotFound()
    {
        // Companion sanity check: the SAME task, looked up under its OWNING tenant's scope, must
        // resolve past the not-found check (it may still fail later for unrelated reasons, e.g.
        // its Pending status not being Failed) — proving the prior test's NotFoundException is
        // caused by cross-tenant invisibility, not some unconditional failure in the handler.
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);

        var tenantAId = Guid.NewGuid();
        var user = TestUserFactory.Create("resume-cross-tenant-owner@test.local");

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            await seedCtx.SaveChangesAsync();
        }

        Guid taskAId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt").WithTestTenant(tenantAId);
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskAId = task.Id;
        }

        var taskRepository = new OrchestrationTaskRepository(factory);
        var handler = new ResumeOrchestrationTaskHandler(
            taskRepository,
            Mock.Of<IOrchestratorAgent>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<ITaskCheckpointRepository>(),
            Mock.Of<IOrchestrationEventBus>(),
            NullLogger<ResumeOrchestrationTaskHandler>.Instance);

        using (accessor.SetTenant(tenantAId))
        {
            var act = async () => await handler.Handle(
                new ResumeOrchestrationTaskCommand(taskAId), CancellationToken.None);

            // Default Create() status is Pending, not Failed, so the handler's resume-eligibility
            // check throws ConflictException — the key assertion is that it is NOT
            // NotFoundException, proving the task WAS found once scoped to its owning tenant.
            await act.Should().ThrowAsync<ConflictException>();
        }
    }
}
