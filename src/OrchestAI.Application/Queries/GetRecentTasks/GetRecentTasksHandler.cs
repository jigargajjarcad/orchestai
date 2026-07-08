using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetRecentTasks;

public sealed class GetRecentTasksHandler : IRequestHandler<GetRecentTasksQuery, IReadOnlyList<RecentTaskDto>>
{
    private readonly IOrchestrationTaskRepository _taskRepository;

    public GetRecentTasksHandler(IOrchestrationTaskRepository taskRepository)
    {
        _taskRepository = taskRepository;
    }

    public async Task<IReadOnlyList<RecentTaskDto>> Handle(
        GetRecentTasksQuery request, CancellationToken cancellationToken)
    {
        var tasks = await _taskRepository
            .GetRecentByUserIdAsync(request.UserId, request.Limit, cancellationToken)
            .ConfigureAwait(false);

        return tasks
            .Select(t => new RecentTaskDto(
                t.Id, t.Title, t.Status.ToString(), t.TotalCostUsd, t.CreatedAt, t.CompletedAt))
            .ToList()
            .AsReadOnly();
    }
}
