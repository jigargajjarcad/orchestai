using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class RuleBasedScorerTests
{
    private static readonly EvalScoringContext Context = new(Guid.NewGuid(), Guid.NewGuid());

    private static EvalCase BuildCase(string expectedCriteria) => EvalCase.Create(
        Guid.NewGuid(), "{}", expectedCriteria, EvalScorerType.RuleBased, regressionThreshold: 0.1m);

    [Theory]
    [InlineData("hello world", "hello world", true)]
    [InlineData("hello world", "goodbye", false)]
    public async Task ScoreAsync_ExactMatch_ComparesActualOutputVerbatim(
        string actual, string expected, bool shouldPass)
    {
        var scorer = new RuleBasedScorer();
        var criteria = JsonSerializer.Serialize(new { mode = "ExactMatch", expected });
        var evalCase = BuildCase(criteria);

        var result = await scorer.ScoreAsync(evalCase, actual, Context, CancellationToken.None);

        result.Passed.Should().Be(shouldPass);
        result.Score.Should().Be(shouldPass ? 1.0m : 0.0m);
    }

    [Theory]
    [InlineData("order-42891", @"^order-\d+$", true)]
    [InlineData("not-an-order", @"^order-\d+$", false)]
    public async Task ScoreAsync_Regex_MatchesPattern(string actual, string pattern, bool shouldPass)
    {
        var scorer = new RuleBasedScorer();
        var criteria = JsonSerializer.Serialize(new { mode = "Regex", pattern });
        var evalCase = BuildCase(criteria);

        var result = await scorer.ScoreAsync(evalCase, actual, Context, CancellationToken.None);

        result.Passed.Should().Be(shouldPass);
    }

    [Fact]
    public async Task ScoreAsync_JsonSchema_RequiredPropertyMissing_Fails()
    {
        var scorer = new RuleBasedScorer();
        var schema = new
        {
            type = "object",
            properties = new { name = new { type = "string" } },
            required = new[] { "name" }
        };
        var criteria = JsonSerializer.Serialize(new { mode = "JsonSchema", schema });
        var evalCase = BuildCase(criteria);

        var result = await scorer.ScoreAsync(evalCase, """{"age":30}""", Context, CancellationToken.None);

        result.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task ScoreAsync_JsonSchema_RequiredPropertyPresentWithCorrectType_Passes()
    {
        var scorer = new RuleBasedScorer();
        var schema = new
        {
            type = "object",
            properties = new { name = new { type = "string" } },
            required = new[] { "name" }
        };
        var criteria = JsonSerializer.Serialize(new { mode = "JsonSchema", schema });
        var evalCase = BuildCase(criteria);

        var result = await scorer.ScoreAsync(evalCase, """{"name":"Ada"}""", Context, CancellationToken.None);

        result.Passed.Should().BeTrue();
        result.Score.Should().Be(1.0m);
    }

    [Fact]
    public async Task ScoreAsync_AlwaysStampsScorerVersion()
    {
        var scorer = new RuleBasedScorer();
        var criteria = JsonSerializer.Serialize(new { mode = "ExactMatch", expected = "x" });
        var evalCase = BuildCase(criteria);

        var result = await scorer.ScoreAsync(evalCase, "x", Context, CancellationToken.None);

        result.ScorerVersion.Should().Be(RuleBasedScorer.Version);
    }
}
