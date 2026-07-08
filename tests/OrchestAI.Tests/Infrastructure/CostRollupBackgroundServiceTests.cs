using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Observability;

namespace OrchestAI.Tests.Infrastructure;

public sealed class CostRollupBackgroundServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task RunOnceAsync_NoPriorRollups_AggregatesLastThirtyDaysAndUpserts()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var aggregate = new CostLedgerAggregate(
            today, UserId, AgentType.Research, "anthropic/claude-haiku-4-5-20251001",
            InputTokens: 100, OutputTokens: 50, CostUsd: 0.001m, ExecutionCount: 2);

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock
            .Setup(r => r.GetDailyAggregatesAsync(
                It.Is<DateOnly>(d => d == today.AddDays(-30)), today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([aggregate]);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock
            .Setup(r => r.GetLastRolledUpDateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);
        rollupRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<CostRollup>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = BuildService(ledgerRepoMock.Object, rollupRepoMock.Object);

        await service.RunOnceAsync(CancellationToken.None);

        rollupRepoMock.Verify(
            r => r.UpsertAsync(
                It.Is<CostRollup>(c =>
                    c.Date == today && c.UserId == UserId && c.AgentType == AgentType.Research
                    && c.InputTokens == 100 && c.OutputTokens == 50 && c.ExecutionCount == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunOnceAsync_PriorRollupExists_QueriesTrailingLookbackWindowOnly()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastRolledUp = today.AddDays(-10);

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock
            .Setup(r => r.GetDailyAggregatesAsync(
                It.Is<DateOnly>(d => d == lastRolledUp.AddDays(-2)), today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock
            .Setup(r => r.GetLastRolledUpDateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastRolledUp);

        var service = BuildService(ledgerRepoMock.Object, rollupRepoMock.Object);

        await service.RunOnceAsync(CancellationToken.None);

        ledgerRepoMock.Verify(
            r => r.GetDailyAggregatesAsync(lastRolledUp.AddDays(-2), today, It.IsAny<CancellationToken>()),
            Times.Once);
        rollupRepoMock.Verify(
            r => r.UpsertAsync(It.IsAny<CostRollup>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunOnceAsync_NoAggregates_DoesNotThrow()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock
            .Setup(r => r.GetDailyAggregatesAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock
            .Setup(r => r.GetLastRolledUpDateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var service = BuildService(ledgerRepoMock.Object, rollupRepoMock.Object);

        var act = async () => await service.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static CostRollupBackgroundService BuildService(
        ICostLedgerRepository ledgerRepo, ICostRollupRepository rollupRepo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ledgerRepo);
        services.AddSingleton(rollupRepo);
        var provider = services.BuildServiceProvider();

        return new CostRollupBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CostRollupBackgroundService>.Instance);
    }
}
