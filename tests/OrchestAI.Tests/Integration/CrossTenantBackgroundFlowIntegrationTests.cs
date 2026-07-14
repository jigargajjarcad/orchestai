using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Eval;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;
using OrchestAI.Tests.Infrastructure;

namespace OrchestAI.Tests.Integration;

// Proves the full background-flow propagation contract from ADR-014 confirmation #5: tenant A
// authenticates (simulated by setting the ambient tenant), triggers post-hoc scoring, the
// "HTTP request" ends (the scope is disposed), the worker processes the queued item entirely
// outside that scope by restoring TenantId from the persisted EvalRun, and tenant B's own
// ambient scope can never see any of tenant A's resulting data. Every repository here is real
// (backed by a shared IDbContextFactory<AppDbContext>/TestDbContextFactory against one in-memory
// database) — only ILlmProvider/IModelPricingCache are mocked — so the real EF Core tenant query
// filter and the real TenantScopingInterceptor are exercised end to end, not bypassed.
public sealed class CrossTenantBackgroundFlowIntegrationTests
{
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        // TenantScopingInterceptor must be wired in exactly like TenantScopingInterceptorTests
        // does — this is what stamps EvalRun.TenantId (and every other ITenantScoped entity)
        // from the ambient accessor on real writes, which is the whole premise this test proves:
        // RequestPostHocScoringHandler reads run.TenantId straight off the entity with no second
        // ambient-tenant lookup, relying on the interceptor having already set it in-memory.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        // See TenantQueryFilterTests.TestDbContextFactory: PooledDbContextFactory<TContext> has
        // no hook for AppDbContext's ICurrentTenantAccessor constructor dependency, so this test
        // uses the shared minimal IDbContextFactory<AppDbContext> instead.
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task FullFlow_TenantAEnqueuesPostHocScoring_WorkerRestoresTenantOutsideHttpScope_TenantBCannotSeeResults()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var executionRepository = new AgentExecutionRepository(factory);
        var runRepository = new EvalRunRepository(factory);
        var resultRepository = new EvalResultRepository(factory);
        var tenantRepository = new TenantRepository(factory);
        var queue = new InMemoryEvalRunQueue();

