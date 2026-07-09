using MediatR;

namespace OrchestAI.Application.Queries.GetEvalRuns;

public sealed record GetEvalRunsQuery(Guid SuiteId) : IRequest<GetEvalRunsResponse>;
