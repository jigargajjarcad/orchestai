using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Queries.GetExecutionSummary;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetExecutionSummaryHandlerTests
{
    private readonly Mock<IOrchestrationTaskRepository> _taskRepositoryMock;
    private readonly Mock<ICostLedgerRepository> _costLedgerRepositoryMock;
    private readonly Mock<IAgentRetryAttemptRepository> _retryAttemptRepositoryMock;
    private readonly GetExecutionSummaryHandler _handler;

    public GetExecutionSummaryHandlerTests()
    {
        _taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        _costLedgerRepositoryMock = new Mock<ICostLedgerRepository>();
        _retryAttemptRepositoryMock = new Mock<IAgentRetryAttemptRepository>();
        _handler = new GetExecutionSummaryHandler(
            _taskRepositoryMock.Object,
            _costLedgerRepositoryMock.Object,
            _retryAttemptRepositoryMock.Object,
            NullLogger<GetExecutionSummaryHandler>.Instance);
    }

    private static void SeedExecutions(OrchestrationTask task, params AgentExecution[] executions)
    {
        var field = typeof(OrchestrationTask).GetField("_agentExecutions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((List<AgentExecution>)field.GetValue(task)!).AddRange(executions);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNull()
    {
        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsMessagesAndToolCallsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationTask?)null);

        var result = await _handler.Handle(new GetExecutionSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TaskWithMixedResults_ReportsCountsAndFlagsCorrectly()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "Test", "Do something");
        task.MarkRunning();
        task.MarkResumed();

        var research = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        research.Start();
        research.Complete("result", 100, 50, 0.001m);
        research.SetMemoriesInjected(2);

        var writer = AgentExecution.Create(task.Id, AgentType.Writer, "prompt2");
        writer.Start();
        writer.Fail("boom", ExecutionErrorCategory.ProviderError);

        SeedExecutions(task, research, writer);
        task.MarkCompleted("final");

        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsMessagesAndToolCallsAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        _costLedgerRepositoryMock
            .Setup(r => r.GetByTaskIdAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([CostLedger.Create(task.Id, "anthropic/claude-haiku-4-5-20251001", 100, 50, 0.001m, research.Id)]);

        _retryAttemptRepositoryMock
            .Setup(r => r.GetByAgentExecutionIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([AgentRetryAttempt.Create(writer.Id, 1, 500, "transient error")]);

        var result = await _handler.Handle(new GetExecutionSummaryQuery(task.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.AgentsInvolved.Should().BeEquivalentTo(["Research", "Writer"]);
        result.ErrorCount.Should().Be(1);
        result.RetryCount.Should().Be(1);
        result.MemoryUsed.Should().BeTrue();
        result.CheckpointRestored.Should().BeTrue();
        result.ProvidersAndModels.Should().ContainSingle().Which.Should().Be("anthropic/claude-haiku-4-5-20251001");
    }
}
