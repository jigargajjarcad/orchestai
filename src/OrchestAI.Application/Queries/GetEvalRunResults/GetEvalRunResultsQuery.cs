using MediatR;

namespace OrchestAI.Application.Queries.GetEvalRunResults;

public sealed record GetEvalRunResultsQuery(Guid EvalRunId) : IRequest<GetEvalRunResultsResponse>;
