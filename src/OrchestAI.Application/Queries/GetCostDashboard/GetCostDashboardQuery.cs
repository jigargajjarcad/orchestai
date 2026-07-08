using MediatR;

namespace OrchestAI.Application.Queries.GetCostDashboard;

public sealed record GetCostDashboardQuery(
    Guid UserId,
    DateOnly From,
    DateOnly To
) : IRequest<GetCostDashboardResponse>;
