using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Queries.GetTaskComparison;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetTaskComparisonHandlerTests
{
    private readonly Mock<IOrchestrationTaskRepository> _taskRepositoryMock;
    private readonly GetTaskComparisonHandler _handler;

    public GetTaskComparisonHandlerTests()
    {
        _taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        _handler = new GetTaskComparisonHandler(
            _taskRepositoryMock.Object, NullLogger<GetTaskComparisonHandler>.Instance);
    }

    private static void SeedExecutions(OrchestrationTask task, params AgentExecution[] executions)
    {
        var field = typeof(OrchestrationTask).GetField("_agentExecutions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((List<AgentExecution>)field.GetValue(task)!).AddRange(executions);
    }

    [Fact]
    public async Task Handle_EitherTaskMissing_ReturnsNull()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsAsync(firstId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrchestrationTask.Create(Guid.NewGuid(), "T", "P"));
        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsAsync(secondId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationTask?)null);

        var result = await _handler.Handle(new GetTaskComparisonQuery(firstId, secondId), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BothTasksExist_ReturnsBothSidesWithExecutions()
    {
        var firstTask = OrchestrationTask.Create(Guid.NewGuid(), "First", "Prompt one");
        var firstExec = AgentExecution.Create(firstTask.Id, AgentType.Research, "p");
        firstExec.Start();
        firstExec.Complete("out", 100, 50, 0.001m);
        SeedExecutions(firstTask, firstExec);
        firstTask.MarkCompleted("final one");

        var secondTask = OrchestrationTask.Create(Guid.NewGuid(), "Second", "Prompt two");
        var secondExec = AgentExecution.Create(secondTask.Id, AgentType.Writer, "p2");
        secondExec.Start();
        secondExec.Complete("out2", 200, 100, 0.002m);
        SeedExecutions(secondTask, secondExec);
        secondTask.MarkCompleted("final two");

        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsAsync(firstTask.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstTask);
        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsAsync(secondTask.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondTask);

        var result = await _handler.Handle(
            new GetTaskComparisonQuery(firstTask.Id, secondTask.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.First.TaskId.Should().Be(firstTask.Id);
        result.First.Executions.Should().ContainSingle().Which.AgentType.Should().Be("Research");
        result.Second.TaskId.Should().Be(secondTask.Id);
        result.Second.Executions.Should().ContainSingle().Which.AgentType.Should().Be("Writer");
    }
}
