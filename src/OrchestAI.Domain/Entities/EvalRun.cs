using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class EvalRun
{
    private EvalRun() { }

    public Guid Id { get; private set; }
    public Guid SuiteId { get; private set; }
    public DateTimeOffset TriggeredAt { get; private set; }
    public EvalRunStatus Status { get; private set; }
    public Guid? BaselineRunId { get; private set; }
    public string SubjectVersion { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public EvalSuite Suite { get; private set; } = null!;
    public EvalRun? BaselineRun { get; private set; }

    public static EvalRun Create(Guid suiteId, string subjectVersion, Guid? baselineRunId)
    {
        return new EvalRun
        {
            Id = Guid.NewGuid(),
            SuiteId = suiteId,
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = EvalRunStatus.Pending,
            BaselineRunId = baselineRunId,
            SubjectVersion = subjectVersion
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
}
