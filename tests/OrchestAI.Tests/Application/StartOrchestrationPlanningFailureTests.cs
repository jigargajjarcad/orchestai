using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

// Reproduces the bug this task fixes: a real Orchestrator-planning-level failure (LLM outage,
// rate limit, auth error, malformed JSON surviving retry — see OrchestratorAgent.PlanAsync's
// own catch, which records the failure and rethrows) used to propagate straight out of
// StartOrchestrationHandler.Handle uncaught, leaving the OrchestrationTask stuck at Running
// forever (only TasksController.StartAsync's background Task.Run generic catch-and-log ever saw
// it). Handle must now catch that failure, mark the task Failed, publish task_failed, and still
// release the admission reservation exactly once via the existing finally.
public sealed class StartOrchestrationPlanningFailureTests
{
    [Fact]
    public async Task Handle_PlanAsyncThrows_MarksTaskFailedPublishesEventAndReleasesReservationOnce()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "Do the thing", false);
        task.MarkRunning(); // simulates AdmitOrchestrationTaskHandler (Task 6) having already run

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock
            .Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Orchestrator planning failed after retries: authentication_error"));

        var agentFactoryMock = new Mock<IAgentFactory>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();
        reservationRepoMock
            .Setup(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var rejectionEventRepoMock = new Mock<IRejectionEventRepository>();
        var toolCallBudgetMock = new Mock<ITaskToolCallBudget>();
        toolCallBudgetMock.Setup(b => b.BeginScope(It.IsAny<int>())).Returns(Mock.Of<IDisposable>());

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        var tenantAccessorMock = new Mock<ICurrentTenantAccessor>();
        tenantAccessorMock.Setup(a => a.TenantId).Returns(Guid.NewGuid());

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object, eventBusMock.Object,
            approvalGatewayMock.Object, checkpointRepoMock.Object, reservationRepoMock.Object,
            limitsProviderMock.Object, tenantAccessorMock.Object, rejectionEventRepoMock.Object,
            toolCallBudgetMock.Object,
            NullLogger<StartOrchestrationHandler>.Instance);

        // The bug this fixes meant this would throw, leaving the task stuck at Running — asserting
        // it does NOT throw is itself part of the regression coverage.
        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);
        await act.Should().NotThrowAsync();

        task.Status.Should().Be(OrchestrationTaskStatus.Failed);

        taskRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<OrchestrationTask>(t => t.Status == OrchestrationTaskStatus.Failed),
                It.IsAny<CancellationToken>()),
            Times.Once);

        eventBusMock.Verify(
            b => b.Publish(task.Id, It.Is<SseEvent>(e => e.Event == "task_failed")),
            Times.Once);

        reservationRepoMock.Verify(
            r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()),
            Times.Once,
            "the admission reservation must still be released even when planning itself fails");

        // A planning failure never got as far as selecting/dispatching agents.
        agentFactoryMock.Verify(f => f.Create(It.IsAny<AgentType>()), Times.Never);
        limitsProviderMock.Verify(
            p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "PlanAsync throws before the tenant-limits lookup that depends on a successful plan");
    }
}
