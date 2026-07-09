using MediatR;

namespace OrchestAI.Application.Commands.RunEvalSuite;

public sealed record RunEvalSuiteCommand(
    Guid SuiteId,
    string SubjectVersion,
    Guid? BaselineRunId
) : IRequest<RunEvalSuiteResponse>;
