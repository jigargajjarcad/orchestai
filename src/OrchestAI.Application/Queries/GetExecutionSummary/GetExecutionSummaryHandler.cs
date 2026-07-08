using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetExecutionSummary;

public sealed class GetExecutionSummaryHandler
    : IRequestHandler<GetExecutionSummaryQuery, GetExecutionSummaryResponse?>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly ICostLedgerRepository _costLedgerRepository;
    private readonly IAgentRetryAttemptRepository _retryAttemptRepository;
    private readonly ILogger<GetExecutionSummaryHandler> _logger;

    public GetExecutionSummaryHandler(
        IOrchestrationTaskRepository taskRepository,
        ICostLedgerRepository costLedgerRepository,
        IAgentRetryAttemptRepository retryAttemptRepository,
        ILogger<GetExecutionSummaryHandler> logger)
    {
        _taskRepository = taskRepository;
        _costLedgerRepository = costLedgerRepository;
        _retryAttemptRepository = retryAttemptRepository;
        _logger = logger;
    }

    public async Task<GetExecutionSummaryResponse?> Handle(
        GetExecutionSummaryQuery request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository
            .GetByIdWithExecutionsMessagesAndToolCallsAsync(request.TaskId, cancellationToken)
            .ConfigureAwait(false);

        if (task is null)
        {
            _logger.LogWarning("Orchestration task {TaskId} not found for summary query", request.TaskId);
            return null;
        }

        var executionIds = task.AgentExecutions.Select(e => e.Id).ToList();

        var costLedgerEntriesTask = _costLedgerRepository.GetByTaskIdAsync(task.Id, cancellationToken);
        var retryAttemptsTask = _retryAttemptRepository.GetByAgentExecutionIdsAsync(executionIds, cancellationToken);
        await Task.WhenAll(costLedgerEntriesTask, retryAttemptsTask).ConfigureAwait(false);

        var costLedgerEntries = costLedgerEntriesTask.Result;
        var retryAttempts = retryAttemptsTask.Result;

        double? durationSeconds = task.CompletedAt.HasValue
            ? (task.CompletedAt.Value - task.CreatedAt).TotalSeconds
            : null;

        return new GetExecutionSummaryResponse(
            task.Id,
            task.Status.ToString(),
            durationSeconds,
            task.TotalCostUsd,
            task.TotalInputTokens,
            task.TotalOutputTokens,
            task.AgentExecutions.Select(e => e.AgentType.ToString()).Distinct().ToList().AsReadOnly(),
            task.AgentExecutions.Sum(e => e.ToolCalls.Count),
            costLedgerEntries.Select(c => c.Model).Distinct().ToList().AsReadOnly(),
            retryAttempts.Count,
            task.AgentExecutions.Count(e => e.Status == ExecutionStatus.Failed),
            task.AgentExecutions.Any(e => e.MemoriesInjectedCount > 0),
            task.ResumedAt.HasValue);
    }
}
