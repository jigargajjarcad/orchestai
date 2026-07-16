using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class StartOrchestrationHandlerTests
{
    private readonly Mock<IOrchestrationTaskRepository> _taskRepositoryMock;
    private readonly Mock<IOrchestratorAgent> _orchestratorMock;
    private readonly Mock<IAgentFactory> _agentFactoryMock;
    private readonly Mock<IOrchestrationEventBus> _eventBusMock;
    private readonly Mock<IApprovalGateway> _approvalGatewayMock;
    private readonly Mock<ITaskCheckpointRepository> _checkpointRepositoryMock;
    private readonly Mock<ITaskAdmissionReservationRepository> _reservationRepositoryMock;
    private readonly Mock<ITenantLimitsProvider> _limitsProviderMock;
    private readonly Mock<ICurrentTenantAccessor> _tenantAccessorMock;
    private readonly Mock<IRejectionEventRepository> _rejectionEventRepositoryMock;
    private readonly Mock<ITaskToolCallBudget> _toolCallBudgetMock;
    private readonly Mock<ILogger<StartOrchestrationHandler>> _loggerMock;
    private readonly StartOrchestrationHandler _handler;

    private static readonly Guid DevUserId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    public StartOrchestrationHandlerTests()
    {
        _taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        _orchestratorMock = new Mock<IOrchestratorAgent>();
        _agentFactoryMock = new Mock<IAgentFactory>();
        _eventBusMock = new Mock<IOrchestrationEventBus>();
        _approvalGatewayMock = new Mock<IApprovalGateway>();
        _checkpointRepositoryMock = new Mock<ITaskCheckpointRepository>();
        _checkpointRepositoryMock
            .Setup(r => r.DeleteByTaskIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _reservationRepositoryMock = new Mock<ITaskAdmissionReservationRepository>();
        _reservationRepositoryMock
            .Setup(r => r.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _limitsProviderMock = new Mock<ITenantLimitsProvider>();
        _limitsProviderMock
            .Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 10, 100, 50m, 500m, 100));
        _tenantAccessorMock = new Mock<ICurrentTenantAccessor>();
        _tenantAccessorMock.Setup(a => a.TenantId).Returns(Guid.NewGuid());
        _rejectionEventRepositoryMock = new Mock<IRejectionEventRepository>();
        _toolCallBudgetMock = new Mock<ITaskToolCallBudget>();
        _toolCallBudgetMock.Setup(b => b.BeginScope(It.IsAny<int>())).Returns(Mock.Of<IDisposable>());
        _loggerMock = new Mock<ILogger<StartOrchestrationHandler>>();

        _handler = new StartOrchestrationHandler(
            _taskRepositoryMock.Object,
            _orchestratorMock.Object,
            _agentFactoryMock.Object,
            _eventBusMock.Object,
            _approvalGatewayMock.Object,
            _checkpointRepositoryMock.Object,
            _reservationRepositoryMock.Object,
            _limitsProviderMock.Object,
            _tenantAccessorMock.Object,
            _rejectionEventRepositoryMock.Object,
            _toolCallBudgetMock.Object,
            _loggerMock.Object);
    }

    // The handler now always calls ReviewAsync once sub-agents finish, regardless of outcome.
    private void SetupReview(Guid taskId, OrchestrationPlan plan, string text = "Reviewed synthesis.")
    {
        _orchestratorMock
            .Setup(o => o.ReviewAsync(
                taskId, It.IsAny<string>(), plan, It.IsAny<IReadOnlyList<AgentExecutionResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), text, true, 20, 10, 0.0001m));
    }

    [Fact]
    public async Task Handle_TaskNotFound_ThrowsNotFoundException()
    {
        var taskId = Guid.NewGuid();
        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationTask?)null);

        var act = () => _handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>()
            .Where(ex => ex.EntityId.Equals(taskId));
    }

    // Superseded by Task 7: previously this guard rejected re-entry on an already-Running task
    // (the old handler itself performed the Pending -> Running transition). Now admission
    // (Task 6) performs that CAS before this handler ever runs, so "Running" is the expected
    // precondition, not an error state. The remaining defensive guard now rejects the case where
    // the task has NOT yet been admitted to Running (e.g. reached here still Pending).
    [Fact]
    public async Task Handle_TaskStillPending_ThrowsInvalidOperationException()
    {
        var task = OrchestrationTask.Create(DevUserId, "Test Task", "Test prompt"); // still Pending

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var act = () => _handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Pending*");
    }

    [Fact]
    public async Task Handle_TaskAlreadyCompleted_ThrowsInvalidOperationException()
    {
        var task = OrchestrationTask.Create(DevUserId, "Test Task", "Test prompt");
        task.MarkRunning();
        task.MarkCompleted("done");

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var act = () => _handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_HappyPath_OrchestratorCalledAndAgentsSpawned()
    {
        var task = OrchestrationTask.Create(DevUserId, "Research Task", "Research .NET 9");
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;
        var researchExecutionId = Guid.NewGuid();
        var writerExecutionId = Guid.NewGuid();

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var plan = new OrchestrationPlan(
            "Research and write a report on .NET 9",
            ExecutionMode.Parallel,
            [AgentType.Research, AgentType.Writer],
            [AgentType.Research, AgentType.Writer],
            new Dictionary<AgentType, string>
            {
                [AgentType.Research] = "Research .NET 9 improvements",
                [AgentType.Writer] = "Write a report on .NET 9 improvements"
            },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 100, 50, 0.0001m));

        _orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        SetupReview(taskId, plan);

        var researchResult = new AgentExecutionResult(
            researchExecutionId, "Research findings...", true, 500, 300, 0.001m);
        var writerResult = new AgentExecutionResult(
            writerExecutionId, "Written report...", true, 400, 600, 0.003m);

        var mockResearchAgent = new Mock<IAgent>();
        mockResearchAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, plan.AgentPrompts[AgentType.Research], It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(researchResult);

        var mockWriterAgent = new Mock<IAgent>();
        mockWriterAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, plan.AgentPrompts[AgentType.Writer], It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(writerResult);

        _agentFactoryMock
            .Setup(f => f.Create(AgentType.Research))
            .Returns(mockResearchAgent.Object);
        _agentFactoryMock
            .Setup(f => f.Create(AgentType.Writer))
            .Returns(mockWriterAgent.Object);

        var response = await _handler.Handle(
            new StartOrchestrationCommand(taskId), CancellationToken.None);

        response.TaskId.Should().Be(taskId);
        response.AgentExecutionIds.Should().HaveCount(2);
        response.AgentExecutionIds.Should().Contain(researchExecutionId);
        response.AgentExecutionIds.Should().Contain(writerExecutionId);

        _orchestratorMock.Verify(
            o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()),
            Times.Once);

        _agentFactoryMock.Verify(f => f.Create(AgentType.Research), Times.Once);
        _agentFactoryMock.Verify(f => f.Create(AgentType.Writer), Times.Once);

        // Task 7: the handler no longer performs its own Pending -> Running UpdateAsync (that CAS
        // now happens in AdmitOrchestrationTaskHandler, Task 6) — only the final MarkCompleted
        // persist happens here.
        _taskRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OneAgentFails_TaskMarkedFailed()
    {
        var task = OrchestrationTask.Create(DevUserId, "Code Task", "Write a sorting algorithm");
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var plan = new OrchestrationPlan(
            "Write a sorting algorithm",
            ExecutionMode.Parallel,
            [AgentType.Code],
            [AgentType.Code],
            new Dictionary<AgentType, string>
            {
                [AgentType.Code] = "Write a quicksort implementation in C#"
            },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 50, 25, 0.00005m));

        _orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        SetupReview(taskId, plan);

        var failedResult = new AgentExecutionResult(
            Guid.NewGuid(), string.Empty, false, 0, 0, 0m, "API timeout");

        var mockCodeAgent = new Mock<IAgent>();
        mockCodeAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, plan.AgentPrompts[AgentType.Code], It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(failedResult);

        _agentFactoryMock
            .Setup(f => f.Create(AgentType.Code))
            .Returns(mockCodeAgent.Object);

        await _handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        // Verify task ended in Failed status via the SSE event (avoids Moq reference-type mutation issue)
        _eventBusMock.Verify(
            b => b.Publish(taskId, It.Is<SseEvent>(e => e.Event == "task_failed")),
            Times.Once);

        // Task 7: the handler no longer performs its own Pending -> Running UpdateAsync (that CAS
        // now happens in AdmitOrchestrationTaskHandler, Task 6) — only the final MarkFailed
        // persist happens here.
        _taskRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SequentialMode_PriorOutputInjectedIntoNextAgentPrompt()
    {
        var task = OrchestrationTask.Create(DevUserId, "Sequential Task", "Research then write");
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;
        const string researchOutput = "Findings: .NET 9 improves performance by 20%.";

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var plan = new OrchestrationPlan(
            "Research then write sequentially",
            ExecutionMode.Sequential,
            [AgentType.Research, AgentType.Writer],
            [AgentType.Research, AgentType.Writer],
            new Dictionary<AgentType, string>
            {
                [AgentType.Research] = "Research .NET 9 improvements",
                [AgentType.Writer] = "Write a summary report"
            },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 100, 50, 0.0001m));

        _orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        SetupReview(taskId, plan);

        var researchResult = new AgentExecutionResult(Guid.NewGuid(), researchOutput, true, 400, 200, 0.001m);
        string capturedWriterPrompt = string.Empty;
        var writerResult = new AgentExecutionResult(Guid.NewGuid(), "Final report.", true, 300, 400, 0.002m);

        var mockResearchAgent = new Mock<IAgent>();
        mockResearchAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(researchResult);

        var mockWriterAgent = new Mock<IAgent>();
        mockWriterAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<Guid, Guid, string, CancellationToken, string?, Guid?>((_, _, prompt, _, _, _) => capturedWriterPrompt = prompt)
            .ReturnsAsync(writerResult);

        _agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(mockResearchAgent.Object);
        _agentFactoryMock.Setup(f => f.Create(AgentType.Writer)).Returns(mockWriterAgent.Object);

        await _handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        capturedWriterPrompt.Should().Contain("Write a summary report");
        capturedWriterPrompt.Should().Contain(researchOutput);
        capturedWriterPrompt.Should().Contain("--- Prior Agent Output ---");
    }

    [Fact]
    public async Task Handle_SequentialMode_AgentFailContinuesToNextWithoutPriorOutput()
    {
        var task = OrchestrationTask.Create(DevUserId, "Sequential Task", "Research then write");
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        const string writerBasePrompt = "Write a summary report";
        var plan = new OrchestrationPlan(
            "Research then write sequentially",
            ExecutionMode.Sequential,
            [AgentType.Research, AgentType.Writer],
            [AgentType.Research, AgentType.Writer],
            new Dictionary<AgentType, string>
            {
                [AgentType.Research] = "Research .NET 9 improvements",
                [AgentType.Writer] = writerBasePrompt
            },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 100, 50, 0.0001m));

        _orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        SetupReview(taskId, plan);

        var failedResearch = new AgentExecutionResult(Guid.NewGuid(), string.Empty, false, 0, 0, 0m, "API error");
        string capturedWriterPrompt = string.Empty;
        var writerResult = new AgentExecutionResult(Guid.NewGuid(), "Report written.", true, 300, 400, 0.002m);

        var mockResearchAgent = new Mock<IAgent>();
        mockResearchAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(failedResearch);

        var mockWriterAgent = new Mock<IAgent>();
        mockWriterAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<Guid, Guid, string, CancellationToken, string?, Guid?>((_, _, prompt, _, _, _) => capturedWriterPrompt = prompt)
            .ReturnsAsync(writerResult);

        _agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(mockResearchAgent.Object);
        _agentFactoryMock.Setup(f => f.Create(AgentType.Writer)).Returns(mockWriterAgent.Object);

        await _handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        // Writer still runs despite research failure
        mockWriterAgent.Verify(
            a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()), Times.Once);

        // Writer receives only its base prompt — no prior context since research failed
        capturedWriterPrompt.Should().Be(writerBasePrompt);
    }

    [Fact]
    public async Task Handle_SequentialMode_AllSucceed_PublishesTaskCompleted()
    {
        var task = OrchestrationTask.Create(DevUserId, "Sequential Task", "Sequential pipeline");
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var plan = new OrchestrationPlan(
            "Sequential research and analysis",
            ExecutionMode.Sequential,
            [AgentType.Research, AgentType.Data],
            [AgentType.Research, AgentType.Data],
            new Dictionary<AgentType, string>
            {
                [AgentType.Research] = "Research AI libraries",
                [AgentType.Data] = "Analyze the research data"
            },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 100, 50, 0.0001m));

        _orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        SetupReview(taskId, plan);

        var researchResult = new AgentExecutionResult(Guid.NewGuid(), "Research output.", true, 400, 200, 0.001m);
        var dataResult = new AgentExecutionResult(Guid.NewGuid(), "Analysis output.", true, 300, 400, 0.002m);

        var mockResearchAgent = new Mock<IAgent>();
        mockResearchAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(researchResult);

        var mockDataAgent = new Mock<IAgent>();
        mockDataAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(dataResult);

        _agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(mockResearchAgent.Object);
        _agentFactoryMock.Setup(f => f.Create(AgentType.Data)).Returns(mockDataAgent.Object);

        await _handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        _eventBusMock.Verify(
            b => b.Publish(taskId, It.Is<SseEvent>(e => e.Event == "task_completed")),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SequentialMode_AgentsRunInOrder()
    {
        var task = OrchestrationTask.Create(DevUserId, "Order Test", "Run in order");
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;
        var callOrder = new List<AgentType>();

        _taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        _taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var plan = new OrchestrationPlan(
            "Ordered sequential execution",
            ExecutionMode.Sequential,
            [AgentType.Research, AgentType.Code],
            [AgentType.Research, AgentType.Code],
            new Dictionary<AgentType, string>
            {
                [AgentType.Research] = "Research the topic",
                [AgentType.Code] = "Write code based on research"
            },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 100, 50, 0.0001m));

        _orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        SetupReview(taskId, plan);

        var researchResult = new AgentExecutionResult(Guid.NewGuid(), "Research done.", true, 400, 200, 0.001m);
        var codeResult = new AgentExecutionResult(Guid.NewGuid(), "Code written.", true, 300, 400, 0.002m);

        var mockResearchAgent = new Mock<IAgent>();
        mockResearchAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<Guid, Guid, string, CancellationToken, string?, Guid?>((_, _, _, _, _, _) => callOrder.Add(AgentType.Research))
            .ReturnsAsync(researchResult);

        var mockCodeAgent = new Mock<IAgent>();
        mockCodeAgent
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .Callback<Guid, Guid, string, CancellationToken, string?, Guid?>((_, _, _, _, _, _) => callOrder.Add(AgentType.Code))
            .ReturnsAsync(codeResult);

        _agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(mockResearchAgent.Object);
        _agentFactoryMock.Setup(f => f.Create(AgentType.Code)).Returns(mockCodeAgent.Object);

        await _handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        callOrder.Should().HaveCount(2);
        callOrder[0].Should().Be(AgentType.Research);
        callOrder[1].Should().Be(AgentType.Code);
    }
}
