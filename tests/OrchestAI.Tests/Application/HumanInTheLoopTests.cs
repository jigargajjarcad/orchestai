using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OrchestAI.Application.Commands.ApproveOrchestrationTask;
using OrchestAI.Application.Commands.RejectOrchestrationTask;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class HumanInTheLoopTests
{
    private static readonly Guid DevUserId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    // ── StartOrchestrationHandler: approval gate ─────────────────────────────

    [Fact]
    public async Task StartOrchestration_RequireApproval_EmitsApprovalRequiredAndWaits()
    {
        var task = OrchestrationTask.Create(DevUserId, "Approval Task", "Do something", requireApproval: true);
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;

        var taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        taskRepositoryMock.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var plan = new OrchestrationPlan(
            "Research the topic",
            ExecutionMode.Parallel,
            [AgentType.Research],
            [AgentType.Research],
            new Dictionary<AgentType, string> { [AgentType.Research] = "Research it" },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 50, 25, 0.0001m));

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        orchestratorMock
            .Setup(o => o.ReviewAsync(
                taskId, It.IsAny<string>(), plan, It.IsAny<IReadOnlyList<AgentExecutionResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), "Synthesized.", true, 10, 5, 0.0001m));

        var researchAgentMock = new Mock<IAgent>();
        researchAgentMock
            .Setup(a => a.ExecuteAsync(taskId, DevUserId, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), "Research done.", true, 100, 50, 0.001m));

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(researchAgentMock.Object);

        var eventBusMock = new Mock<IOrchestrationEventBus>();

        var approvalGatewayMock = new Mock<IApprovalGateway>();
        approvalGatewayMock
            .Setup(g => g.WaitForApprovalAsync(taskId, It.IsAny<CancellationToken>()))
            // Simulates the API call that would approve the task while execution is blocked.
            .Callback(() => task.Approve(null))
            .Returns(Task.CompletedTask);

        var checkpointRepositoryMock = new Mock<ITaskCheckpointRepository>();
        checkpointRepositoryMock
            .Setup(r => r.DeleteByTaskIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var reservationRepositoryMock = new Mock<ITaskAdmissionReservationRepository>();
        reservationRepositoryMock
            .Setup(r => r.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new StartOrchestrationHandler(
            taskRepositoryMock.Object,
            orchestratorMock.Object,
            agentFactoryMock.Object,
            eventBusMock.Object,
            approvalGatewayMock.Object,
            checkpointRepositoryMock.Object,
            reservationRepositoryMock.Object,
            new Mock<ILogger<StartOrchestrationHandler>>().Object);

        var response = await handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        eventBusMock.Verify(
            b => b.Publish(taskId, It.Is<SseEvent>(e => e.Event == "approval_required")),
            Times.Once);

        approvalGatewayMock.Verify(g => g.WaitForApprovalAsync(taskId, It.IsAny<CancellationToken>()), Times.Once);

        // Execution resumed after approval — the research agent ran and the task completed.
        agentFactoryMock.Verify(f => f.Create(AgentType.Research), Times.Once);
        response.AgentExecutionIds.Should().NotBeEmpty();
    }

    [Fact]
    public async Task StartOrchestration_Rejected_AbortsBeforeAgentDispatch()
    {
        var task = OrchestrationTask.Create(DevUserId, "Approval Task", "Do something", requireApproval: true);
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run
        var taskId = task.Id;

        var taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        taskRepositoryMock.Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var plan = new OrchestrationPlan(
            "Research the topic",
            ExecutionMode.Parallel,
            [AgentType.Research],
            [AgentType.Research],
            new Dictionary<AgentType, string> { [AgentType.Research] = "Research it" },
            new AgentExecutionResult(Guid.NewGuid(), "{}", true, 50, 25, 0.0001m));

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock
            .Setup(o => o.PlanAsync(taskId, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        var agentFactoryMock = new Mock<IAgentFactory>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();

        var approvalGatewayMock = new Mock<IApprovalGateway>();
        approvalGatewayMock
            .Setup(g => g.WaitForApprovalAsync(taskId, It.IsAny<CancellationToken>()))
            .Callback(() => task.Reject("Not aligned with goals."))
            .Returns(Task.CompletedTask);

        var reservationRepositoryMock = new Mock<ITaskAdmissionReservationRepository>();
        reservationRepositoryMock
            .Setup(r => r.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new StartOrchestrationHandler(
            taskRepositoryMock.Object,
            orchestratorMock.Object,
            agentFactoryMock.Object,
            eventBusMock.Object,
            approvalGatewayMock.Object,
            new Mock<ITaskCheckpointRepository>().Object,
            reservationRepositoryMock.Object,
            new Mock<ILogger<StartOrchestrationHandler>>().Object);

        var response = await handler.Handle(new StartOrchestrationCommand(taskId), CancellationToken.None);

        response.AgentExecutionIds.Should().BeEmpty();
        agentFactoryMock.Verify(f => f.Create(It.IsAny<AgentType>()), Times.Never);
        orchestratorMock.Verify(
            o => o.ReviewAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<OrchestrationPlan>(),
                It.IsAny<IReadOnlyList<AgentExecutionResult>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        task.Status.Should().Be(OrchestrationTaskStatus.Failed);
    }

    // ── ApproveOrchestrationTaskHandler ───────────────────────────────────────

    [Fact]
    public async Task Approve_TaskNotFound_ThrowsNotFoundException()
    {
        var taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        var taskId = Guid.NewGuid();
        taskRepositoryMock
            .Setup(r => r.GetByIdAsync(taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationTask?)null);

        var handler = new ApproveOrchestrationTaskHandler(
            taskRepositoryMock.Object,
            new Mock<IApprovalGateway>().Object,
            new Mock<IOrchestrationEventBus>().Object,
            new Mock<ILogger<ApproveOrchestrationTaskHandler>>().Object);

        var act = () => handler.Handle(new ApproveOrchestrationTaskCommand(taskId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Approve_TaskNotWaitingForApproval_ThrowsConflictException()
    {
        var task = OrchestrationTask.Create(DevUserId, "Task", "Prompt");
        task.MarkRunning();

        var taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        taskRepositoryMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var handler = new ApproveOrchestrationTaskHandler(
            taskRepositoryMock.Object,
            new Mock<IApprovalGateway>().Object,
            new Mock<IOrchestrationEventBus>().Object,
            new Mock<ILogger<ApproveOrchestrationTaskHandler>>().Object);

        var act = () => handler.Handle(new ApproveOrchestrationTaskCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Approve_HappyPath_ApprovesTaskAndSignalsGateway()
    {
        var task = OrchestrationTask.Create(DevUserId, "Task", "Prompt");
        task.MarkRunning();
        task.RequestApproval();

        var taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        taskRepositoryMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();

        var handler = new ApproveOrchestrationTaskHandler(
            taskRepositoryMock.Object,
            approvalGatewayMock.Object,
            eventBusMock.Object,
            new Mock<ILogger<ApproveOrchestrationTaskHandler>>().Object);

        await handler.Handle(new ApproveOrchestrationTaskCommand(task.Id, "Looks good"), CancellationToken.None);

        task.ApprovalStatus.Should().Be(TaskApprovalStatus.Approved);
        task.Status.Should().Be(OrchestrationTaskStatus.Running);

        approvalGatewayMock.Verify(g => g.Signal(task.Id, true), Times.Once);
        eventBusMock.Verify(
            b => b.Publish(task.Id, It.Is<SseEvent>(e => e.Event == "task_approved")),
            Times.Once);
    }

    // ── RejectOrchestrationTaskHandler ────────────────────────────────────────

    [Fact]
    public async Task Reject_TaskNotWaitingForApproval_ThrowsConflictException()
    {
        var task = OrchestrationTask.Create(DevUserId, "Task", "Prompt");

        var taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        taskRepositoryMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var handler = new RejectOrchestrationTaskHandler(
            taskRepositoryMock.Object,
            new Mock<IApprovalGateway>().Object,
            new Mock<IOrchestrationEventBus>().Object,
            new Mock<ILogger<RejectOrchestrationTaskHandler>>().Object);

        var act = () => handler.Handle(new RejectOrchestrationTaskCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Reject_HappyPath_MarksTaskFailedAndSignalsGateway()
    {
        var task = OrchestrationTask.Create(DevUserId, "Task", "Prompt");
        task.MarkRunning();
        task.RequestApproval();

        var taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        taskRepositoryMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();

        var handler = new RejectOrchestrationTaskHandler(
            taskRepositoryMock.Object,
            approvalGatewayMock.Object,
            eventBusMock.Object,
            new Mock<ILogger<RejectOrchestrationTaskHandler>>().Object);

        await handler.Handle(new RejectOrchestrationTaskCommand(task.Id, "Not needed"), CancellationToken.None);

        task.ApprovalStatus.Should().Be(TaskApprovalStatus.Rejected);
        task.Status.Should().Be(OrchestrationTaskStatus.Failed);

        approvalGatewayMock.Verify(g => g.Signal(task.Id, false), Times.Once);
        eventBusMock.Verify(
            b => b.Publish(task.Id, It.Is<SseEvent>(e => e.Event == "task_rejected")),
            Times.Once);
        eventBusMock.Verify(
            b => b.Publish(task.Id, It.Is<SseEvent>(e => e.Event == "task_failed")),
            Times.Once);
    }
}
