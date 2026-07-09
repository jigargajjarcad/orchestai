namespace OrchestAI.Application.Queries.GetEvalRunResults;

public sealed record GetEvalRunResultsResponse(
    Guid EvalRunId, string Status, IReadOnlyList<EvalResultDto> Results);

public sealed record EvalResultDto(
    Guid EvalCaseId, Guid? AgentExecutionId, string ScorerType, string ScorerVersion,
    decimal Score, bool Passed, string ScorerOutputJson, DateTimeOffset ScoredAt);
