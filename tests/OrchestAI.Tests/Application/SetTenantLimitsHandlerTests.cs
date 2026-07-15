using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.SetTenantLimits;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class SetTenantLimitsHandlerTests
{
    [Fact]
    public async Task Handle_TenantDoesNotExist_ThrowsNotFoundException()
    {
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);
        var limitsRepoMock = new Mock<ITenantLimitsRepository>();
        var handler = new SetTenantLimitsHandler(limitsRepoMock.Object, tenantRepoMock.Object);

        var act = () => handler.Handle(
            new SetTenantLimitsCommand(Guid.NewGuid(), null, null, null, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NoExistingRow_CreatesNewTenantLimits()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var limitsRepoMock = new Mock<ITenantLimitsRepository>();
        limitsRepoMock.Setup(r => r.GetByTenantIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync((TenantLimits?)null);
        TenantLimits? saved = null;
        limitsRepoMock.Setup(r => r.UpsertAsync(It.IsAny<TenantLimits>(), It.IsAny<CancellationToken>()))
            .Callback<TenantLimits, CancellationToken>((l, _) => saved = l)
            .Returns(Task.CompletedTask);
        var handler = new SetTenantLimitsHandler(limitsRepoMock.Object, tenantRepoMock.Object);

        var response = await handler.Handle(
            new SetTenantLimitsCommand(tenant.Id, 200, 10, null, null, 100m, null, null), CancellationToken.None);

        response.RequestsPerMinute.Should().Be(200);
        saved.Should().NotBeNull();
        saved!.TenantId.Should().Be(tenant.Id);
        saved.MaxConcurrentTasks.Should().Be(10);
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesInPlace()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var existing = TenantLimits.Create(tenant.Id, 50, null, null, null, null, null, null);
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var limitsRepoMock = new Mock<ITenantLimitsRepository>();
        limitsRepoMock.Setup(r => r.GetByTenantIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var handler = new SetTenantLimitsHandler(limitsRepoMock.Object, tenantRepoMock.Object);

        var response = await handler.Handle(
            new SetTenantLimitsCommand(tenant.Id, 300, null, null, null, null, null, null), CancellationToken.None);

        response.RequestsPerMinute.Should().Be(300);
        limitsRepoMock.Verify(r => r.UpsertAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }
}
