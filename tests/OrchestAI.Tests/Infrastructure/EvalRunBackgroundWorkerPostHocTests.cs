using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Application.Configuration;
using OrchestAI.Infrastructure.Eval;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalRunBackgroundWorkerPostHocTests
{
    private static LlmJudgeScorer BuildRealJudgeScorer(string judgeResponseJson, Mock<ICostLedgerRepository> costRepoMock)
    {
        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", judgeResponseJson, [], 200, 40));

        var factoryMock = new Mock<ILlmProviderFactory>();
        factoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock
            .Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));

        costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001",
            DefaultJudgePassThreshold = 0.7m
        });

        return new LlmJudgeScorer(factoryMock.Object, pricingCacheMock.Object, costRepoMock.Object, options);
    }

    private static LlmJudgeScorer BuildThrowingJudgeScorer(Mock<ICostLedgerRepository> costRepoMock)
    {
        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient LLM failure"));

        var factoryMock = new Mock<ILlmProviderFactory>();
        factoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock
            .Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));

        var options = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001",
            DefaultJudgePassThreshold = 0.7m
        });

        return new LlmJudgeScorer(factoryMock.Object, pricingCacheMock.Object, costRepoMock.Object, options);
    }

    private static EvalRunBackgroundWorker BuildWorker(
        IEvalRunRepository runRepo, IEvalResultRepository resultRepo, IAgentExecutionRepository executionRepo,
        LlmJudgeScorer judgeScorer, ITenantRepository? tenantRepo = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton(runRepo);
        services.AddSingleton(resultRepo);
        services.AddSingleton(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton(executionRepo);
        services.AddSingleton(Mock.Of<IAgentFactory>());
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([judgeScorer]));
        services.AddSingleton(tenantRepo ?? BuildActiveTenantRepository());
        var provider = services.BuildServiceProvider();

        var queueMock = new Mock<IEvalRunQueue>();
        return new EvalRunBackgroundWorker(
            queueMock.Object, provider.GetRequiredService<IServiceScopeFactory>(),
            new AsyncLocalCurrentTenantAccessor(), NullLogger<EvalRunBackgroundWorker>.Instance);
    }

    // Every existing test in this file exercises the PostHoc scoring path itself and never
    // stamped a real tenant onto its EvalRun (TenantId defaults to Guid.Empty) — the new
    // tenant-suspension check added in Task 11 needs SOME active tenant to resolve, or every
    // test here would fail with "tenant not active" before ever reaching the scoring logic
    // they actually test.
    private static ITenantRepository BuildActiveTenantRepository()
    {
        var mock = new Mock<ITenantRepository>();
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        mock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        return mock.Object;
    }

    // Dedicated helper for the tenant-suspension test below: no judge scorer is registered (an
    // empty IEvalScorerFactory) because the suspension check must reject the run before scoring
    // is ever attempted — if ProcessRunAsync regressed and reached the scorer anyway, resolving
    // a scorer type with no registered scorer would throw and fail the test loudly, instead of
    // silently passing.
    private static EvalRunBackgroundWorker BuildWorkerWithTenantRepository(
        IEvalRunRepository runRepo, IEvalResultRepository resultRepo, IAgentExecutionRepository executionRepo,
        ITenantRepository tenantRepo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton(runRepo);
        services.AddSingleton(resultRepo);
        services.AddSingleton(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton(executionRepo);
        services.AddSingleton(Mock.Of<IAgentFactory>());
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([]));
        services.AddSingleton(tenantRepo);
        var provider = services.BuildServiceProvider();

        var queueMock = new Mock<IEvalRunQueue>();
        return new EvalRunBackgroundWorker(
            queueMock.Object, provider.GetRequiredService<IServiceScopeFactory>(),
            new AsyncLocalCurrentTenantAccessor(), NullLogger<EvalRunBackgroundWorker>.Instance);
    }

    private static EvalRun BuildPostHocRun(params Guid[] resolvedTraceIds)
    {
        var criteriaJson = System.Text.Json.JsonSerializer.Serialize(new { resolvedTraceIds });
        return EvalRun.CreatePostHoc("posthoc-1", "was the tool call appropriate?", criteriaJson);
    }

    [Fact]
    public async Task ProcessRunAsync_PostHocRun_ScoresEachTraceWithNullEvalCaseIdAndRubric()
    {
        var taskId = Guid.NewGuid();
        var executionA = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        executionA.Start();
        executionA.Complete("output A", 10, 5, 0.01m);
        var executionB = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        executionB.Start();
        executionB.Complete("output B", 10, 5, 0.01m);

        var run = BuildPostHocRun(executionA.Id, executionB.Id);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock
            .Setup(r => r.GetScoredAgentExecutionIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), EvalScorerType.LlmJudge, LlmJudgeScorer.Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        var captured = new List<EvalResult>();
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback<EvalResult, CancellationToken>((r, _) => captured.Add(r))
            .Returns(Task.CompletedTask);

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock.Setup(r => r.GetByIdAsync(executionA.Id, It.IsAny<CancellationToken>())).ReturnsAsync(executionA);
        executionRepoMock.Setup(r => r.GetByIdAsync(executionB.Id, It.IsAny<CancellationToken>())).ReturnsAsync(executionB);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        var judgeScorer = BuildRealJudgeScorer("""{"score":0.9,"reasoning":"looks correct"}""", costRepoMock);

        var worker = BuildWorker(runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, judgeScorer);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.Status.Should().Be(EvalRunStatus.Completed);
        run.SkippedAlreadyScoredCount.Should().Be(0);
        captured.Should().HaveCount(2);
        captured.Should().OnlyContain(r => r.EvalCaseId == null);
        captured.Should().OnlyContain(r => r.Rubric == "was the tool call appropriate?");
        captured.Select(r => r.AgentExecutionId).Should().BeEquivalentTo([executionA.Id, executionB.Id]);
        costRepoMock.Verify(
            r => r.AddAsync(It.Is<CostLedger>(c => c.Source == CostSource.Eval && c.EvalRunId == run.Id),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        resultRepoMock.Verify(
            r => r.DeletePostHocResultAsync(
                It.IsAny<Guid>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the default (non-force) path never supersedes — it only skips or inserts");
    }

    [Fact]
    public async Task ProcessRunAsync_PostHocRun_SkipsAlreadyScoredTracesAndCountsThem()
    {
        var taskId = Guid.NewGuid();
        var alreadyScored = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        alreadyScored.Start();
        alreadyScored.Complete("output", 10, 5, 0.01m);
        var notYetScored = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        notYetScored.Start();
        notYetScored.Complete("output", 10, 5, 0.01m);

        var run = BuildPostHocRun(alreadyScored.Id, notYetScored.Id);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock
            .Setup(r => r.GetScoredAgentExecutionIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), EvalScorerType.LlmJudge, LlmJudgeScorer.Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { alreadyScored.Id });
        var addCallCount = 0;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback(() => addCallCount++)
            .Returns(Task.CompletedTask);

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock.Setup(r => r.GetByIdAsync(notYetScored.Id, It.IsAny<CancellationToken>())).ReturnsAsync(notYetScored);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        var judgeScorer = BuildRealJudgeScorer("""{"score":0.9,"reasoning":"looks correct"}""", costRepoMock);

        var worker = BuildWorker(runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, judgeScorer);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.SkippedAlreadyScoredCount.Should().Be(1);
        addCallCount.Should().Be(1);
        executionRepoMock.Verify(r => r.GetByIdAsync(alreadyScored.Id, It.IsAny<CancellationToken>()), Times.Never);
        resultRepoMock.Verify(
            r => r.DeletePostHocResultAsync(
                It.IsAny<Guid>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the default (non-force) path skips the already-scored trace outright — it never supersedes");
    }

    [Fact]
    public async Task ProcessRunAsync_ForceRescoreTrue_SupersedesPriorResultWithoutSkippingOrConsultingIdempotencyCheck()
    {
        var taskId = Guid.NewGuid();
        var execution = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        execution.Start();
        execution.Complete("output", 10, 5, 0.01m);

        var criteriaJson = System.Text.Json.JsonSerializer.Serialize(new { resolvedTraceIds = new[] { execution.Id } });
        var run = EvalRun.CreatePostHoc("posthoc-1", "was the tool call appropriate?", criteriaJson, forceRescore: true);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        var deleteCallCount = 0;
        resultRepoMock
            .Setup(r => r.DeletePostHocResultAsync(
                execution.Id, EvalScorerType.LlmJudge, LlmJudgeScorer.Version, It.IsAny<CancellationToken>()))
            .Callback(() => deleteCallCount++)
            .Returns(Task.CompletedTask);
        var addCallCount = 0;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback(() => addCallCount++)
            .Returns(Task.CompletedTask);

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock.Setup(r => r.GetByIdAsync(execution.Id, It.IsAny<CancellationToken>())).ReturnsAsync(execution);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        var judgeScorer = BuildRealJudgeScorer("""{"score":0.95,"reasoning":"corrected score"}""", costRepoMock);

        var worker = BuildWorker(runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, judgeScorer);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        deleteCallCount.Should().Be(1, "ForceRescore supersedes the prior result instead of appending");
        addCallCount.Should().Be(1);
        run.SkippedAlreadyScoredCount.Should().Be(0, "a superseded trace was not skipped, it was re-scored");
        resultRepoMock.Verify(
            r => r.GetScoredAgentExecutionIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "ForceRescore bypasses the idempotency pre-check entirely — it doesn't need to know what was already scored");
    }

    [Fact]
    public async Task ProcessRunAsync_ForceRescoreTrue_ScorerThrows_DoesNotDeletePriorResult()
    {
        var taskId = Guid.NewGuid();
        var execution = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        execution.Start();
        execution.Complete("output", 10, 5, 0.01m);

        var criteriaJson = System.Text.Json.JsonSerializer.Serialize(new { resolvedTraceIds = new[] { execution.Id } });
        var run = EvalRun.CreatePostHoc("posthoc-1", "was the tool call appropriate?", criteriaJson, forceRescore: true);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock
            .Setup(r => r.DeletePostHocResultAsync(
                It.IsAny<Guid>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock.Setup(r => r.GetByIdAsync(execution.Id, It.IsAny<CancellationToken>())).ReturnsAsync(execution);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        var judgeScorer = BuildThrowingJudgeScorer(costRepoMock);

        var worker = BuildWorker(runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, judgeScorer);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        resultRepoMock.Verify(
            r => r.DeletePostHocResultAsync(
                It.IsAny<Guid>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "scoring happens before delete — a scorer failure must leave the stale prior result intact, not delete it first");
        resultRepoMock.Verify(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()), Times.Never);
        run.Status.Should().Be(EvalRunStatus.Completed, "the per-trace try/catch swallows the scorer's exception and the run still completes");
    }

    [Fact]
    public async Task ProcessRunAsync_TenantSuspendedAfterEnqueue_RunMarkedFailedNotCompleted()
    {
        var taskId = Guid.NewGuid();
        var execution = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        execution.Start();
        execution.Complete("output", 10, 5, 0.01m);

        var criteriaJson = System.Text.Json.JsonSerializer.Serialize(new { resolvedTraceIds = new[] { execution.Id } });
        var run = EvalRun.CreatePostHoc("posthoc-1", "was the tool call appropriate?", criteriaJson);
        var tenantId = Guid.NewGuid();
        typeof(EvalRun).GetProperty(nameof(EvalRun.TenantId))!.SetValue(run, tenantId);

        var suspendedTenant = Tenant.Create("Acme", "acme");
        suspendedTenant.Suspend();

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(suspendedTenant);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        var executionRepoMock = new Mock<IAgentExecutionRepository>();

        var worker = BuildWorkerWithTenantRepository(
            runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, tenantRepoMock.Object);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.Status.Should().Be(EvalRunStatus.Failed, "a tenant suspended after enqueue must reject queued work, not silently complete it");
        executionRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "no trace should even be looked up once the owning tenant is known to be suspended");
    }
}
