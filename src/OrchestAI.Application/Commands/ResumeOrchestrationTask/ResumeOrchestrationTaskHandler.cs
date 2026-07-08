using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Application.Commands.ResumeOrchestrationTask;

public sealed class ResumeOrchestrationTaskHandler
    : IRequestHandler<ResumeOrchestrationTaskCommand, ResumeOrchestrationTaskResponse>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly IOrchestratorAgent _orchestratorAgent;
    private readonly IAgentFactory _agentFactory;
    private readonly ITaskCheckpointRepository _checkpointRepository;
    private readonly IOrchestrationEventBus _eventBus;
    private readonly ILogger<ResumeOrchestrationTaskHandler> _logger;

    public ResumeOrchestrationTaskHandler(
        IOrchestrationTaskRepository taskRepository,
        IOrchestratorAgent orchestratorAgent,
        IAgentFactory agentFactory,
        ITaskCheckpointRepository checkpointRepository,
        IOrchestrationEventBus eventBus,
        ILogger<ResumeOrchestrationTaskHandler> logger)
    {
        _taskRepository = taskRepository;
        _orchestratorAgent = orchestratorAgent;
        _agentFactory = agentFactory;
        _checkpointRepository = checkpointRepository;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<ResumeOrchestrationTaskResponse> Handle(
        ResumeOrchestrationTaskCommand request,
        CancellationToken cancellationToken)
    {
        var task = await _taskRepository
            .GetByIdWithExecutionsAsync(request.TaskId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

        if (task.Status != OrchestrationTaskStatus.Failed)
            throw new ConflictException(
                $"Task {request.TaskId} is in '{task.Status}' state and cannot be resumed — only Failed tasks can be resumed.");

        var planExecution = task.AgentExecutions
            .Where(e => e.AgentType == AgentType.Orchestrator && e.Status == ExecutionStatus.Completed)
            .OrderBy(e => e.CreatedAt)
            .FirstOrDefault()
            ?? throw new ConflictException(
                $"Task {request.TaskId} has no completed Orchestrator plan to resume from.");

        if (!OrchestrationPlanParser.TryParse(planExecution.OutputResult ?? string.Empty, out var plan) || plan is null)
            throw new ConflictException(
                $"Task {request.TaskId}'s stored orchestration plan could not be parsed — cannot resume.");

        // The parser has no access to the persisted AgentExecution, so OrchestratorExecution
        // comes back null — reconstruct it from the loaded plan execution so ReviewAsync can
        // use its SpanId as the review's parent span.
        plan = plan with
        {
            OrchestratorExecution = new AgentExecutionResult(
                planExecution.Id,
                planExecution.OutputResult ?? string.Empty,
                true,
                planExecution.InputTokens,
                planExecution.OutputTokens,
                planExecution.CostUsd,
                SpanId: planExecution.SpanId)
        };

        var checkpoints = await _checkpointRepository
            .GetByTaskIdAsync(request.TaskId, cancellationToken)
            .ConfigureAwait(false);

        var checkpointedResults = checkpoints.ToDictionary(
            c => c.AgentType,
            c => new AgentExecutionResult(c.AgentExecutionId, c.Output, true, c.InputTokens, c.OutputTokens, c.CostUsd));

        var skippedAgents = plan.ExecutionOrder.Where(checkpointedResults.ContainsKey).ToList();
        var resumingFrom = plan.ExecutionOrder.Where(a => !checkpointedResults.ContainsKey(a)).ToList();

        task.MarkRunning();
        task.MarkResumed();
        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(request.TaskId, new SseEvent(
            "task_resumed",
            request.TaskId,
            new { taskId = request.TaskId, skippedAgents = skippedAgents.Select(a => a.ToString()).ToList(), resumingFrom = resumingFrom.Select(a => a.ToString()).ToList() },
            DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "Task {TaskId} resumed — skipping {SkippedCount} checkpointed agents, running {ResumeCount} remaining",
            request.TaskId, skippedAgents.Count, resumingFrom.Count);

        AgentExecutionResult[] results;

        if (plan.ExecutionMode == ExecutionMode.Sequential)
        {
            results = await RunSequentialAsync(
                request.TaskId, task.UserId, plan, checkpointedResults, planExecution.SpanId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var subAgentTasks = plan.ExecutionOrder
                .Select(agentType => checkpointedResults.TryGetValue(agentType, out var cached)
                    ? Task.FromResult(cached)
                    : RunSubAgentAsync(
                        request.TaskId, task.UserId, agentType, plan.AgentPrompts[agentType],
                        planExecution.SpanId, cancellationToken))
                .ToList();
            results = await Task.WhenAll(subAgentTasks).ConfigureAwait(false);
        }

        var successResults = results.Where(r => r.Success).ToList();
        var failedResults = results.Where(r => !r.Success).ToList();

        var aggregatedOutput = string.Join("\n\n---\n\n",
            successResults.Select(r => r.Output).Where(o => !string.IsNullOrWhiteSpace(o)));

        var reviewResult = await _orchestratorAgent
            .ReviewAsync(request.TaskId, task.UserPrompt, plan, results, cancellationToken)
            .ConfigureAwait(false);

        var allResults = results.Append(reviewResult).ToList();
        var synthesizedOutput = reviewResult.Success ? reviewResult.Output : aggregatedOutput;

        var totalInputTokens = allResults.Sum(r => r.InputTokens);
        var totalOutputTokens = allResults.Sum(r => r.OutputTokens);
        var totalCostUsd = allResults.Sum(r => r.CostUsd);
        task.AccumulateCost(totalInputTokens, totalOutputTokens, totalCostUsd);

        if (failedResults.Count == 0)
        {
            task.MarkCompleted(synthesizedOutput);
            await _checkpointRepository.DeleteByTaskIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false);

            _eventBus.Publish(request.TaskId, new SseEvent(
                "task_completed",
                request.TaskId,
                new { taskId = request.TaskId, totalCostUsd = task.TotalCostUsd, agentCount = results.Length },
                DateTimeOffset.UtcNow));

            _logger.LogInformation("Task {TaskId} completed after resume. Cost: ${CostUsd:F4}", request.TaskId, task.TotalCostUsd);
        }
        else
        {
            var errorSummary = string.Join("; ", failedResults.Select(r => r.ErrorMessage));
            task.MarkFailed(errorSummary, synthesizedOutput);

            _eventBus.Publish(request.TaskId, new SseEvent(
                "task_failed",
                request.TaskId,
                new { taskId = request.TaskId, errorMessage = errorSummary },
                DateTimeOffset.UtcNow));

            _logger.LogWarning(
                "Task {TaskId} failed again after resume with {FailedCount} agent failures", request.TaskId, failedResults.Count);
        }

        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        return new ResumeOrchestrationTaskResponse(request.TaskId, resumingFrom, skippedAgents);
    }

    private async Task<AgentExecutionResult> RunSubAgentAsync(
        Guid taskId, Guid userId, AgentType agentType, string prompt, string? parentSpanId,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = _agentFactory.Create(agentType);
            return await agent.ExecuteAsync(taskId, userId, prompt, cancellationToken, parentSpanId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sub-agent {AgentType} threw an unhandled exception for task {TaskId}",
                agentType, taskId);
            return new AgentExecutionResult(Guid.Empty, string.Empty, false, 0, 0, 0m, ex.Message);
        }
    }

    private async Task<AgentExecutionResult[]> RunSequentialAsync(
        Guid taskId,
        Guid userId,
        OrchestrationPlan plan,
        IReadOnlyDictionary<AgentType, AgentExecutionResult> checkpointedResults,
        string? parentSpanId,
        CancellationToken cancellationToken)
    {
        var results = new List<AgentExecutionResult>();
        string? priorOutput = null;

        foreach (var agentType in plan.ExecutionOrder)
        {
            if (checkpointedResults.TryGetValue(agentType, out var cached))
            {
                results.Add(cached);
                priorOutput = cached.Output;
                continue;
            }

            var prompt = BuildSequentialPrompt(plan.AgentPrompts[agentType], priorOutput);
            var result = await RunSubAgentAsync(taskId, userId, agentType, prompt, parentSpanId, cancellationToken)
                .ConfigureAwait(false);
            results.Add(result);

            if (result.Success)
                priorOutput = result.Output;
            else
                _logger.LogWarning(
                    "Sequential agent {AgentType} failed for task {TaskId}: {Error}. Continuing to next agent.",
                    agentType, taskId, result.ErrorMessage);
        }

        return [.. results];
    }

    private static string BuildSequentialPrompt(string basePrompt, string? priorOutput)
    {
        if (string.IsNullOrWhiteSpace(priorOutput))
            return basePrompt;

        const int MaxPriorLength = 3000;
        var prior = priorOutput.Length > MaxPriorLength
            ? priorOutput[..MaxPriorLength] + "\n[...truncated...]"
            : priorOutput;

        return $"{basePrompt}\n\n--- Prior Agent Output ---\n{prior}";
    }
}
