using MediatR;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Commands.AddEvalCase;

public sealed record AddEvalCaseCommand(
    Guid SuiteId,
    string InputPayloadJson,
    string ExpectedCriteriaJson,
    EvalScorerType ScorerType,
    decimal RegressionThreshold,
    string Tags
) : IRequest<AddEvalCaseResponse>;
