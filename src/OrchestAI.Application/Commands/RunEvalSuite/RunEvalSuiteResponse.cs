namespace OrchestAI.Application.Commands.RunEvalSuite;

public sealed record RunEvalSuiteResponse(
    Guid EvalRunId,
    Guid SuiteId,
    string Status,
    Guid? BaselineRunId,
    DateTimeOffset TriggeredAt
);
