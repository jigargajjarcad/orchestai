using MediatR;

namespace OrchestAI.Application.Queries.GetErrorRateMonitoring;

public sealed record GetErrorRateMonitoringQuery(
    Guid UserId,
    DateOnly From,
    DateOnly To
) : IRequest<GetErrorRateMonitoringResponse>;
