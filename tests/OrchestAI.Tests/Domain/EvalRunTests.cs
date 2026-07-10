using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class EvalRunTests
{
    [Fact]
    public void Create_NoBaseline_StartsPendingWithNullBaseline()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", baselineRunId: null);

        run.Status.Should().Be(EvalRunStatus.Pending);
        run.BaselineRunId.Should().BeNull();
        run.SubjectVersion.Should().Be("commit-abc123");
    }

    [Fact]
    public void MarkRunning_ThenMarkCompleted_TransitionsStatusAndSetsCompletedAt()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", null);

        run.MarkRunning();
        run.Status.Should().Be(EvalRunStatus.Running);

        run.MarkCompleted();
        run.Status.Should().Be(EvalRunStatus.Completed);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_SetsStatusAndErrorMessage()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", null);

        run.MarkFailed("suite has no cases");

        run.Status.Should().Be(EvalRunStatus.Failed);
        run.ErrorMessage.Should().Be("suite has no cases");
    }

    [Fact]
    public void Create_DefaultsToLiveSuiteSource()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", null);

        run.Source.Should().Be(EvalRunSource.LiveSuite);
        run.SuiteId.Should().NotBeNull();
    }

    [Fact]
    public void CreatePostHoc_SetsSourceRubricAndNullSuiteId()
    {
        var run = EvalRun.CreatePostHoc(
            "posthoc-20260710120000", "was the tool call appropriate?", "{\"resolvedTraceIds\":[]}");

        run.Source.Should().Be(EvalRunSource.PostHoc);
        run.SuiteId.Should().BeNull();
        run.Rubric.Should().Be("was the tool call appropriate?");
        run.SelectionCriteriaJson.Should().Be("{\"resolvedTraceIds\":[]}");
        run.Status.Should().Be(EvalRunStatus.Pending);
        run.SkippedAlreadyScoredCount.Should().Be(0);
        run.ForceRescore.Should().BeFalse("default post-hoc runs skip already-scored traces rather than superseding them");
    }

    [Fact]
    public void CreatePostHoc_ForceRescoreTrue_SetsFlagExplicitly()
    {
        var run = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{}", forceRescore: true);

        run.ForceRescore.Should().BeTrue();
    }

    [Fact]
    public void IncrementSkippedCount_IncrementsFromZero()
    {
        var run = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{}");

        run.IncrementSkippedCount();
        run.IncrementSkippedCount();

        run.SkippedAlreadyScoredCount.Should().Be(2);
    }
}
