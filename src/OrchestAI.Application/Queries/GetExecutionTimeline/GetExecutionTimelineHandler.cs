using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetExecutionTimeline;

public sealed class GetExecutionTimelineHandler
    : IRequestHandler<GetExecutionTimelineQuery, GetExecutionTimelineResponse?>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly ILogger<GetExecutionTimelineHandler> _logger;

    public GetExecutionTimelineHandler(
        IOrchestrationTaskRepository taskRepository,
        ILogger<GetExecutionTimelineHandler> logger)
    {
        _taskRepository = taskRepository;
        _logger = logger;
    }

    public async Task<GetExecutionTimelineResponse?> Handle(
        GetExecutionTimelineQuery request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository
            .GetByIdWithExecutionsMessagesAndToolCallsAsync(request.TaskId, cancellationToken)
            .ConfigureAwait(false);

        if (task is null)
        {
            _logger.LogWarning("Orchestration task {TaskId} not found for timeline query", request.TaskId);
            return null;
        }

        var spans = new List<TimelineSpanDto>();

        foreach (var execution in task.AgentExecutions)
        {
            int? durationMs = execution.StartedAt.HasValue && execution.CompletedAt.HasValue
                ? (int)(execution.CompletedAt.Value - execution.StartedAt.Value).TotalMilliseconds
                : null;

            spans.Add(new TimelineSpanDto(
                execution.SpanId,
                execution.ParentSpanId,
                "AgentExecution",
                execution.AgentType.ToString(),
                execution.Status.ToString(),
                execution.ErrorCategory?.ToString(),
                execution.StartedAt,
                execution.CompletedAt,
                durationMs,
                execution.InputTokens,
                execution.OutputTokens,
                execution.CostUsd));

            foreach (var toolCall in execution.ToolCalls)
            {
                spans.Add(new TimelineSpanDto(
                    toolCall.SpanId,
                    toolCall.ParentSpanId,
                    "ToolCall",
                    toolCall.ToolName,
                    toolCall.Success ? "Completed" : "Failed",
                    toolCall.ErrorCategory?.ToString(),
                    toolCall.CreatedAt,
                    toolCall.DurationMs.HasValue
                        ? toolCall.CreatedAt.AddMilliseconds(toolCall.DurationMs.Value)
                        : null,
                    toolCall.DurationMs,
                    null,
                    null,
                    null));
            }
        }

        return new GetExecutionTimelineResponse(
            task.Id, task.TraceId, spans.OrderBy(s => s.StartedAt).ToList().AsReadOnly());
    }
}
