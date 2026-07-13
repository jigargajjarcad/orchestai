using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.CreateApiKey;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class CreateApiKeyHandlerTests
{
    [Fact]
    public async Task Handle_ExistingTenant_ReturnsRawKeyOnceAndPersistsOnlyTheHash()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var hasherMock = new Mock<IApiKeyHasher>();
        hasherMock.Setup(h => h.GenerateNew())
            .Returns(new GeneratedApiKey("orch_live_pk123.secretvalue", "pk123", "hashed-secretvalue"));

        ApiKey? captured = null;
        var apiKeyRepoMock = new Mock<IApiKeyRepository>();
        apiKeyRepoMock.Setup(r => r.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<ApiKey, CancellationToken>((k, _) => captured = k)
            .Returns(Task.CompletedTask);

        var handler = new CreateApiKeyHandler(tenantRepoMock.Object, apiKeyRepoMock.Object, hasherMock.Object);
        var response = await handler.Handle(new CreateApiKeyCommand(tenant.Id, "prod"), CancellationToken.None);

        response.RawKey.Should().Be("orch_live_pk123.secretvalue");
        captured.Should().NotBeNull();
        captured!.HashedSecret.Should().Be("hashed-secretvalue");
        captured.PublicKeyId.Should().Be("pk123");
        captured.DisplayName.Should().Be("prod");
    }

    [Fact]
    public async Task Handle_UnknownTenant_ThrowsNotFound()
    {
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var handler = new CreateApiKeyHandler(tenantRepoMock.Object, Mock.Of<IApiKeyRepository>(), Mock.Of<IApiKeyHasher>());
        var act = async () => await handler.Handle(new CreateApiKeyCommand(Guid.NewGuid(), null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_DefaultTenant_ThrowsValidation_NeverCreatesKey()
    {
        var apiKeyRepoMock = new Mock<IApiKeyRepository>();
        var handler = new CreateApiKeyHandler(
            Mock.Of<ITenantRepository>(), apiKeyRepoMock.Object, Mock.Of<IApiKeyHasher>());

        var act = async () => await handler.Handle(
            new CreateApiKeyCommand(Tenant.DefaultTenantId, "should-never-exist"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        apiKeyRepoMock.Verify(r => r.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
