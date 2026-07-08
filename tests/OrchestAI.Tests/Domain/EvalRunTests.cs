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
}
