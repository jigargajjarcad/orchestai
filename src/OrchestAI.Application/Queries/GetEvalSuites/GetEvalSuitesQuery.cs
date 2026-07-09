using MediatR;

namespace OrchestAI.Application.Queries.GetEvalSuites;

public sealed record GetEvalSuitesQuery : IRequest<GetEvalSuitesResponse>;
