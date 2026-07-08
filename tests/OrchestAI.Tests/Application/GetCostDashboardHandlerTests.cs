using FluentAssertions;
using Moq;
using OrchestAI.Application.Queries.GetCostDashboard;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class GetCostDashboardHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ICostRollupRepository> _rollupRepositoryMock;
    private readonly Mock<ICostLedgerRepository> _ledgerRepositoryMock;
    private readonly GetCostDashboardHandler _handler;

    public GetCostDashboardHandlerTests()
    {
        _rollupRepositoryMock = new Mock<ICostRollupRepository>();
        _ledgerRepositoryMock = new Mock<ICostLedgerRepository>();
        _handler = new GetCostDashboardHandler(_rollupRepositoryMock.Object, _ledgerRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_RangeEntirelyInThePast_ReadsOnlyFromRollups()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-10);
        var to = today.AddDays(-5);

        var rollup = CostRollup.Create(from, UserId, AgentType.Research, "anthropic/claude-haiku-4-5-20251001", 100, 50, 0.001m, 1);
        _rollupRepositoryMock
            .Setup(r => r.GetByDateRangeAsync(from, to, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([rollup]);

        var result = await _handler.Handle(new GetCostDashboardQuery(UserId, from, to), CancellationToken.None);

        result.Breakdown.Should().ContainSingle();
        result.Breakdown[0].IsLive.Should().BeFalse();
        result.TotalCostUsd.Should().Be(0.001m);
        _ledgerRepositoryMock.Verify(
            r => r.GetDailyAggregatesAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RangeIncludesToday_MergesRollupsWithLiveTodayData()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-5);

        var rollup = CostRollup.Create(from, UserId, AgentType.Research, "anthropic/claude-haiku-4-5-20251001", 100, 50, 0.001m, 1);
        _rollupRepositoryMock
            .Setup(r => r.GetByDateRangeAsync(from, today.AddDays(-1), UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([rollup]);

        var otherUser = Guid.NewGuid();
        var liveForUser = new CostLedgerAggregate(today, UserId, AgentType.Writer, "anthropic/claude-haiku-4-5-20251001", 30, 20, 0.0005m, 1);
        var liveForOtherUser = new CostLedgerAggregate(today, otherUser, AgentType.Writer, "anthropic/claude-haiku-4-5-20251001", 999, 999, 9.99m, 1);
        _ledgerRepositoryMock
            .Setup(r => r.GetDailyAggregatesAsync(today, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([liveForUser, liveForOtherUser]);

        var result = await _handler.Handle(new GetCostDashboardQuery(UserId, from, today), CancellationToken.None);

        result.Breakdown.Should().HaveCount(2);
        result.Breakdown.Should().ContainSingle(b => b.IsLive && b.AgentType == "Writer");
        result.Breakdown.Should().NotContain(b => b.CostUsd == 9.99m);
        result.TotalCostUsd.Should().Be(0.0015m);
    }
}
