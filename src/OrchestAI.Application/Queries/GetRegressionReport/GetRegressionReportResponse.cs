namespace OrchestAI.Application.Queries.GetRegressionReport;

public sealed record GetRegressionReportResponse(
    Guid EvalRunId,
    Guid BaselineRunId,
    decimal CurrentPassRate,
    decimal BaselinePassRate,
    decimal PassRateDelta,
    IReadOnlyList<CaseRegressionDto> CaseDiffs
);

public sealed record CaseRegressionDto(
    Guid EvalCaseId,
    decimal CurrentScore,
    decimal? BaselineScore,
    decimal? ScoreDelta,
    bool Regressed,
    bool IsNewCase
);
