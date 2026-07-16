using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class StartOrchestrationAgentCapTests
{
    [Fact]
    public async Task Handle_PlanExceedsMaxAgentsPerTask_FailsTaskWithoutDispatchingAnyAgent()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        task.MarkRunning();
        var tenantId = Guid.NewGuid();

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var orchestratorExecution = new AgentExecutionResult(
            Guid.NewGuid(), "plan text", true, 10, 10, 0.01m, ErrorMessage: null, SpanId: "span-1");
        var plan = new OrchestrationPlan(
            Plan: "plan text",
            ExecutionMode: ExecutionMode.Parallel,
            SelectedAgents: [AgentType.Research, AgentType.Writer, AgentType.Code, AgentType.Data, AgentType.Browser, AgentType.Research],
            ExecutionOrder: [],
            AgentPrompts: new Dictionary<AgentType, string>(),
            OrchestratorExecution: orchestratorExecution);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var agentFactoryMock = new Mock<IAgentFactory>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();
        var rejectionEventRepoMock = new Mock<IRejectionEventRepository>();
        var toolCallBudgetMock = new Mock<ITaskToolCallBudget>();
        toolCallBudgetMock.Setup(b => b.BeginScope(It.IsAny<int>())).Returns(Mock.Of<IDisposable>());

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 4, 20, 50m, 500m, 100)); // MaxAgentsPerTask = 4

        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(tenantId);

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object, eventBusMock.Object,
            approvalGatewayMock.Object, checkpointRepoMock.Object, reservationRepoMock.Object,
            limitsProviderMock.Object, accessorMock.Object, rejectionEventRepoMock.Object, toolCallBudgetMock.Object,
            NullLogger<StartOrchestrationHandler>.Instance);

        var response = await handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        response.AgentExecutionIds.Should().BeEmpty();
        task.Status.Should().Be(OrchestrationTaskStatus.Failed);
        agentFactoryMock.Verify(f => f.Create(It.IsAny<AgentType>()), Times.Never,
            "exceeding the agent cap must fail the task before dispatching a single agent — never a partial/silent truncation");
        rejectionEventRepoMock.Verify(r => r.AddAsync(
            It.Is<RejectionEvent>(e => e.Reason == RejectionReason.AgentCapExceeded), It.IsAny<CancellationToken>()), Times.Once);
        reservationRepoMock.Verify(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()), Times.Once,
            "the admission reservation must still be released even when the task fails at the agent-cap check");
    }
}
