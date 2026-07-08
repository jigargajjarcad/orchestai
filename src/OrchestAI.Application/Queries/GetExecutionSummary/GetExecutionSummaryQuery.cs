using MediatR;

namespace OrchestAI.Application.Queries.GetExecutionSummary;

public sealed record GetExecutionSummaryQuery(Guid TaskId) : IRequest<GetExecutionSummaryResponse?>;
