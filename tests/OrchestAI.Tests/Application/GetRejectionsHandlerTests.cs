using FluentAssertions;
using Moq;
using OrchestAI.Application.Queries.GetRejections;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetRejectionsHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsRepositoryResultsMappedToDto()
    {
        var rejection = RejectionEvent.Create(RejectionReason.RateLimited, "req-1", null, null, "{}");
        var repoMock = new Mock<IRejectionEventRepository>();
        repoMock.Setup(r => r.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RejectionEvent> { rejection });
        var handler = new GetRejectionsHandler(repoMock.Object);

        var response = await handler.Handle(new GetRejectionsQuery(50), CancellationToken.None);

        response.Rejections.Should().ContainSingle();
        response.Rejections[0].Reason.Should().Be("RateLimited");
        response.Rejections[0].RequestId.Should().Be("req-1");
    }
}
