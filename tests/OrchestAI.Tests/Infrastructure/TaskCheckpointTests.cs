using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.ResumeOrchestrationTask;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Infrastructure;

public sealed class TaskCheckpointTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string ModelName = "claude-haiku-4-5-20251001";

    // ── AgentBase writes/skips checkpoints ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SuccessfulCompletion_WritesCheckpoint()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        mocks.Provider
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Task output.", [], 50, 25));

        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        mocks.CheckpointRepo.Verify(
            r => r.UpsertAsync(
                It.Is<TaskCheckpoint>(c => c.OrchestrationTaskId == TaskId && c.AgentType == AgentType.Code && c.Output == "Task output."),
                It.IsAny<CancellationToken>()),
            Times.Once);

        mocks.EventBus.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "checkpoint_saved")),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FailedCompletion_DoesNotWriteCheckpoint()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        mocks.Provider
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad request"));

        var result = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        result.Success.Should().BeFalse();
        mocks.CheckpointRepo.Verify(
            r => r.UpsertAsync(It.IsAny<TaskCheckpoint>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static (TestAgent Agent, AgentMocks Mocks) BuildAgent()
    {
        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");

        var providerFactoryMock = new Mock<ILlmProviderFactory>();
        providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var execRepoMock = new Mock<IAgentExecutionRepository>();
        execRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        execRepoMock.Setup(r => r.UpdateAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var msgRepoMock = new Mock<IAgentMessageRepository>();
        msgRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        costRepoMock.Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var toolCallRepoMock = new Mock<IMcpToolCallRepository>();

        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        checkpointRepoMock.Setup(r => r.UpsertAsync(It.IsAny<TaskCheckpoint>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var memoryRepoMock = new Mock<IAgentMemoryRepository>();
        memoryRepoMock
            .Setup(r => r.GetRelevantAsync(It.IsAny<Guid>(), It.IsAny<AgentType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var retryAttemptRepoMock = new Mock<IAgentRetryAttemptRepository>();
        retryAttemptRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AgentRetryAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var piiRedactorMock = new Mock<IPiiRedactor>();
        piiRedactorMock.Setup(r => r.IsEnabled).Returns(false);

        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var toolRegistryMock = new Mock<IToolRegistry>();

        var agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Code"] = $"anthropic/{ModelName}" },
            MaxTokens = new Dictionary<string, int> { ["Code"] = 1024 }
        });
        var modelPricingCacheMock = new Mock<IModelPricingCache>();
        modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 0.80m, 4.00m));
        var retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = 1, InitialDelayMs = 1, MaxDelayMs = 5, BackoffMultiplier = 2.0, JitterMs = 1
        });

        var agent = new TestAgent(
            providerFactoryMock.Object, execRepoMock.Object, msgRepoMock.Object, costRepoMock.Object,
            toolCallRepoMock.Object, checkpointRepoMock.Object, memoryRepoMock.Object, retryAttemptRepoMock.Object,
            piiRedactorMock.Object,
            eventBusMock.Object, agentOptions, modelPricingCacheMock.Object, retryOptions, toolRegistryMock.Object,
            new AsyncLocalTaskToolCallBudget(), Mock.Of<IRejectionEventRepository>(),
            NullLoggerFactory.Instance);

        return (agent, new AgentMocks(providerMock, checkpointRepoMock, eventBusMock, toolRegistryMock));
    }

    private sealed record AgentMocks(
        Mock<ILlmProvider> Provider,
        Mock<ITaskCheckpointRepository> CheckpointRepo,
        Mock<IOrchestrationEventBus> EventBus,
        Mock<IToolRegistry> ToolRegistry);

    private sealed class TestAgent : AgentBase
    {
        protected override string SystemPrompt => "You are a test agent.";
        protected override AgentType AgentType => AgentType.Code;

        public TestAgent(
            ILlmProviderFactory llmProviderFactory,
            IAgentExecutionRepository execRepo,
            IAgentMessageRepository msgRepo,
            ICostLedgerRepository costRepo,
            IMcpToolCallRepository toolCallRepo,
            ITaskCheckpointRepository checkpointRepo,
            IAgentMemoryRepository memoryRepo,
            IAgentRetryAttemptRepository retryAttemptRepo,
            IPiiRedactor piiRedactor,
            IOrchestrationEventBus eventBus,
            IOptions<AgentOptions> agentOptions,
            IModelPricingCache modelPricingCache,
            IOptions<RetryPolicyOptions> retryOptions,
            IToolRegistry toolRegistry,
            ITaskToolCallBudget taskToolCallBudget,
            IRejectionEventRepository rejectionEventRepository,
            ILoggerFactory loggerFactory)
            : base(llmProviderFactory, execRepo, msgRepo, costRepo, toolCallRepo, checkpointRepo,
                   memoryRepo, retryAttemptRepo, piiRedactor, eventBus, agentOptions, modelPricingCache, retryOptions,
                   toolRegistry, taskToolCallBudget, rejectionEventRepository, loggerFactory)
        { }
    }

    // ── ResumeOrchestrationTaskHandler ────────────────────────────────────────

    private static readonly Guid DevUserId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    private static void SeedAgentExecutions(OrchestrationTask task, params AgentExecution[] executions)
    {
        // OrchestrationTask.AgentExecutions is populated by EF Core relationship fixup in
        // production — there's no public domain method to seed it, so tests reach past
        // encapsulation the same way the ADR-001 SDK tests do for private SDK setters.
        var field = typeof(OrchestrationTask).GetField("_agentExecutions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (List<AgentExecution>)field.GetValue(task)!;
        list.AddRange(executions);
    }

    [Fact]
    public async Task Resume_TaskPending_ThrowsConflictException()
    {
        var task = OrchestrationTask.Create(DevUserId, "Task", "Prompt");

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdWithExecutionsAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var handler = new ResumeOrchestrationTaskHandler(
            taskRepoMock.Object, new Mock<IOrchestratorAgent>().Object, new Mock<IAgentFactory>().Object,
            new Mock<ITaskCheckpointRepository>().Object, new Mock<IOrchestrationEventBus>().Object,
            new Mock<ILogger<ResumeOrchestrationTaskHandler>>().Object);

        var act = () => handler.Handle(new ResumeOrchestrationTaskCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Resume_TaskCompleted_ThrowsConflictException()
    {
        var task = OrchestrationTask.Create(DevUserId, "Task", "Prompt");
        task.MarkRunning();
        task.MarkCompleted("done");

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdWithExecutionsAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var handler = new ResumeOrchestrationTaskHandler(
            taskRepoMock.Object, new Mock<IOrchestratorAgent>().Object, new Mock<IAgentFactory>().Object,
            new Mock<ITaskCheckpointRepository>().Object, new Mock<IOrchestrationEventBus>().Object,
            new Mock<ILogger<ResumeOrchestrationTaskHandler>>().Object);

        var act = () => handler.Handle(new ResumeOrchestrationTaskCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Resume_LoadsCheckpoints_SkipsCompletedAgentsAndRunsOnlyTheRest()
    {
        var task = OrchestrationTask.Create(DevUserId, "Task", "Research then write");
        task.MarkRunning();
        task.MarkFailed("Writer agent crashed");

        const string planJson =
            """
            {
              "plan": "Research then write",
              "execution_mode": "sequential",
              "agents": ["Research", "Writer"],
              "execution_order": ["Research", "Writer"],
              "agent_prompts": {
                "Research": "Research the topic",
                "Writer": "Write a summary"
              }
            }
            """;

        var planExecution = AgentExecution.Create(task.Id, AgentType.Orchestrator, task.UserPrompt);
        planExecution.Start();
        planExecution.Complete(planJson, 100, 50, 0.001m);
        SeedAgentExecutions(task, planExecution);

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdWithExecutionsAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var researchCheckpoint = TaskCheckpoint.Create(
            task.Id, AgentType.Research, Guid.NewGuid(), "Research findings from checkpoint.", 100, 50, 0.001m);

        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        checkpointRepoMock
            .Setup(r => r.GetByTaskIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([researchCheckpoint]);
        checkpointRepoMock
            .Setup(r => r.DeleteByTaskIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock
            .Setup(o => o.ReviewAsync(
                task.Id, task.UserPrompt, It.IsAny<OrchestrationPlan>(), It.IsAny<IReadOnlyList<AgentExecutionResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), "Final synthesis.", true, 10, 5, 0.0001m));

        var writerAgentMock = new Mock<IAgent>();
        writerAgentMock
            .Setup(a => a.ExecuteAsync(task.Id, task.UserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), "Written report.", true, 200, 100, 0.002m));

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Writer)).Returns(writerAgentMock.Object);

        var eventBusMock = new Mock<IOrchestrationEventBus>();

        var handler = new ResumeOrchestrationTaskHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object,
            checkpointRepoMock.Object, eventBusMock.Object, new Mock<ILogger<ResumeOrchestrationTaskHandler>>().Object);

        var response = await handler.Handle(new ResumeOrchestrationTaskCommand(task.Id), CancellationToken.None);

        response.SkippedAgents.Should().ContainSingle();
        response.SkippedAgents[0].Should().Be(AgentType.Research);
        response.ResumedFrom.Should().ContainSingle();
        response.ResumedFrom[0].Should().Be(AgentType.Writer);

        agentFactoryMock.Verify(f => f.Create(AgentType.Research), Times.Never);
        agentFactoryMock.Verify(f => f.Create(AgentType.Writer), Times.Once);

        eventBusMock.Verify(
            b => b.Publish(task.Id, It.Is<SseEvent>(e => e.Event == "task_resumed")),
            Times.Once);
        eventBusMock.Verify(
            b => b.Publish(task.Id, It.Is<SseEvent>(e => e.Event == "task_completed")),
            Times.Once);

        checkpointRepoMock.Verify(r => r.DeleteByTaskIdAsync(task.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
