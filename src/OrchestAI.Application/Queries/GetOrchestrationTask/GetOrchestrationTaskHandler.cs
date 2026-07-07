using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.DTOs;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetOrchestrationTask;

public sealed class GetOrchestrationTaskHandler
    : IRequestHandler<GetOrchestrationTaskQuery, GetOrchestrationTaskResponse?>
{
    private readonly IOrchestrationTaskRepository _repository;
    private readonly ILogger<GetOrchestrationTaskHandler> _logger;

    public GetOrchestrationTaskHandler(
        IOrchestrationTaskRepository repository,
        ILogger<GetOrchestrationTaskHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetOrchestrationTaskResponse?> Handle(
        GetOrchestrationTaskQuery request,
        CancellationToken cancellationToken)
    {
        var task = request.IncludeToolCalls
            ? await _repository
                .GetByIdWithExecutionsMessagesAndToolCallsAsync(request.TaskId, cancellationToken)
                .ConfigureAwait(false)
            : request.IncludeMessages
                ? await _repository
                    .GetByIdWithExecutionsAndMessagesAsync(request.TaskId, cancellationToken)
                    .ConfigureAwait(false)
                : await _repository
                    .GetByIdWithExecutionsAsync(request.TaskId, cancellationToken)
                    .ConfigureAwait(false);

        if (task is null)
        {
            _logger.LogWarning("Orchestration task {TaskId} not found", request.TaskId);
            return null;
        }

        var executions = task.AgentExecutions
            .Select(e => new AgentExecutionDto(
                e.Id,
                e.AgentType.ToString(),
                e.Status.ToString(),
                e.InputPrompt,
                e.OutputResult,
                e.InputTokens,
                e.OutputTokens,
                e.CostUsd,
                e.ErrorMessage,
                e.StartedAt,
                e.CompletedAt,
                e.CreatedAt,
                request.IncludeMessages || request.IncludeToolCalls
                    ? e.Messages
                        .OrderBy(m => m.SequenceOrder)
                        .Select(m => new AgentMessageDto(
                            m.Id,
                            m.Role.ToString(),
                            m.Content,
                            m.SequenceOrder,
                            m.CreatedAt))
                        .ToList()
                        .AsReadOnly()
                    : null,
                request.IncludeToolCalls
                    ? e.ToolCalls
                        .OrderBy(tc => tc.CreatedAt)
                        .Select(tc => new McpToolCallDto(
                            tc.Id,
                            tc.ToolName,
                            tc.Success,
                            tc.DurationMs,
                            tc.InputParameters,
                            tc.OutputResult != null
                                ? tc.OutputResult[..Math.Min(500, tc.OutputResult.Length)]
                                : null,
                            tc.ErrorMessage,
                            tc.CreatedAt))
                        .ToList()
                        .AsReadOnly()
                    : null))
            .ToList()
            .AsReadOnly();

        return new GetOrchestrationTaskResponse(
            task.Id,
            task.UserId,
            task.Title,
            task.UserPrompt,
            task.Status.ToString(),
            task.FinalResult,
            task.TotalInputTokens,
            task.TotalOutputTokens,
            task.TotalCostUsd,
            task.ErrorMessage,
            task.RequireApproval,
            task.ApprovalStatus?.ToString(),
            task.ApprovalRequestedAt,
            task.ApprovedAt,
            task.ApprovalNote,
            task.CreatedAt,
            task.UpdatedAt,
            task.CompletedAt,
            executions);
    }
}
