using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Observability;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class CostRollupBackgroundServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task RunOnceAsync_NoPriorRollups_AggregatesLastThirtyDaysAndUpserts()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tenantId = Guid.NewGuid();
        var aggregate = new CostLedgerAggregate(
            today, tenantId, UserId, AgentType.Research, "anthropic/claude-haiku-4-5-20251001",
            InputTokens: 100, OutputTokens: 50, CostUsd: 0.001m, ExecutionCount: 2);

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock
            .Setup(r => r.GetDailyAggregatesForRollupAsync(
                It.Is<DateOnly>(d => d == today.AddDays(-30)), today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([aggregate]);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock
            .Setup(r => r.GetLastRolledUpDateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);
        rollupRepoMock
            .Setup(r => r.UpsertAsync(It.IsAny<CostRollup>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var accessor = new AsyncLocalCurrentTenantAccessor();
        var service = BuildService(ledgerRepoMock.Object, rollupRepoMock.Object, accessor);

        await service.RunOnceAsync(CancellationToken.None);

        rollupRepoMock.Verify(
            r => r.UpsertAsync(
                It.Is<CostRollup>(c =>
                    c.Date == today && c.TenantId == tenantId && c.UserId == UserId && c.AgentType == AgentType.Research
                    && c.InputTokens == 100 && c.OutputTokens == 50 && c.ExecutionCount == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
        accessor.IsSystemWriteScope.Should().BeFalse(
            "the system-write scope must be scoped to the duration of RunOnceAsync only, restored after it returns");
    }

    [Fact]
    public async Task RunOnceAsync_PriorRollupExists_QueriesTrailingLookbackWindowOnly()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastRolledUp = today.AddDays(-10);

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock
            .Setup(r => r.GetDailyAggregatesForRollupAsync(
                It.Is<DateOnly>(d => d == lastRolledUp.AddDays(-2)), today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock
            .Setup(r => r.GetLastRolledUpDateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastRolledUp);

        var accessor = new AsyncLocalCurrentTenantAccessor();
        var service = BuildService(ledgerRepoMock.Object, rollupRepoMock.Object, accessor);

        await service.RunOnceAsync(CancellationToken.None);

        ledgerRepoMock.Verify(
            r => r.GetDailyAggregatesForRollupAsync(lastRolledUp.AddDays(-2), today, It.IsAny<CancellationToken>()),
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
            .Setup(r => r.GetDailyAggregatesForRollupAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock
            .Setup(r => r.GetLastRolledUpDateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var accessor = new AsyncLocalCurrentTenantAccessor();
        var service = BuildService(ledgerRepoMock.Object, rollupRepoMock.Object, accessor);

        var act = async () => await service.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunOnceAsync_WhileFetchingAggregates_IsSystemWriteScopeIsTrue()
    {
        // Proves RunOnceAsync wraps its entire per-tick operation in the system-write scope —
        // not just the final SaveChanges — since GetDailyAggregatesForRollupAsync's own
        // production guard (CostLedgerRepository) depends on the scope being active for the
        // whole call, not just the write at the end.
        bool? observedDuringFetch = null;
        var accessor = new AsyncLocalCurrentTenantAccessor();

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock
            .Setup(r => r.GetDailyAggregatesForRollupAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                observedDuringFetch = accessor.IsSystemWriteScope;
                return Task.FromResult<IReadOnlyList<CostLedgerAggregate>>(Array.Empty<CostLedgerAggregate>());
            });

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock
            .Setup(r => r.GetLastRolledUpDateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var service = BuildService(ledgerRepoMock.Object, rollupRepoMock.Object, accessor);

        await service.RunOnceAsync(CancellationToken.None);

        observedDuringFetch.Should().BeTrue();
    }

    private static CostRollupBackgroundService BuildService(
        ICostLedgerRepository ledgerRepo, ICostRollupRepository rollupRepo, ICurrentTenantAccessor tenantAccessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ledgerRepo);
        services.AddSingleton(rollupRepo);
        var provider = services.BuildServiceProvider();

        return new CostRollupBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            tenantAccessor,
            NullLogger<CostRollupBackgroundService>.Instance);
    }
}
