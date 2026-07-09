namespace OrchestAI.Application.Queries.GetEvalRuns;

public sealed record GetEvalRunsResponse(IReadOnlyList<EvalRunSummaryDto> Runs);

public sealed record EvalRunSummaryDto(
    Guid Id, string Status, string SubjectVersion, Guid? BaselineRunId,
    DateTimeOffset TriggeredAt, DateTimeOffset? CompletedAt);
