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
    private readonly IApprovalGateway _approvalGateway;
    private readonly ITaskCheckpointRepository _checkpointRepository;
    private readonly ITaskAdmissionReservationRepository _reservationRepository;
    private readonly ILogger<StartOrchestrationHandler> _logger;

    public StartOrchestrationHandler(
        IOrchestrationTaskRepository taskRepository,
        IOrchestratorAgent orchestratorAgent,
        IAgentFactory agentFactory,
        IOrchestrationEventBus eventBus,
        IApprovalGateway approvalGateway,
        ITaskCheckpointRepository checkpointRepository,
        ITaskAdmissionReservationRepository reservationRepository,
        ILogger<StartOrchestrationHandler> logger)
    {
        _taskRepository = taskRepository;
        _orchestratorAgent = orchestratorAgent;
        _agentFactory = agentFactory;
        _eventBus = eventBus;
        _approvalGateway = approvalGateway;
        _checkpointRepository = checkpointRepository;
        _reservationRepository = reservationRepository;
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

        // AdmitOrchestrationTaskHandler (Task 6) already performed the Pending -> Running CAS
        // and the concurrency/budget reservation atomically, awaited by the controller BEFORE
        // this background dispatch was even started (see TasksController.StartAsync). This is a
        // defensive check for a state that should be unreachable, not the primary guard.
        if (task.Status != OrchestrationTaskStatus.Running)
            throw new InvalidOperationException(
                $"Task {request.TaskId} reached StartOrchestrationHandler in unexpected state " +
                $"'{task.Status}' — admission should have already transitioned it to Running.");

        try
        {
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

            if (task.RequireApproval)
            {
                task.RequestApproval();
                await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

                _eventBus.Publish(request.TaskId, new SseEvent(
                    "approval_required",
                    request.TaskId,
                    new
                    {
                        taskId = request.TaskId,
                        plan = plan.Plan,
                        selectedAgents = plan.SelectedAgents.Select(a => a.ToString()).ToList(),
                        agentPrompts = plan.AgentPrompts.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                        executionMode = plan.ExecutionMode.ToString()
                    },
                    DateTimeOffset.UtcNow));

                _logger.LogInformation("Task {TaskId} waiting for human approval", request.TaskId);

                await _approvalGateway.WaitForApprovalAsync(request.TaskId, cancellationToken).ConfigureAwait(false);

                task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

                if (task.ApprovalStatus == TaskApprovalStatus.Rejected)
                {
                    // RejectOrchestrationTaskHandler already marked the task Failed and published task_failed.
                    _logger.LogInformation("Task {TaskId} rejected — aborting before agent dispatch", request.TaskId);
                    return new StartOrchestrationResponse(request.TaskId, []);
                }

                _logger.LogInformation("Task {TaskId} approved — resuming agent dispatch", request.TaskId);
            }

            AgentExecutionResult[] results;

            if (plan.ExecutionMode == ExecutionMode.Sequential)
            {
                _logger.LogInformation(
                    "Task {TaskId} using sequential execution across {AgentCount} agents",
                    request.TaskId, plan.ExecutionOrder.Count);
                results = await RunSequentialAsync(
                    request.TaskId, task.UserId, plan, plan.OrchestratorExecution.SpanId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var subAgentTasks = plan.ExecutionOrder
                    .Select(agentType => RunSubAgentAsync(
                        request.TaskId, task.UserId, agentType, plan.AgentPrompts[agentType],
                        plan.OrchestratorExecution.SpanId, cancellationToken))
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

            var allResults = results.Append(plan.OrchestratorExecution).Append(reviewResult).ToList();
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

                _logger.LogInformation(
                    "Task {TaskId} completed. Cost: ${CostUsd:F4}", request.TaskId, task.TotalCostUsd);
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
                    "Task {TaskId} failed with {FailedCount} agent failures", request.TaskId, failedResults.Count);
            }

            await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

            return new StartOrchestrationResponse(
                request.TaskId,
                results.Select(r => r.AgentExecutionId).ToList().AsReadOnly());
        }
        finally
        {
            // Releases both the concurrency slot and the budget reservation together — covers
            // success, task failure (MarkFailed above), and any unhandled exception in this
            // block alike. Best-effort: a failure here must never mask whatever outcome the try
            // block actually produced (see StartOrchestrationReservationReleaseTests) — it only
            // means the reservation leaks until it ages past the staleness TTL (Task 6/ADR-015).
            try
            {
                await _reservationRepository.ReleaseAsync(request.TaskId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to release admission reservation for task {TaskId} — this may leak tenant " +
                    "concurrency/budget capacity until the reservation ages past the staleness TTL",
                    request.TaskId);
            }
        }
    }

    private async Task<AgentExecutionResult> RunSubAgentAsync(
        Guid taskId,
        Guid userId,
        AgentType agentType,
        string prompt,
        string? parentSpanId,
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
        string? parentSpanId,
        CancellationToken cancellationToken)
    {
        var results = new List<AgentExecutionResult>();
        string? priorOutput = null;

        foreach (var agentType in plan.ExecutionOrder)
        {
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
