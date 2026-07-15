using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.AdmitOrchestrationTask;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class AdmitOrchestrationTaskHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private AdmitOrchestrationTaskHandler CreateHandler(OrchestrationTask task, AdmissionResult admissionResult)
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var admissionRepoMock = new Mock<IOrchestrationAdmissionRepository>();
        admissionRepoMock.Setup(r => r.TryAdmitAsync(
            task.Id, _tenantId, It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(admissionResult);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 4, 20, 50m, 500m, 100));

        var estimatorMock = new Mock<IBudgetEstimator>();
        estimatorMock.Setup(e => e.EstimateWorstCaseCostAsync(It.IsAny<ResolvedTenantLimits>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4m);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock.Setup(r => r.GetByDateRangeAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostRollup>());

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock.Setup(r => r.GetDailyAggregatesAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostLedgerAggregate>());

        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(_tenantId);

        return new AdmitOrchestrationTaskHandler(
            taskRepoMock.Object, admissionRepoMock.Object, limitsProviderMock.Object, estimatorMock.Object,
            rollupRepoMock.Object, ledgerRepoMock.Object, accessorMock.Object,
            Options.Create(new AbuseProtectionOptions()), NullLogger<AdmitOrchestrationTaskHandler>.Instance);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ThrowsNotFoundException()
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrchestrationTask?)null);
        var handler = new AdmitOrchestrationTaskHandler(
            taskRepoMock.Object, Mock.Of<IOrchestrationAdmissionRepository>(), Mock.Of<ITenantLimitsProvider>(),
            Mock.Of<IBudgetEstimator>(), Mock.Of<ICostRollupRepository>(), Mock.Of<ICostLedgerRepository>(),
            Mock.Of<ICurrentTenantAccessor>(), Options.Create(new AbuseProtectionOptions()),
            NullLogger<AdmitOrchestrationTaskHandler>.Instance);

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_Admitted_ReturnsResponse()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(task, new AdmissionResult(true, null, null));

        var response = await handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        response.TaskId.Should().Be(task.Id);
    }

    [Fact]
    public async Task Handle_TaskNotPending_ThrowsConflictException()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(task, new AdmissionResult(false, AdmissionFailureReason.TaskNotPending, null));

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_ConcurrencyExceeded_ThrowsTenantLimitExceededExceptionWithCorrectReason()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(
            task, new AdmissionResult(false, AdmissionFailureReason.ConcurrencyExceeded, """{"limit":5,"actual":5}"""));

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TenantLimitExceededException>();
        exception.Which.Reason.Should().Be(RejectionReason.ConcurrencyExceeded);
        exception.Which.TenantId.Should().Be(_tenantId);
        exception.Which.TraceId.Should().Be(task.TraceId);
    }

    [Fact]
    public async Task Handle_BudgetExceeded_ThrowsTenantLimitExceededExceptionWithCorrectReason()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(
            task, new AdmissionResult(false, AdmissionFailureReason.BudgetExceeded, """{"limit":50,"actual":52}"""));

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TenantLimitExceededException>();
        exception.Which.Reason.Should().Be(RejectionReason.BudgetExceeded);
    }
}