        // Seed tenant A's Tenant row (Active) and one historical AgentExecution, scoped to
        // tenant A.
        Guid executionId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var tenantA = Tenant.Create("Tenant A", "tenant-a");
            typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenantA, tenantAId);
            ctx.Tenants.Add(tenantA);

            var user = TestUserFactory.Create("cross-tenant-bg@test.local");
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            execution.Start();
            execution.Complete("Researched thoroughly.", 100, 50, 0.02m);
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            executionId = execution.Id;
        }

        // "HTTP request" for tenant A: submit the post-hoc scoring request while tenant A's
        // scope is active, then the scope ends (simulating the request finishing).
        Guid evalRunId;
        using (accessor.SetTenant(tenantAId))
        {
            var evalOptions = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });
            var requestHandler = new RequestPostHocScoringHandler(
                executionRepository, runRepository, queue, evalOptions, NullLogger<RequestPostHocScoringHandler>.Instance);

            var command = new RequestPostHocScoringCommand(
                DateFrom: DateTimeOffset.UtcNow.AddDays(-1), DateTo: DateTimeOffset.UtcNow.AddDays(1),
                AgentType: AgentType.Research, TraceIds: null, ScorerType: EvalScorerType.LlmJudge,
                Rubric: "Did the agent research thoroughly?", PassThreshold: 0.5m, MaxTraces: 10);

            var response = await requestHandler.Handle(command, CancellationToken.None);
            response.ResolvedTraceCount.Should().Be(1);
            evalRunId = response.EvalRunId;
        }
        // Ambient scope for tenant A is now cleared — accessor.TenantId is null here.
        accessor.TenantId.Should().BeNull("the simulated HTTP request has ended");

        // Dequeue and process exactly like EvalRunBackgroundWorker.ExecuteAsync would, entirely
        // outside any tenant scope until the worker restores one from the queued item itself.
        var queuedItem = await queue.DequeueAsync(CancellationToken.None);
        queuedItem.TenantId.Should().Be(
            tenantAId, "TenantId must have been captured at enqueue time from the EvalRun the interceptor stamped");

        var services = new ServiceCollection();
        services.AddSingleton<IEvalSuiteRepository>(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton<IEvalRunRepository>(runRepository);
        services.AddSingleton<IEvalResultRepository>(resultRepository);
        services.AddSingleton<IOrchestrationTaskRepository>(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton<IAgentExecutionRepository>(executionRepository);
        services.AddSingleton<ITenantRepository>(tenantRepository);
        services.AddSingleton<IAgentFactory>(Mock.Of<IAgentFactory>());

        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", """{"score":0.9,"reasoning":"Thorough."}""", [], 200, 40));
        var providerFactoryMock = new Mock<ILlmProviderFactory>();
        providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);
        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock.Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));
        var costLedgerRepository = new CostLedgerRepository(factory, accessor);
        var judgeOptions = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001", DefaultJudgePassThreshold = 0.7m
        });
        var judgeScorer = new LlmJudgeScorer(providerFactoryMock.Object, pricingCacheMock.Object, costLedgerRepository, judgeOptions);
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([judgeScorer]));
        var provider = services.BuildServiceProvider();

        var worker = new EvalRunBackgroundWorker(
            Mock.Of<IEvalRunQueue>(), provider.GetRequiredService<IServiceScopeFactory>(),
            accessor, NullLogger<EvalRunBackgroundWorker>.Instance);

        // Simulates exactly what ExecuteAsync does: restore the scope from the dequeued item's
        // TenantId, THEN process — never before.
        using (accessor.SetTenant(queuedItem.TenantId))
        {
            await worker.ProcessRunAsync(queuedItem.EvalRunId, CancellationToken.None);
        }

        // Tenant A can see the result.
        using (accessor.SetTenant(tenantAId))
        {
            var results = await resultRepository.GetByRunIdAsync(evalRunId, CancellationToken.None);
            results.Should().ContainSingle();
            results[0].TenantId.Should().Be(tenantAId);
        }

        // Tenant B — a completely different tenant who was never involved — sees nothing.
        using (accessor.SetTenant(tenantBId))
        {
            var results = await resultRepository.GetByRunIdAsync(evalRunId, CancellationToken.None);
            results.Should().BeEmpty("tenant B must never see tenant A's post-hoc scoring results, background-processed or not");

            await using var ctx = await factory.CreateDbContextAsync();
            var visibleExecution = await ctx.AgentExecutions.FirstOrDefaultAsync(e => e.Id == executionId);
            visibleExecution.Should().BeNull("tenant B must not see tenant A's underlying AgentExecution either");
        }
    }

    [Fact]
    public async Task ProcessRunAsync_TenantSuspendedBetweenEnqueueAndDequeue_WorkerRejectsRealSuspendedTenantRow()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);
        var tenantId = Guid.NewGuid();

        var executionRepository = new AgentExecutionRepository(factory);
        var runRepository = new EvalRunRepository(factory);
        var resultRepository = new EvalResultRepository(factory);
        var tenantRepository = new TenantRepository(factory);
        var queue = new InMemoryEvalRunQueue();

        Guid executionId;
        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var tenant = Tenant.Create("Acme", "acme");
            typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
            ctx.Tenants.Add(tenant);

            var user = TestUserFactory.Create("cross-tenant-bg-suspended@test.local");
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            execution.Start();
            execution.Complete("Researched thoroughly.", 100, 50, 0.02m);
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            executionId = execution.Id;
        }

        Guid evalRunId;
        using (accessor.SetTenant(tenantId))
        {
            var evalOptions = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });
            var requestHandler = new RequestPostHocScoringHandler(
                executionRepository, runRepository, queue, evalOptions, NullLogger<RequestPostHocScoringHandler>.Instance);

            var command = new RequestPostHocScoringCommand(
                DateFrom: DateTimeOffset.UtcNow.AddDays(-1), DateTo: DateTimeOffset.UtcNow.AddDays(1),
                AgentType: AgentType.Research, TraceIds: null, ScorerType: EvalScorerType.LlmJudge,
                Rubric: "Did the agent research thoroughly?", PassThreshold: 0.5m, MaxTraces: 10);

            var response = await requestHandler.Handle(command, CancellationToken.None);
            evalRunId = response.EvalRunId;
        }

        var queuedItem = await queue.DequeueAsync(CancellationToken.None);
        queuedItem.TenantId.Should().Be(tenantId);

        // Between enqueue and dequeue, an admin suspends the tenant — a REAL Tenant row is
        // mutated via Suspend() and persisted, not a mocked "is suspended" flag.
        using (accessor.SetTenant(tenantId))
        {
            var tenant = await tenantRepository.GetByIdAsync(tenantId, CancellationToken.None);
            tenant!.Suspend();
            await tenantRepository.UpdateAsync(tenant, CancellationToken.None);
        }

        var services = new ServiceCollection();
        services.AddSingleton<IEvalSuiteRepository>(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton<IEvalRunRepository>(runRepository);
        services.AddSingleton<IEvalResultRepository>(resultRepository);
        services.AddSingleton<IOrchestrationTaskRepository>(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton<IAgentExecutionRepository>(executionRepository);
        services.AddSingleton<ITenantRepository>(tenantRepository);
        services.AddSingleton<IAgentFactory>(Mock.Of<IAgentFactory>());
        // No scorer registered — if the suspension check failed to short-circuit and the worker
        // reached the scoring branch anyway, resolving IEvalScorerFactory would throw and fail
        // this test loudly, instead of silently passing.
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([]));
        var provider = services.BuildServiceProvider();

        var worker = new EvalRunBackgroundWorker(
            Mock.Of<IEvalRunQueue>(), provider.GetRequiredService<IServiceScopeFactory>(),
            accessor, NullLogger<EvalRunBackgroundWorker>.Instance);

        using (accessor.SetTenant(queuedItem.TenantId))
        {
            await worker.ProcessRunAsync(queuedItem.EvalRunId, CancellationToken.None);
        }

        using (accessor.SetTenant(tenantId))
        {
            var run = await runRepository.GetByIdAsync(evalRunId, CancellationToken.None);
            run!.Status.Should().Be(
                EvalRunStatus.Failed, "a tenant suspended after enqueue must reject queued work, not silently complete it");

            var results = await resultRepository.GetByRunIdAsync(evalRunId, CancellationToken.None);
            results.Should().BeEmpty("no scoring should have happened once the owning tenant was known to be suspended");
        }
    }
}
