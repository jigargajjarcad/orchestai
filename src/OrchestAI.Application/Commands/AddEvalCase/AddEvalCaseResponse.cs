namespace OrchestAI.Application.Commands.AddEvalCase;

public sealed record AddEvalCaseResponse(
    Guid Id,
    Guid SuiteId,
    string ScorerType,
    decimal RegressionThreshold,
    DateTimeOffset CreatedAt
);
