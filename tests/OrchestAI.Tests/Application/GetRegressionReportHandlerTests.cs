using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetRegressionReport;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetRegressionReportHandlerTests
{
    private static (IEvalRunRepository RunRepo, IEvalSuiteRepository SuiteRepo, IEvalResultRepository ResultRepo)
        BuildRepos(
            EvalRun currentRun, EvalRun? baselineRun, EvalSuite suite,
            IReadOnlyList<EvalResult> currentResults, IReadOnlyList<EvalResult> baselineResults)
    {
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(currentRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(currentRun);
        if (baselineRun is not null)
            runRepoMock.Setup(r => r.GetByIdAsync(baselineRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(baselineRun);

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdWithCasesAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock.Setup(r => r.GetByRunIdAsync(currentRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(currentResults);
        if (baselineRun is not null)
            resultRepoMock.Setup(r => r.GetByRunIdAsync(baselineRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(baselineResults);

        return (runRepoMock.Object, suiteRepoMock.Object, resultRepoMock.Object);
    }

    private static EvalSuite BuildSuiteWithCase(out EvalCase evalCase, decimal regressionThreshold)
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        evalCase = EvalCase.Create(suite.Id, "{}", "{}", EvalScorerType.RuleBased, regressionThreshold);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { evalCase });
        return suite;
    }

    [Fact]
    public async Task Handle_ScoreDropExceedsThreshold_FlagsCaseAsRegressed()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.05m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var baselineResult = EvalResult.Create(
            baselineRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.90m, true, "{}");
        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.70m, false, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], [baselineResult]);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        var diff = response.CaseDiffs.Single();
        diff.ScoreDelta.Should().Be(0.20m);
        diff.Regressed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ScoreDropWithinThreshold_DoesNotFlagRegression()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.30m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var baselineResult = EvalResult.Create(
            baselineRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.90m, true, "{}");
        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.70m, false, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], [baselineResult]);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        response.CaseDiffs.Single().Regressed.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ScoreDropExactlyEqualsThreshold_DoesNotFlagRegression()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.20m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var baselineResult = EvalResult.Create(
            baselineRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.90m, true, "{}");
        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.70m, false, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], [baselineResult]);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        var diff = response.CaseDiffs.Single();
        diff.ScoreDelta.Should().Be(0.20m);
        diff.Regressed.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CaseHasNoBaselineResult_ReportedAsNewCaseNotRegressed()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.05m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.50m, true, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], []);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        var diff = response.CaseDiffs.Single();
        diff.IsNewCase.Should().BeTrue();
        diff.Regressed.Should().BeFalse();
        diff.ScoreDelta.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BaselineRunIdIsNull_ThrowsValidationExceptionInsteadOfEmptyDiff()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRunId: null);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(currentRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(currentRun);

        var handler = new GetRegressionReportHandler(
            runRepoMock.Object, Mock.Of<IEvalSuiteRepository>(), Mock.Of<IEvalResultRepository>(),
            NullLogger<GetRegressionReportHandler>.Instance);

        var act = async () => await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_ComputesSuiteLevelPassRateDelta()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var caseA = EvalCase.Create(suite.Id, "{}", "{}", EvalScorerType.RuleBased, 0.05m);
        var caseB = EvalCase.Create(suite.Id, "{}", "{}", EvalScorerType.RuleBased, 0.05m);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { caseA, caseB });

        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var baselineResults = new List<EvalResult>
        {
            EvalResult.Create(baselineRun.Id, caseA.Id, null, EvalScorerType.RuleBased, "v1", 1.0m, true, "{}"),
            EvalResult.Create(baselineRun.Id, caseB.Id, null, EvalScorerType.RuleBased, "v1", 1.0m, true, "{}")
        };
        var currentResults = new List<EvalResult>
        {
            EvalResult.Create(currentRun.Id, caseA.Id, null, EvalScorerType.RuleBased, "v1", 1.0m, true, "{}"),
            EvalResult.Create(currentRun.Id, caseB.Id, null, EvalScorerType.RuleBased, "v1", 0.0m, false, "{}")
        };

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(currentRun, baselineRun, suite, currentResults, baselineResults);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        response.BaselinePassRate.Should().Be(1.0m);
        response.CurrentPassRate.Should().Be(0.5m);
        response.PassRateDelta.Should().Be(-0.5m);
    }

    [Fact]
    public async Task Handle_BaselineHasNoResults_ReturnsZeroPassRateWithoutDividingByZero()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.05m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.85m, true, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], []);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        response.BaselinePassRate.Should().Be(0m);
        response.CurrentPassRate.Should().Be(1.0m);
        response.PassRateDelta.Should().Be(1.0m);
    }

    [Fact]
    public async Task Handle_PostHocRun_ThrowsValidation_RegressionOnlyAppliesToLiveSuiteRuns()
    {
        var postHocRun = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{}");

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(postHocRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(postHocRun);

        var handler = new GetRegressionReportHandler(
            runRepoMock.Object, Mock.Of<IEvalSuiteRepository>(), Mock.Of<IEvalResultRepository>(),
            NullLogger<GetRegressionReportHandler>.Instance);

        var act = async () => await handler.Handle(new GetRegressionReportQuery(postHocRun.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
