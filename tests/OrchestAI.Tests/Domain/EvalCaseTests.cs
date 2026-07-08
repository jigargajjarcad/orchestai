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
}
