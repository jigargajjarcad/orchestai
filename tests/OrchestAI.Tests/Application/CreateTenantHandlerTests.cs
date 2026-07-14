using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.CreateTenant;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class CreateTenantHandlerTests
{
    [Fact]
    public async Task Handle_ValidNameAndSlug_CreatesTenant()
    {
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetBySlugAsync("acme-corp", It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);
        Tenant? captured = null;
        repoMock.Setup(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Callback<Tenant, CancellationToken>((t, _) => captured = t)
            .Returns(Task.CompletedTask);

        var handler = new CreateTenantHandler(repoMock.Object);
        var response = await handler.Handle(new CreateTenantCommand("Acme Corp", "acme-corp"), CancellationToken.None);

        captured.Should().NotBeNull();
        response.Name.Should().Be("Acme Corp");
        response.Slug.Should().Be("acme-corp");
    }

    [Fact]
    public async Task Handle_DuplicateSlug_ThrowsValidation()
    {
        var existing = Tenant.Create("Existing", "acme-corp");
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetBySlugAsync("acme-corp", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var handler = new CreateTenantHandler(repoMock.Object);
        var act = async () => await handler.Handle(new CreateTenantCommand("Acme Corp", "acme-corp"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsValidation()
    {
        var handler = new CreateTenantHandler(Mock.Of<ITenantRepository>());
        var act = async () => await handler.Handle(new CreateTenantCommand("", "slug"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
