namespace OrchestAI.Application.Queries.GetEvalSuites;

public sealed record GetEvalSuitesResponse(IReadOnlyList<EvalSuiteSummaryDto> Suites);

public sealed record EvalSuiteSummaryDto(
    Guid Id, string Name, string Description, string TargetAgentType, DateTimeOffset CreatedAt);
