using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.SuspendTenant;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class SuspendTenantHandlerTests
{
    [Fact]
    public async Task Handle_ExistingTenant_SuspendsAndPersists()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        repoMock.Setup(r => r.UpdateAsync(tenant, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new SuspendTenantHandler(repoMock.Object);
        var response = await handler.Handle(new SuspendTenantCommand(tenant.Id), CancellationToken.None);

        response.Status.Should().Be(nameof(TenantStatus.Suspended));
        tenant.Status.Should().Be(TenantStatus.Suspended);
        repoMock.Verify(r => r.UpdateAsync(tenant, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownTenant_ThrowsNotFound()
    {
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var handler = new SuspendTenantHandler(repoMock.Object);
        var act = async () => await handler.Handle(new SuspendTenantCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
