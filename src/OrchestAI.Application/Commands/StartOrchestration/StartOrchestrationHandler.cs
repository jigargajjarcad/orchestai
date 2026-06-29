using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Application.Commands.StartOrchestration;

public sealed class StartOrchestrationHandler
    : IRequestHandler<StartOrchestrationCommand, StartOrchestrationResponse>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly IOrchestratorAgent _orchestratorAgent;
    private readonly IAgentFactory _agentFactory;
    private readonly IOrchestrationEventBus _eventBus;
    private readonly ILogger<StartOrchestrationHandler> _logger;

    public StartOrchestrationHandler(
        IOrchestrationTaskRepository taskRepository,
        IOrchestratorAgent orchestratorAgent,
        IAgentFactory agentFactory,
        IOrchestrationEventBus eventBus,
        ILogger<StartOrchestrationHandler> logger)
    {
        _taskRepository = taskRepository;
        _orchestratorAgent = orchestratorAgent;
        _agentFactory = agentFactory;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<StartOrchestrationResponse> Handle(
        StartOrchestrationCommand request,
        CancellationToken cancellationToken)
    {
        var task = await _taskRepository
            .GetByIdAsync(request.TaskId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

        if (task.Status != OrchestrationTaskStatus.Pending)
            throw new InvalidOperationException(
                $"Task {request.TaskId} is in '{task.Status}' state and cannot be started.");

        task.MarkRunning();
        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(request.TaskId, new SseEvent(
            "task_started",
            request.TaskId,
            new { taskId = request.TaskId, status = "Running" },
            DateTimeOffset.UtcNow));

        _logger.LogInformation("Task {TaskId} started, running orchestrator", request.TaskId);

        var plan = await _orchestratorAgent
            .PlanAsync(request.TaskId, task.UserPrompt, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Orchestrator selected {AgentCount} agents for task {TaskId}: {Agents}",
            plan.SelectedAgents.Count,
            request.TaskId,
            string.Join(", ", plan.SelectedAgents));

        AgentExecutionResult[] results;

        if (plan.ExecutionMode == ExecutionMode.Sequential)
        {
            _logger.LogInformation(
                "Task {TaskId} using sequential execution across {AgentCount} agents",
                request.TaskId, plan.ExecutionOrder.Count);
            results = await RunSequentialAsync(request.TaskId, plan, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var subAgentTasks = plan.ExecutionOrder
                .Select(agentType => RunSubAgentAsync(
                    request.TaskId, agentType, plan.AgentPrompts[agentType], cancellationToken))
                .ToList();
            results = await Task.WhenAll(subAgentTasks).ConfigureAwait(false);
        }

        var successResults = results.Where(r => r.Success).ToList();
        var failedResults = results.Where(r => !r.Success).ToList();

        var aggregatedOutput = string.Join("\n\n---\n\n",
            successResults.Select(r => r.Output).Where(o => !string.IsNullOrWhiteSpace(o)));

        var allResults = results.Append(plan.OrchestratorExecution);
        var totalInputTokens = allResults.Sum(r => r.InputTokens);
        var totalOutputTokens = allResults.Sum(r => r.OutputTokens);
        var totalCostUsd = allResults.Sum(r => r.CostUsd);

        task.AccumulateCost(totalInputTokens, totalOutputTokens, totalCostUsd);

        if (failedResults.Count == 0)
        {
            task.MarkCompleted(aggregatedOutput);

            _eventBus.Publish(request.TaskId, new SseEvent(
                "task_completed",
                request.TaskId,
                new { taskId = request.TaskId, totalCostUsd = task.TotalCostUsd, agentCount = results.Length },
                DateTimeOffset.UtcNow));

            _logger.LogInformation(
                "Task {TaskId} completed. Cost: ${CostUsd:F4}", request.TaskId, task.TotalCostUsd);
        }
        else
        {
            var errorSummary = string.Join("; ", failedResults.Select(r => r.ErrorMessage));
            task.MarkFailed(errorSummary);

            _eventBus.Publish(request.TaskId, new SseEvent(
                "task_failed",
                request.TaskId,
                new { taskId = request.TaskId, errorMessage = errorSummary },
                DateTimeOffset.UtcNow));

            _logger.LogWarning(
                "Task {TaskId} failed with {FailedCount} agent failures", request.TaskId, failedResults.Count);
        }

        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        return new StartOrchestrationResponse(
            request.TaskId,
            results.Select(r => r.AgentExecutionId).ToList().AsReadOnly());
    }

    private async Task<AgentExecutionResult> RunSubAgentAsync(
        Guid taskId,
        AgentType agentType,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = _agentFactory.Create(agentType);
            return await agent.ExecuteAsync(taskId, prompt, cancellationToken).ConfigureAwait(false);
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
        OrchestrationPlan plan,
        CancellationToken cancellationToken)
    {
        var results = new List<AgentExecutionResult>();
        string? priorOutput = null;

        foreach (var agentType in plan.ExecutionOrder)
        {
            var prompt = BuildSequentialPrompt(plan.AgentPrompts[agentType], priorOutput);
            var result = await RunSubAgentAsync(taskId, agentType, prompt, cancellationToken).ConfigureAwait(false);
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
