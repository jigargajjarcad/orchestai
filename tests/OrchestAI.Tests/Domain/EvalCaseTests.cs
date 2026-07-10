using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class EvalCaseTests
{
    [Fact]
    public void Create_ValidInputs_SetsAllFields()
    {
        var suiteId = Guid.NewGuid();

        var evalCase = EvalCase.Create(
            suiteId, "{\"prompt\":\"hi\"}", "{\"mode\":\"ExactMatch\",\"expected\":\"hi\"}",
            EvalScorerType.RuleBased, 0.05m, "smoke,regression");

        evalCase.SuiteId.Should().Be(suiteId);
        evalCase.InputPayload.Should().Be("{\"prompt\":\"hi\"}");
        evalCase.ScorerType.Should().Be(EvalScorerType.RuleBased);
        evalCase.RegressionThreshold.Should().Be(0.05m);
        evalCase.Tags.Should().Be("smoke,regression");
        evalCase.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void CreateEphemeral_BuildsTransientLlmJudgeCaseWithRubricCriteria()
    {
        var evalCase = EvalCase.CreateEphemeral("was the tool call appropriate?", passThreshold: 0.75m);

        evalCase.ScorerType.Should().Be(EvalScorerType.LlmJudge);
        evalCase.SuiteId.Should().Be(Guid.Empty);

        using var doc = System.Text.Json.JsonDocument.Parse(evalCase.ExpectedCriteria);
        doc.RootElement.GetProperty("rubric").GetString().Should().Be("was the tool call appropriate?");
        doc.RootElement.GetProperty("passThreshold").GetDecimal().Should().Be(0.75m);
    }

    [Fact]
    public void CreateEphemeral_NoPassThreshold_OmitsPassThresholdFromCriteria()
    {
        var evalCase = EvalCase.CreateEphemeral("rubric text", passThreshold: null);

        using var doc = System.Text.Json.JsonDocument.Parse(evalCase.ExpectedCriteria);
        doc.RootElement.TryGetProperty("passThreshold", out _).Should().BeFalse();
    }
}
