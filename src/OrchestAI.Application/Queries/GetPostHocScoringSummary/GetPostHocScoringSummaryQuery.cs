using MediatR;

namespace OrchestAI.Application.Queries.GetPostHocScoringSummary;

public sealed record GetPostHocScoringSummaryQuery(Guid EvalRunId) : IRequest<GetPostHocScoringSummaryResponse>;
