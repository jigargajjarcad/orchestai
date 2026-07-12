using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Application.Configuration;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Eval;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;
using OrchestAI.Tests.Infrastructure;

namespace OrchestAI.Tests.Integration;

// End-to-end: seeds AgentExecution rows exactly as real production traffic would create them
// (never via a live eval run), submits a real post-hoc scoring request through the real command
// handler, processes it through the real background worker, and asserts on the real persisted
// EvalResult rows — no mocked repositories anywhere in this test.
public sealed class PostHocScoringIntegrationTests
{
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        // See TenantQueryFilterTests.TestDbContextFactory: PooledDbContextFactory<TContext> has
        // no hook for AppDbContext's new ICurrentTenantAccessor dependency, so this test uses the
        // shared minimal IDbContextFactory<AppDbContext> instead.
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task FullFlow_SeededHistoricalTraces_ProducesEvalResultsWithNullCaseIdAndRubric()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);
        // This test predates tenant scoping (Task 10) and TenantScopingInterceptor (Task 5, not
        // yet on this branch). Every ITenantScoped entity in this flow — seeded AgentExecution
        // rows and the EvalRun/EvalResult/CostLedger rows the real handler/worker create deep
        // inside the call chain — keeps its Guid.Empty TenantId default. Scoping the ambient
        // tenant to Guid.Empty for the whole flow (it flows through every await via AsyncLocal)
        // makes Task 4's new query filter transparent to this test without reaching into
        // production code to stamp anything.
        using var tenantScope = accessor.SetTenant(Guid.Empty);

        var user = TestUserFactory.Create("posthoc-integration@test.local");
        var task = OrchestrationTask.Create(user.Id, "production task", "prompt");
        var executionOne = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        executionOne.Start();
        executionOne.Complete("Researched topic X thoroughly.", 120, 60, 0.02m);
        var executionTwo = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        executionTwo.Start();
        executionTwo.Complete("Researched topic Y thoroughly.", 130, 65, 0.02m);

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(executionOne, executionTwo);
            await seedCtx.SaveChangesAsync();
        }

        var executionRepository = new AgentExecutionRepository(factory);
        var runRepository = new EvalRunRepository(factory);
        var resultRepository = new EvalResultRepository(factory);
        var queue = new InMemoryEvalRunQueue();

        var evalOptions = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });
        var requestHandler = new RequestPostHocScoringHandler(
            executionRepository, runRepository, queue, evalOptions, NullLogger<RequestPostHocScoringHandler>.Instance);

        var command = new RequestPostHocScoringCommand(
            DateFrom: DateTimeOffset.UtcNow.AddDays(-1), DateTo: DateTimeOffset.UtcNow.AddDays(1),
            AgentType: AgentType.Research, TraceIds: null, ScorerType: EvalScorerType.LlmJudge,
            Rubric: "Did the agent thoroughly research the requested topic?", PassThreshold: 0.6m, MaxTraces: 10);

        var triggerResponse = await requestHandler.Handle(command, CancellationToken.None);
        triggerResponse.ResolvedTraceCount.Should().Be(2);

        var services = new ServiceCollection();
        services.AddSingleton<IEvalSuiteRepository>(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton<IEvalRunRepository>(runRepository);
        services.AddSingleton<IEvalResultRepository>(resultRepository);
        services.AddSingleton<IOrchestrationTaskRepository>(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton<IAgentExecutionRepository>(executionRepository);
        services.AddSingleton<IAgentFactory>(Mock.Of<IAgentFactory>());

        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", """{"score":0.85,"reasoning":"Thorough research shown."}""", [], 200, 40));
        var providerFactoryMock = new Mock<ILlmProviderFactory>();
        providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock
            .Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));

        var costLedgerRepository = new CostLedgerRepository(factory);
        var judgeOptions = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001", DefaultJudgePassThreshold = 0.7m
        });
        var judgeScorer = new LlmJudgeScorer(providerFactoryMock.Object, pricingCacheMock.Object, costLedgerRepository, judgeOptions);
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([judgeScorer]));
        var provider = services.BuildServiceProvider();

        var worker = new EvalRunBackgroundWorker(
            Mock.Of<IEvalRunQueue>(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EvalRunBackgroundWorker>.Instance);

        await worker.ProcessRunAsync(triggerResponse.EvalRunId, CancellationToken.None);

        var persistedResults = await resultRepository.GetByRunIdAsync(triggerResponse.EvalRunId, CancellationToken.None);
        persistedResults.Should().HaveCount(2);
        persistedResults.Should().OnlyContain(r => r.EvalCaseId == null);
        persistedResults.Should().OnlyContain(r => r.Rubric == "Did the agent thoroughly research the requested topic?");
        persistedResults.Should().OnlyContain(r => r.Passed);

        var run = await runRepository.GetByIdAsync(triggerResponse.EvalRunId, CancellationToken.None);
        run!.Status.Should().Be(EvalRunStatus.Completed);

        // Direct assertion on the persisted CostLedger rows themselves: both historical
        // executions share one OrchestrationTask (task.Id), and LlmJudgeScorer tags its cost
        // rows with context.OrchestrationTaskId (the trace's own task), so both judge cost rows
        // land under task.Id. This proves Source=Eval segregation directly, rather than only
        // inferring it from the aggregate below being empty (which is also excluded via the
        // AgentExecutionId != null check, so that assertion alone can't prove Source works).
        var costLedgerRows = await costLedgerRepository.GetByTaskIdAsync(task.Id, CancellationToken.None);
        costLedgerRows.Should().HaveCount(2, "one judge cost row per scored trace");
        costLedgerRows.Should().OnlyContain(c => c.Source == CostSource.Eval);
        costLedgerRows.Should().OnlyContain(c => c.AgentExecutionId == null);

        var costRows = await costLedgerRepository.GetDailyAggregatesAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
        costRows.Should().BeEmpty("post-hoc judge cost is tagged Source=Eval and must not reach the production cost dashboard");
    }
}
