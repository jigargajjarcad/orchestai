using FluentAssertions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetPostHocScoringSummary;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetPostHocScoringSummaryHandlerTests
{
    [Fact]
    public async Task Handle_RunNotFound_ThrowsNotFound()
    {
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EvalRun?)null);

        var handler = new GetPostHocScoringSummaryHandler(runRepoMock.Object, Mock.Of<IEvalResultRepository>());

        var act = async () => await handler.Handle(new GetPostHocScoringSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_LiveSuiteRun_ThrowsValidation_SummaryOnlyAppliesToPostHocRuns()
    {
        var liveRun = EvalRun.Create(Guid.NewGuid(), "commit-abc", null);
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(liveRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(liveRun);

        var handler = new GetPostHocScoringSummaryHandler(runRepoMock.Object, Mock.Of<IEvalResultRepository>());

        var act = async () => await handler.Handle(new GetPostHocScoringSummaryQuery(liveRun.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_PostHocRunWithMixedResults_ComputesPassRateAndDistribution()
    {
        var run = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{\"resolvedTraceIds\":[]}");
        run.IncrementSkippedCount();
        run.MarkCompleted();

        var results = new List<EvalResult>
        {
            EvalResult.Create(run.Id, null, Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"),
            EvalResult.Create(run.Id, null, Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", 0.3m, false, "{}"),
            EvalResult.Create(run.Id, null, Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", 0.95m, true, "{}"),
        };

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock.Setup(r => r.GetByRunIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(results);

        var handler = new GetPostHocScoringSummaryHandler(runRepoMock.Object, resultRepoMock.Object);

        var response = await handler.Handle(new GetPostHocScoringSummaryQuery(run.Id), CancellationToken.None);

        response.ScoredCount.Should().Be(3);
        response.SkippedAlreadyScoredCount.Should().Be(1);
        response.PassedCount.Should().Be(2);
        response.PassRate.Should().BeApproximately(2m / 3m, 0.0001m);
        response.ScoreDistribution.Sum(b => b.Count).Should().Be(3);
    }
}
