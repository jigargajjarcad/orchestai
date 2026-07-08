using FluentAssertions;
using Moq;
using OrchestAI.Application.Queries.GetErrorRateMonitoring;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class GetErrorRateMonitoringHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
    private static readonly DateOnly To = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly Mock<IAgentExecutionRepository> _executionRepositoryMock;
    private readonly Mock<IMcpToolCallRepository> _toolCallRepositoryMock;
    private readonly Mock<IAgentRetryAttemptRepository> _retryAttemptRepositoryMock;
    private readonly GetErrorRateMonitoringHandler _handler;

    public GetErrorRateMonitoringHandlerTests()
    {
        _executionRepositoryMock = new Mock<IAgentExecutionRepository>();
        _toolCallRepositoryMock = new Mock<IMcpToolCallRepository>();
        _retryAttemptRepositoryMock = new Mock<IAgentRetryAttemptRepository>();
        _handler = new GetErrorRateMonitoringHandler(
            _executionRepositoryMock.Object, _toolCallRepositoryMock.Object, _retryAttemptRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_MixedExecutionsAndToolCalls_ComputesRatesAndCategoriesCorrectly()
    {
        var successExecId = Guid.NewGuid();
        var failedExecId = Guid.NewGuid();

        var executionStats = new List<AgentExecutionErrorStat>
        {
            new(successExecId, AgentType.Research, ExecutionStatus.Completed, null, DateTimeOffset.UtcNow),
            new(failedExecId, AgentType.Research, ExecutionStatus.Failed, ExecutionErrorCategory.ProviderError, DateTimeOffset.UtcNow),
        };
        _executionRepositoryMock
            .Setup(r => r.GetErrorStatsAsync(UserId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionStats);

        _retryAttemptRepositoryMock
            .Setup(r => r.GetByAgentExecutionIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([AgentRetryAttempt.Create(failedExecId, 1, 500, "rate limited")]);

        var toolStats = new List<McpToolCallErrorStat>
        {
            new("firecrawl_scrape", false, ExecutionErrorCategory.McpToolError, DateTimeOffset.UtcNow),
            new("firecrawl_scrape", true, null, DateTimeOffset.UtcNow),
        };
        _toolCallRepositoryMock
            .Setup(r => r.GetErrorStatsAsync(UserId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolStats);

        var result = await _handler.Handle(new GetErrorRateMonitoringQuery(UserId, From, To), CancellationToken.None);

        var agentRate = result.AgentErrorRates.Should().ContainSingle().Subject;
        agentRate.AgentType.Should().Be("Research");
        agentRate.TotalExecutions.Should().Be(2);
        agentRate.FailedExecutions.Should().Be(1);
        agentRate.FailureRate.Should().Be(0.5);
        agentRate.RetryCount.Should().Be(1);
        agentRate.FailuresByCategory.Should().ContainKey("ProviderError").WhoseValue.Should().Be(1);

        var toolRate = result.ToolErrorRates.Should().ContainSingle().Subject;
        toolRate.ToolName.Should().Be("firecrawl_scrape");
        toolRate.TotalCalls.Should().Be(2);
        toolRate.FailedCalls.Should().Be(1);
        toolRate.FailureRate.Should().Be(0.5);
        toolRate.FailuresByCategory.Should().ContainKey("McpToolError").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoData_ReturnsEmptyLists()
    {
        _executionRepositoryMock
            .Setup(r => r.GetErrorStatsAsync(UserId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _toolCallRepositoryMock
            .Setup(r => r.GetErrorStatsAsync(UserId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _retryAttemptRepositoryMock
            .Setup(r => r.GetByAgentExecutionIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _handler.Handle(new GetErrorRateMonitoringQuery(UserId, From, To), CancellationToken.None);

        result.AgentErrorRates.Should().BeEmpty();
        result.ToolErrorRates.Should().BeEmpty();
    }
}
