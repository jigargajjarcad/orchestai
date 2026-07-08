using MediatR;

namespace OrchestAI.Application.Queries.GetExecutionTimeline;

public sealed record GetExecutionTimelineQuery(Guid TaskId) : IRequest<GetExecutionTimelineResponse?>;
