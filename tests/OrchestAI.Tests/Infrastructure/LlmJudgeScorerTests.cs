using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Application.Configuration;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class LlmJudgeScorerTests
{
    private static EvalCase BuildCase(string criteriaJson = """{"rubric":"Does it greet the user?"}""") =>
        EvalCase.Create(Guid.NewGuid(), "{}", criteriaJson, EvalScorerType.LlmJudge, regressionThreshold: 0.1m);

    private static (LlmJudgeScorer Scorer, Mock<ILlmProvider> Provider, Mock<ICostLedgerRepository> CostRepo)
        Build(string judgeResponseJson)
    {
        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", judgeResponseJson, [], 200, 40));

        var factoryMock = new Mock<ILlmProviderFactory>();
        factoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock
            .Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));

        var costRepoMock = new Mock<ICostLedgerRepository>();
        costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001",
            DefaultJudgePassThreshold = 0.7m
        });

        var scorer = new LlmJudgeScorer(factoryMock.Object, pricingCacheMock.Object, costRepoMock.Object, options);
        return (scorer, providerMock, costRepoMock);
    }

    [Fact]
    public async Task ScoreAsync_JudgeReturnsScoreAboveThreshold_Passes()
    {
        var (scorer, _, _) = Build("""{"score":0.9,"reasoning":"Greets the user warmly."}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await scorer.ScoreAsync(BuildCase(), "Hello there!", context, CancellationToken.None);

        result.Score.Should().Be(0.9m);
        result.Passed.Should().BeTrue();
        result.ScorerVersion.Should().Be(LlmJudgeScorer.Version);
    }

    [Fact]
    public async Task ScoreAsync_JudgeReturnsScoreBelowThreshold_Fails()
    {
        var (scorer, _, _) = Build("""{"score":0.3,"reasoning":"No greeting present."}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await scorer.ScoreAsync(BuildCase(), "The weather is nice.", context, CancellationToken.None);

        result.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task ScoreAsync_UsesCaseSpecificPassThresholdWhenPresent()
    {
        var (scorer, _, _) = Build("""{"score":0.5,"reasoning":"Partially meets rubric."}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());
        var evalCase = BuildCase("""{"rubric":"x","passThreshold":0.4}""");

        var result = await scorer.ScoreAsync(evalCase, "some output", context, CancellationToken.None);

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task ScoreAsync_AlwaysSendsTemperatureZero()
    {
        var (scorer, providerMock, _) = Build("""{"score":0.8,"reasoning":"ok"}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());

        await scorer.ScoreAsync(BuildCase(), "output", context, CancellationToken.None);

        providerMock.Verify(
            p => p.SendAsync(It.Is<AgentConversation>(c => c.Temperature == 0.0), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScoreAsync_WritesCostLedgerRowTaggedEvalAndLinkedToRun()
    {
        var (scorer, _, costRepoMock) = Build("""{"score":0.8,"reasoning":"ok"}""");
        var orchestrationTaskId = Guid.NewGuid();
        var evalRunId = Guid.NewGuid();
        var context = new EvalScoringContext(orchestrationTaskId, evalRunId);

        CostLedger? captured = null;
        costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((l, _) => captured = l)
            .Returns(Task.CompletedTask);

        await scorer.ScoreAsync(BuildCase(), "output", context, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Source.Should().Be(CostSource.Eval);
        captured.EvalRunId.Should().Be(evalRunId);
        captured.OrchestrationTaskId.Should().Be(orchestrationTaskId);
        captured.AgentExecutionId.Should().BeNull();
    }

    [Fact]
    public async Task ScoreAsync_JudgeReturnsMalformedResponse_FailsGracefullyButStillRecordsCost()
    {
        var (scorer, _, costRepoMock) = Build("This is not JSON at all.");
        var orchestrationTaskId = Guid.NewGuid();
        var evalRunId = Guid.NewGuid();
        var context = new EvalScoringContext(orchestrationTaskId, evalRunId);

        CostLedger? captured = null;
        costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((l, _) => captured = l)
            .Returns(Task.CompletedTask);

        var result = await scorer.ScoreAsync(BuildCase(), "output", context, CancellationToken.None);

        result.Passed.Should().BeFalse();

        costRepoMock.Verify(
            r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()),
            Times.Once);
        captured.Should().NotBeNull();
        captured!.Source.Should().Be(CostSource.Eval);
        captured.EvalRunId.Should().Be(evalRunId);
    }
}
