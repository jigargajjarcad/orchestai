using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.RevokeApiKey;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class RevokeApiKeyHandlerTests
{
    [Fact]
    public async Task Handle_ExistingKey_RevokesAndPersists()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk123", "hashed");
        var repoMock = new Mock<IApiKeyRepository>();
        repoMock.Setup(r => r.GetByIdAsync(key.Id, It.IsAny<CancellationToken>())).ReturnsAsync(key);
        repoMock.Setup(r => r.UpdateAsync(key, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RevokeApiKeyHandler(repoMock.Object);
        var response = await handler.Handle(new RevokeApiKeyCommand(key.Id), CancellationToken.None);

        response.Revoked.Should().BeTrue();
        key.IsUsable().Should().BeFalse();
        repoMock.Verify(r => r.UpdateAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownKey_ThrowsNotFound()
    {
        var repoMock = new Mock<IApiKeyRepository>();
        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ApiKey?)null);

        var handler = new RevokeApiKeyHandler(repoMock.Object);
        var act = async () => await handler.Handle(new RevokeApiKeyCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
