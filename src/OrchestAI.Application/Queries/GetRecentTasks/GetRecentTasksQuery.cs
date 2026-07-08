using MediatR;

namespace OrchestAI.Application.Queries.GetRecentTasks;

public sealed record GetRecentTasksQuery(Guid UserId, int Limit = 20) : IRequest<IReadOnlyList<RecentTaskDto>>;
