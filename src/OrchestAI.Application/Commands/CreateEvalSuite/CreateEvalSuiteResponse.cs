namespace OrchestAI.Application.Commands.CreateEvalSuite;

public sealed record CreateEvalSuiteResponse(
    Guid Id,
    string Name,
    string Description,
    string TargetAgentType,
    DateTimeOffset CreatedAt
);
