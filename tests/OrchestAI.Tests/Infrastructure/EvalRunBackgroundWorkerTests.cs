using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalRunBackgroundWorkerTests
{
    private sealed class StubAgent(AgentExecutionResult result) : IAgent
    {
        public Task<AgentExecutionResult> ExecuteAsync(
            Guid orchestrationTaskId, Guid userId, string userPrompt,
            CancellationToken cancellationToken = default, string? parentSpanId = null, Guid? evalRunId = null) =>
            Task.FromResult(result);
    }

    [Fact]
    public async Task ProcessRunAsync_StubAgentSucceeds_PersistsEvalResultLinkedToAgentExecutionId()
    {
        var suite = EvalSuite.Create("Research suite", "desc", AgentType.Research);
        var evalCase = EvalCase.Create(
            suite.Id, "{\"prompt\":\"hi\"}", "{\"mode\":\"ExactMatch\",\"expected\":\"hello\"}",
            EvalScorerType.RuleBased, regressionThreshold: 0.1m);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { evalCase });

        var run = EvalRun.Create(suite.Id, "commit-abc123", baselineRunId: null);
        var stubExecutionId = Guid.NewGuid();
        var stubAgent = new StubAgent(new AgentExecutionResult(stubExecutionId, "hello", true, 10, 5, 0.001m));

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdWithCasesAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(stubAgent);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        EvalResult? captured = null;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback<EvalResult, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var worker = BuildWorker(
            suiteRepoMock.Object, runRepoMock.Object, resultRepoMock.Object, taskRepoMock.Object, agentFactoryMock.Object);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.Status.Should().Be(EvalRunStatus.Completed);
        captured.Should().NotBeNull();
        captured!.AgentExecutionId.Should().Be(stubExecutionId);
        captured.EvalCaseId.Should().Be(evalCase.Id);
        captured.Passed.Should().BeTrue();
        captured.Score.Should().Be(1.0m);
    }

    [Fact]
    public async Task ProcessRunAsync_AgentInvocationFails_RecordsFailedEvalResultWithoutThrowing()
    {
        var suite = EvalSuite.Create("Research suite", "desc", AgentType.Research);
        var evalCase = EvalCase.Create(
            suite.Id, "{}", "{\"mode\":\"ExactMatch\",\"expected\":\"x\"}",
            EvalScorerType.RuleBased, regressionThreshold: 0.1m);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { evalCase });

        var run = EvalRun.Create(suite.Id, "commit-abc123", null);
        var failedExecutionId = Guid.NewGuid();
        var stubAgent = new StubAgent(new AgentExecutionResult(
            failedExecutionId, string.Empty, false, 0, 0, 0m, ErrorMessage: "provider timed out"));

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdWithCasesAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(stubAgent);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        EvalResult? captured = null;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback<EvalResult, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var worker = BuildWorker(
            suiteRepoMock.Object, runRepoMock.Object, resultRepoMock.Object, taskRepoMock.Object, agentFactoryMock.Object);

        var act = async () => await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        await act.Should().NotThrowAsync();
        run.Status.Should().Be(EvalRunStatus.Completed);
        captured.Should().NotBeNull();
        captured!.Passed.Should().BeFalse();
        captured.Score.Should().Be(0m);
        captured.AgentExecutionId.Should().Be(failedExecutionId);
    }

    [Fact]
    public async Task ProcessRunAsync_SuiteFetchThrows_MarksRunFailedWithoutThrowing()
    {
        var suiteId = Guid.NewGuid();
        var run = EvalRun.Create(suiteId, "commit-abc123", baselineRunId: null);

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock
            .Setup(r => r.GetByIdWithCasesAsync(suiteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient DB error"));

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        EvalRun? updatedRun = null;
        runRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()))
            .Callback<EvalRun, CancellationToken>((r, _) => updatedRun = r)
            .Returns(Task.CompletedTask);

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        var agentFactoryMock = new Mock<IAgentFactory>();
        var resultRepoMock = new Mock<IEvalResultRepository>();

        var worker = BuildWorker(
            suiteRepoMock.Object, runRepoMock.Object, resultRepoMock.Object, taskRepoMock.Object, agentFactoryMock.Object);

        var act = async () => await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        await act.Should().NotThrowAsync();
        run.Status.Should().Be(EvalRunStatus.Failed);
        updatedRun.Should().BeSameAs(run);
        runRepoMock.Verify(r => r.UpdateAsync(It.Is<EvalRun>(r => r.Status == EvalRunStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EvalRunBackgroundWorker BuildWorker(
        IEvalSuiteRepository suiteRepo, IEvalRunRepository runRepo, IEvalResultRepository resultRepo,
        IOrchestrationTaskRepository taskRepo, IAgentFactory agentFactory, ITenantRepository? tenantRepo = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(suiteRepo);
        services.AddSingleton(runRepo);
        services.AddSingleton(resultRepo);
        services.AddSingleton(taskRepo);
        services.AddSingleton(agentFactory);
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([new RuleBasedScorer()]));
        services.AddSingleton(tenantRepo ?? BuildActiveTenantRepository());
        var provider = services.BuildServiceProvider();

        var queueMock = new Mock<IEvalRunQueue>();
        return new EvalRunBackgroundWorker(
            queueMock.Object, provider.GetRequiredService<IServiceScopeFactory>(),
            new AsyncLocalCurrentTenantAccessor(), NullLogger<EvalRunBackgroundWorker>.Instance);
    }

    // Every existing test in this file calls ProcessRunAsync directly with an EvalRun that never
    // had a real tenant stamped (TenantId defaults to Guid.Empty) — the new tenant-suspension
    // check added in Task 11 needs SOME active tenant to resolve for any GetByIdAsync call, or
    // every test here would start failing with "tenant not active" instead of exercising the
    // live-suite behavior they actually test. See EvalRunBackgroundWorkerPostHocTests.cs for the
    // dedicated suspended-tenant test.
    private static ITenantRepository BuildActiveTenantRepository()
    {
        var mock = new Mock<ITenantRepository>();
        var tenant = Tenant.Create("Test Tenant", "test-tenant");
        mock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        return mock.Object;
    }
}
