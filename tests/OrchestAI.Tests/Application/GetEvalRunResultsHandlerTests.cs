using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetEvalRunResults;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetEvalRunResultsHandlerTests
{
    [Fact]
    public async Task Handle_CalledTwice_ReturnsStoredScoreVerbatimBothTimes_NeverRecomputes()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "v1", null);
        var evalCaseId = Guid.NewGuid();
        var persisted = EvalResult.Create(
            run.Id, evalCaseId, Guid.NewGuid(), EvalScorerType.RuleBased, "rule-based-v1",
            score: 0.42m, passed: false, scorerOutput: "{\"note\":\"stale by design\"}");

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock
            .Setup(r => r.GetByRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([persisted]);

        var handler = new GetEvalRunResultsHandler(
            runRepoMock.Object, resultRepoMock.Object, NullLogger<GetEvalRunResultsHandler>.Instance);

        var first = await handler.Handle(new GetEvalRunResultsQuery(run.Id), CancellationToken.None);
        var second = await handler.Handle(new GetEvalRunResultsQuery(run.Id), CancellationToken.None);

        first.Results.Single().Score.Should().Be(0.42m);
        second.Results.Single().Score.Should().Be(0.42m);
        resultRepoMock.Verify(r => r.GetByRunIdAsync(run.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_RunDoesNotExist_ThrowsNotFoundException()
    {
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EvalRun?)null);

        var handler = new GetEvalRunResultsHandler(
            runRepoMock.Object, Mock.Of<IEvalResultRepository>(), NullLogger<GetEvalRunResultsHandler>.Instance);

        var act = async () => await handler.Handle(new GetEvalRunResultsQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
