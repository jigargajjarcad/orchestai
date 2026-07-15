using MediatR;

namespace OrchestAI.Application.Queries.GetRejections;

public sealed record GetRejectionsQuery(int Limit = 50) : IRequest<GetRejectionsResponse>;
