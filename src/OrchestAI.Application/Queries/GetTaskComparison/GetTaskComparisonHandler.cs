using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetTaskComparison;

public sealed class GetTaskComparisonHandler : IRequestHandler<GetTaskComparisonQuery, GetTaskComparisonResponse?>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly ILogger<GetTaskComparisonHandler> _logger;

    public GetTaskComparisonHandler(
        IOrchestrationTaskRepository taskRepository, ILogger<GetTaskComparisonHandler> logger)
    {
        _taskRepository = taskRepository;
        _logger = logger;
    }

    public async Task<GetTaskComparisonResponse?> Handle(
        GetTaskComparisonQuery request, CancellationToken cancellationToken)
    {
        var firstTask = _taskRepository.GetByIdWithExecutionsAsync(request.FirstTaskId, cancellationToken);
        var secondTask = _taskRepository.GetByIdWithExecutionsAsync(request.SecondTaskId, cancellationToken);
        await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);

        if (firstTask.Result is null || secondTask.Result is null)
        {
            _logger.LogWarning(
                "Task comparison requested with a missing task: {FirstTaskId} (found={FirstFound}), {SecondTaskId} (found={SecondFound})",
                request.FirstTaskId, firstTask.Result is not null, request.SecondTaskId, secondTask.Result is not null);
            return null;
        }

        return new GetTaskComparisonResponse(ToSide(firstTask.Result), ToSide(secondTask.Result));
    }

    private static TaskComparisonSideDto ToSide(OrchestrationTask task)
    {
        double? durationSeconds = task.CompletedAt.HasValue
            ? (task.CompletedAt.Value - task.CreatedAt).TotalSeconds
            : null;

        var executions = task.AgentExecutions
            .OrderBy(e => e.CreatedAt)
            .Select(e => new ComparisonAgentExecutionDto(
                e.AgentType.ToString(),
                e.Status.ToString(),
                e.InputPrompt,
                e.OutputResult,
                e.InputTokens,
                e.OutputTokens,
                e.CostUsd,
                e.StartedAt.HasValue && e.CompletedAt.HasValue
                    ? (int)(e.CompletedAt.Value - e.StartedAt.Value).TotalMilliseconds
                    : null))
            .ToList()
            .AsReadOnly();

        return new TaskComparisonSideDto(
            task.Id,
            task.UserPrompt,
            task.Status.ToString(),
            task.FinalResult,
            task.TotalInputTokens,
            task.TotalOutputTokens,
            task.TotalCostUsd,
            durationSeconds,
            executions);
    }
}
