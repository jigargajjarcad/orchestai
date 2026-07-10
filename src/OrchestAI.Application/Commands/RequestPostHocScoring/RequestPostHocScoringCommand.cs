using MediatR;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Commands.RequestPostHocScoring;

public sealed record RequestPostHocScoringCommand(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    AgentType? AgentType,
    IReadOnlyList<Guid>? TraceIds,
    EvalScorerType ScorerType,
    string Rubric,
    decimal? PassThreshold,
    int MaxTraces,
    bool ForceRescore = false) : IRequest<RequestPostHocScoringResponse>;
