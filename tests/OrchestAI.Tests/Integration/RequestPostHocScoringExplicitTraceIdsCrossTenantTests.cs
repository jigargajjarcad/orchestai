using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;
using OrchestAI.Tests.Infrastructure;

namespace OrchestAI.Tests.Integration;

// Proves confirmation #3's claim for explicit TraceIds with a REAL AppDbContext/query filter, not
// a mocked repository: a cross-tenant ID mixed into the explicit TraceIds list is silently
// excluded because AgentExecutionRepository.SelectForPostHocScoringAsync runs its query through
// the real, tenant-filtered AppDbContext — it does not throw, it just isn't in the resolved set.
// This is deliberately the SAME behavior a date-range selection already has (silently scoped to
// what's visible), not a new explicit-rejection code path. Unlike the original version of this
// test file, this one seeds real AgentExecution rows for two tenants and constructs the REAL
// AgentExecutionRepository — so if AppDbContext's tenant filter were ever broken or removed, both
// trace IDs would resolve and this test would fail.
public sealed class RequestPostHocScoringExplicitTraceIdsCrossTenantTests
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
    public async Task Handle_ExplicitTraceIdsIncludingForeignTenantId_ForeignIdSilentlyExcluded()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("posthoc-cross-tenant@test.local");

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            await seedCtx.SaveChangesAsync();
        }

        Guid ownTraceId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt").WithTestTenant(tenantAId);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt").WithTestTenant(tenantAId);
            execution.Start();
            execution.Complete("Tenant A result", 100, 50, 0.01m);

            ctx.OrchestrationTasks.Add(task);
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            ownTraceId = execution.Id;
        }

        Guid foreignTraceId;
        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant B task", "prompt").WithTestTenant(tenantBId);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt").WithTestTenant(tenantBId);
            execution.Start();
            execution.Complete("Tenant B result", 100, 50, 0.01m);

            ctx.OrchestrationTasks.Add(task);
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            foreignTraceId = execution.Id;
        }

        var executionRepository = new AgentExecutionRepository(factory);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()))
            .Callback<EvalRun, CancellationToken>((r, _) =>
                // This mock stands in for the real EvalRunRepository, whose AddAsync flushes a
                // real SaveChanges and lets TenantScopingInterceptor stamp TenantId (see
                // ADR-014). Simulate that stamp the same way TenantQueryFilterTests/
                // EvalRunBackgroundWorkerPostHocTests do for other private-setter properties, so
                // this test still proves the handler's post-AddAsync TenantId-stamped invariant
                // instead of accidentally tripping it.
                typeof(EvalRun).GetProperty(nameof(EvalRun.TenantId))!.SetValue(r, tenantAId))
            .Returns(Task.CompletedTask);
        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var options = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });

        var handler = new RequestPostHocScoringHandler(
            executionRepository, runRepoMock.Object, queueMock.Object, options,
            NullLogger<RequestPostHocScoringHandler>.Instance);

        var command = new RequestPostHocScoringCommand(
            DateFrom: null, DateTo: null, AgentType: null, TraceIds: [ownTraceId, foreignTraceId],
            ScorerType: EvalScorerType.LlmJudge, Rubric: "was it appropriate?", PassThreshold: null, MaxTraces: 10);

        // Ambient scope is Tenant A — the caller — while foreignTraceId belongs to Tenant B. The
        // real AgentExecutionRepository.SelectForPostHocScoringAsync runs its query through the
        // real, filtered AppDbContext; it is the filter itself, not any mock, that must exclude
        // the foreign trace here.
        using (accessor.SetTenant(tenantAId))
        {
            var response = await handler.Handle(command, CancellationToken.None);

            response.ResolvedTraceCount.Should().Be(
                1, "the foreign-tenant trace ID must be silently excluded, not rejected as an error");
        }
    }

    [Fact]
    public async Task SelectForPostHocScoringAsync_ExplicitTraceIdsIncludingForeignTenantId_OnlyResolvesOwnTenantTrace()
    {
        // Repository-level companion proof: calls the real query method directly (bypassing the
        // handler entirely) to pin down exactly which layer is responsible for the exclusion —
        // AgentExecutionRepository's query against the real, tenant-filtered AppDbContext.
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);

        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("posthoc-cross-tenant-repo@test.local");

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            await seedCtx.SaveChangesAsync();
        }

        Guid ownTraceId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt").WithTestTenant(tenantAId);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt").WithTestTenant(tenantAId);
            execution.Start();
            execution.Complete("Tenant A result", 100, 50, 0.01m);

            ctx.OrchestrationTasks.Add(task);
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            ownTraceId = execution.Id;
        }

        Guid foreignTraceId;
        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant B task", "prompt").WithTestTenant(tenantBId);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt").WithTestTenant(tenantBId);
            execution.Start();
            execution.Complete("Tenant B result", 100, 50, 0.01m);

            ctx.OrchestrationTasks.Add(task);
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            foreignTraceId = execution.Id;
        }

        var executionRepository = new AgentExecutionRepository(factory);

        using (accessor.SetTenant(tenantAId))
        {
            var result = await executionRepository.SelectForPostHocScoringAsync(
                null, null, null, [ownTraceId, foreignTraceId], 10, CancellationToken.None);

            result.TotalMatched.Should().Be(1);
            result.AgentExecutionIds.Should().ContainSingle().Which.Should().Be(ownTraceId);
        }
    }
}
