using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class EvalResultTests
{
    [Fact]
    public void Create_NullEvalCaseIdWithRubric_PersistsPostHocShape()
    {
        var runId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var result = EvalResult.Create(
            runId, evalCaseId: null, executionId, EvalScorerType.LlmJudge, "llm-judge-v1",
            score: 0.9m, passed: true, scorerOutput: "{}", rubric: "was the tool call appropriate?");

        result.EvalCaseId.Should().BeNull();
        result.Rubric.Should().Be("was the tool call appropriate?");
        result.AgentExecutionId.Should().Be(executionId);
    }

    [Fact]
    public void Create_LiveCaseResult_RubricDefaultsToNull()
    {
        var result = EvalResult.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvalScorerType.RuleBased, "rule-based-v1",
            score: 1.0m, passed: true, scorerOutput: "{}");

        result.Rubric.Should().BeNull();
        result.EvalCaseId.Should().NotBeNull();
    }
}
