using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Queries.GetExecutionTimeline;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetExecutionTimelineHandlerTests
{
    private readonly Mock<IOrchestrationTaskRepository> _taskRepositoryMock;
    private readonly GetExecutionTimelineHandler _handler;

    public GetExecutionTimelineHandlerTests()
    {
        _taskRepositoryMock = new Mock<IOrchestrationTaskRepository>();
        _handler = new GetExecutionTimelineHandler(
            _taskRepositoryMock.Object, NullLogger<GetExecutionTimelineHandler>.Instance);
    }

    // OrchestrationTask.AgentExecutions / AgentExecution.ToolCalls are populated by EF Core
    // relationship fixup in production — there's no public domain method to seed them, so
    // tests reach past encapsulation the same way TaskCheckpointTests does for _agentExecutions.
    private static void SeedExecutions(OrchestrationTask task, params AgentExecution[] executions)
    {
        var field = typeof(OrchestrationTask).GetField("_agentExecutions", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((List<AgentExecution>)field.GetValue(task)!).AddRange(executions);
    }

    private static void SeedToolCalls(AgentExecution execution, params McpToolCall[] toolCalls)
    {
        var field = typeof(AgentExecution).GetField("_toolCalls", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((List<McpToolCall>)field.GetValue(execution)!).AddRange(toolCalls);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ReturnsNull()
    {
        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsMessagesAndToolCallsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestrationTask?)null);

        var result = await _handler.Handle(new GetExecutionTimelineQuery(Guid.NewGuid()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TaskWithExecutionAndToolCall_ReturnsSpansWithParentChainIntact()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "Test", "Do something");
        var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        execution.Start();
        execution.Complete("result", 100, 50, 0.001m);

        var toolCall = McpToolCall.Create(execution.Id, "perplexity_search", "{}", execution.SpanId);
        toolCall.RecordSuccess("output", 500);
        SeedToolCalls(execution, toolCall);
        SeedExecutions(task, execution);

        _taskRepositoryMock
            .Setup(r => r.GetByIdWithExecutionsMessagesAndToolCallsAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);

        var result = await _handler.Handle(new GetExecutionTimelineQuery(task.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result!.TraceId.Should().Be(task.TraceId);
        result.Spans.Should().HaveCount(2);

        var executionSpan = result.Spans.Single(s => s.SpanType == "AgentExecution");
        executionSpan.SpanId.Should().Be(execution.SpanId);
        executionSpan.Label.Should().Be("Research");
        executionSpan.ParentSpanId.Should().BeNull();

        var toolSpan = result.Spans.Single(s => s.SpanType == "ToolCall");
        toolSpan.ParentSpanId.Should().Be(execution.SpanId);
        toolSpan.Label.Should().Be("perplexity_search");
        toolSpan.Status.Should().Be("Completed");
    }
}
