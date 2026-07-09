using FluentAssertions;
using Moq;
using OrchestAI.Application.Queries.GetEvalRuns;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetEvalRunsHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsRunsNewestFirstWithStatusAndBaseline()
    {
        var suiteId = Guid.NewGuid();
        var older = EvalRun.Create(suiteId, "v1", null);
        var newer = EvalRun.Create(suiteId, "v2", older.Id);

        var repoMock = new Mock<IEvalRunRepository>();
        repoMock
            .Setup(r => r.GetBySuiteIdAsync(suiteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([newer, older]);

        var handler = new GetEvalRunsHandler(repoMock.Object);

        var response = await handler.Handle(new GetEvalRunsQuery(suiteId), CancellationToken.None);

        response.Runs.Should().HaveCount(2);
        response.Runs[0].Id.Should().Be(newer.Id);
        response.Runs[0].BaselineRunId.Should().Be(older.Id);
        response.Runs[1].Id.Should().Be(older.Id);
    }
}
