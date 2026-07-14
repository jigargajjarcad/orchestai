using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class EvalRun : ITenantScoped
{
    private EvalRun() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? SuiteId { get; private set; }
    public EvalRunSource Source { get; private set; }
    public DateTimeOffset TriggeredAt { get; private set; }
    public EvalRunStatus Status { get; private set; }
    public Guid? BaselineRunId { get; private set; }
    public string SubjectVersion { get; private set; } = string.Empty;
    public string? Rubric { get; private set; }
    public string? SelectionCriteriaJson { get; private set; }
    public int SkippedAlreadyScoredCount { get; private set; }
    public bool ForceRescore { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public EvalSuite? Suite { get; private set; }
    public EvalRun? BaselineRun { get; private set; }

    public static EvalRun Create(Guid suiteId, string subjectVersion, Guid? baselineRunId)
    {
        return new EvalRun
        {
            Id = Guid.NewGuid(),
            SuiteId = suiteId,
            Source = EvalRunSource.LiveSuite,
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = EvalRunStatus.Pending,
            BaselineRunId = baselineRunId,
            SubjectVersion = subjectVersion
        };
    }

    // Post-hoc runs have no suite — `SelectionCriteriaJson` carries the resolved AgentExecutionId
    // list (and original filter, for audit) the background worker iterates. `Rubric` is the
    // single free-text judge rubric applied to every trace in this run — see ADR-013.
    // `forceRescore` defaults false (skip-if-already-scored); true means the worker supersedes
    // (delete-then-insert) rather than skips a previously-scored trace — see ADR-013 confirmation #3.
    public static EvalRun CreatePostHoc(
        string subjectVersion, string rubric, string selectionCriteriaJson, bool forceRescore = false)
    {
        return new EvalRun
        {
            Id = Guid.NewGuid(),
            SuiteId = null,
            Source = EvalRunSource.PostHoc,
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = EvalRunStatus.Pending,
            BaselineRunId = null,
            SubjectVersion = subjectVersion,
            Rubric = rubric,
            SelectionCriteriaJson = selectionCriteriaJson,
            ForceRescore = forceRescore
        };
    }

    public void MarkRunning() => Status = EvalRunStatus.Running;

    public void MarkCompleted()
    {
        Status = EvalRunStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = EvalRunStatus.Failed;
        ErrorMessage = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void IncrementSkippedCount() => SkippedAlreadyScoredCount++;
}
