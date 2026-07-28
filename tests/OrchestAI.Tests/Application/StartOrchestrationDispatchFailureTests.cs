using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

// Guards the fix for the highest-priority P2-M4 reliability defect: an unhandled exception in
// the dispatch block (AFTER planning succeeds) — the live case was a KeyNotFoundException from a
// malformed plan reaching agent_prompts[agentType] — used to escape Handle uncaught, so the task
// stayed Running forever and its cost was never aggregated. Handle must now (a) mark the task
// Failed and (b) recover the real per-execution cost from the CostLedger even on the crash path.
public sealed class StartOrchestrationDispatchFailureTests
{
    [Fact]
    public async Task Handle_UnhandledDispatchException_MarksTaskFailedAndAggregatesLedgerCost()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "Do the thing", false);
        task.MarkRunning();

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Planning SUCCEEDS but returns a malformed plan — Data is in execution_order with no
        // Data prompt, so the dispatch's plan.AgentPrompts[Data] throws KeyNotFoundException.
        var orchestratorExec = new AgentExecutionResult(Guid.NewGuid(), "", true, 5, 5, 0.001m, null, "span-orch");
        var malformedPlan = new OrchestrationPlan(
            "plan", ExecutionMode.Parallel,
            SelectedAgents: new[] { AgentType.Data },
            ExecutionOrder: new[] { AgentType.Data },
            AgentPrompts: new Dictionary<AgentType, string>(), // <-- missing the Data prompt
            OrchestratorExecution: orchestratorExec);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(malformedPlan);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(l => l.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 5, 50, 50m, 500m, 100));

        var tenantAccessorMock = new Mock<ICurrentTenantAccessor>();
        tenantAccessorMock.Setup(a => a.TenantId).Returns(Guid.NewGuid());

        // Real per-execution cost persisted before the crash — the failure path must recover it.
        var costLedgerMock = new Mock<ICostLedgerRepository>();
        costLedgerMock.Setup(c => c.GetByTaskIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CostLedger.Create(task.Id, "claude", 100, 50, 0.05m) });

        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();
        reservationRepoMock.Setup(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var toolCallBudgetMock = new Mock<ITaskToolCallBudget>();
        toolCallBudgetMock.Setup(b => b.BeginScope(It.IsAny<int>())).Returns(Mock.Of<IDisposable>());

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, Mock.Of<IAgentFactory>(), eventBusMock.Object,
            Mock.Of<IApprovalGateway>(), Mock.Of<ITaskCheckpointRepository>(), reservationRepoMock.Object,
            limitsProviderMock.Object, tenantAccessorMock.Object, Mock.Of<IRejectionEventRepository>(),
            toolCallBudgetMock.Object, costLedgerMock.Object,
            NullLogger<StartOrchestrationHandler>.Instance);

        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);
        await act.Should().NotThrowAsync("the dispatch crash must be caught, not propagated");

        task.Status.Should().Be(OrchestrationTaskStatus.Failed);
        task.Status.Should().NotBe(OrchestrationTaskStatus.Running, "a crashed task must never stay Running");
        task.TotalCostUsd.Should().Be(0.05m, "real ledger cost must be recovered on the failure path, not reported as $0");

        eventBusMock.Verify(
            b => b.Publish(task.Id, It.Is<SseEvent>(e => e.Event == "task_failed")),
            Times.Once);
        reservationRepoMock.Verify(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
