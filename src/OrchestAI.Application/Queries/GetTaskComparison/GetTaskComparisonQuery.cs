using MediatR;

namespace OrchestAI.Application.Queries.GetTaskComparison;

public sealed record GetTaskComparisonQuery(
    Guid FirstTaskId,
    Guid SecondTaskId
) : IRequest<GetTaskComparisonResponse?>;
