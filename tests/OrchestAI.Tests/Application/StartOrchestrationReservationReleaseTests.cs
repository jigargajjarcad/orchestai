using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

// Covers the reservation try/finally added in Task 7 — StartOrchestrationHandler must release
// the admission reservation on every exit path (success, task failure, unhandled exception),
// never leaking a tenant's concurrency/budget capacity. The genuine process-crash path (where
// even this finally can't run) is covered separately by Task 11's staleness-TTL test.
public sealed class StartOrchestrationReservationReleaseTests
{
    private static (StartOrchestrationHandler Handler, Mock<ITaskAdmissionReservationRepository> ReservationRepoMock,
        Mock<IOrchestratorAgent> OrchestratorMock, Mock<IOrchestrationTaskRepository> TaskRepoMock) CreateHandler(
        OrchestrationTask task)
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        var agentFactoryMock = new Mock<IAgentFactory>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        checkpointRepoMock
            .Setup(r => r.DeleteByTaskIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock
            .Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 10, 100, 50m, 500m, 100));
        var tenantAccessorMock = new Mock<ICurrentTenantAccessor>();
        tenantAccessorMock.Setup(a => a.TenantId).Returns(Guid.NewGuid());
        var toolCallBudgetMock = new Mock<ITaskToolCallBudget>();
        toolCallBudgetMock.Setup(b => b.BeginScope(It.IsAny<int>())).Returns(Mock.Of<IDisposable>());

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object, eventBusMock.Object,
            approvalGatewayMock.Object, checkpointRepoMock.Object, reservationRepoMock.Object,
            limitsProviderMock.Object, tenantAccessorMock.Object, Mock.Of<IRejectionEventRepository>(),
            toolCallBudgetMock.Object,
            NullLogger<StartOrchestrationHandler>.Instance);

        return (handler, reservationRepoMock, orchestratorMock, taskRepoMock);
    }

    [Fact]
    public async Task Handle_TaskNotRunning_ThrowsInvalidOperationException()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false); // still Pending
        var (handler, _, _, _) = CreateHandler(task);

        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_PlanAsyncThrows_StillReleasesReservation()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        task.MarkRunning();
        var (handler, reservationRepoMock, orchestratorMock, _) = CreateHandler(task);
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM provider unavailable"));

        // Task 2 fix: a PlanAsync failure is now caught inside Handle (marking the task Failed)
        // rather than propagating — see StartOrchestrationPlanningFailureTests for full coverage
        // of that behavior. This test only re-confirms the reservation-release guarantee still
        // holds on this exit path now that it no longer throws.
        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        await act.Should().NotThrowAsync();
        reservationRepoMock.Verify(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()), Times.Once,
            "the reservation must be released even when planning fails before completing");
    }

    [Fact]
    public async Task Handle_ReleaseAsyncThrowsAfterUnhandledException_DoesNotMaskTheOriginalOutcome()
    {
        // PlanAsync failures are now caught internally (Task 2), so to exercise "an unhandled
        // exception propagates even though ReleaseAsync itself also throws" this uses a failure
        // point that is still outside the catch boundary: the final task-repository persist
        // after a successful plan/execute/review with zero agents dispatched.
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        task.MarkRunning();
        var (handler, reservationRepoMock, orchestratorMock, taskRepoMock) = CreateHandler(task);

        var orchestratorExecution = new AgentExecutionResult(
            Guid.NewGuid(), "plan text", true, 10, 10, 0.01m);
        var plan = new OrchestrationPlan(
            Plan: "plan text",
            ExecutionMode: ExecutionMode.Parallel,
            SelectedAgents: [],
            ExecutionOrder: [],
            AgentPrompts: new Dictionary<AgentType, string>(),
            OrchestratorExecution: orchestratorExecution);
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        orchestratorMock
            .Setup(o => o.ReviewAsync(
                task.Id, task.UserPrompt, plan, It.IsAny<IReadOnlyList<AgentExecutionResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), "reviewed", true, 5, 5, 0.001m));

        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable persisting final task state"));

        reservationRepoMock.Setup(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable during release"));

        // The release failure must be caught and logged internally (best-effort, matching the
        // existing MaybeRecordUsageAsync/SaveCheckpointAsync pattern) — the ORIGINAL persist
        // failure must still be what propagates, not the release failure masking it.
        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("DB unavailable persisting final task state");
    }
}
