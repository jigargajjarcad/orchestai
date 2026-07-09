using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalScorerFactoryTests
{
    private sealed class FakeScorer(EvalScorerType type) : IEvalScorer
    {
        public EvalScorerType ScorerType => type;
        public Task<EvalScoreResult> ScoreAsync(
            EvalCase evalCase, string actualOutput, EvalScoringContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EvalScoreResult(1.0m, true, "fake", "{}"));
    }

    [Theory]
    [InlineData(EvalScorerType.RuleBased)]
    [InlineData(EvalScorerType.LlmJudge)]
    public void Resolve_KnownScorerType_ReturnsMatchingScorer(EvalScorerType type)
    {
        var factory = new EvalScorerFactory([new FakeScorer(EvalScorerType.RuleBased), new FakeScorer(EvalScorerType.LlmJudge)]);

        var scorer = factory.Resolve(type);

        scorer.ScorerType.Should().Be(type);
    }

    [Fact]
    public void Resolve_UnknownScorerType_Throws()
    {
        var factory = new EvalScorerFactory([new FakeScorer(EvalScorerType.RuleBased)]);

        var act = () => factory.Resolve(EvalScorerType.LlmJudge);

        act.Should().Throw<InvalidOperationException>();
    }
}
