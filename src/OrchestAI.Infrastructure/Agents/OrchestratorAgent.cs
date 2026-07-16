using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Agents;

public sealed class OrchestratorAgent : AgentBase, IOrchestratorAgent
{
    protected override AgentType AgentType => AgentType.Orchestrator;

    protected override string SystemPrompt =>
        """
        You are an orchestration coordinator. Your job is to analyze a user task and decide
        which specialized agents are needed to complete it, what execution mode to use, and in what order.

        Available agents:
        - Research: web search, deep page extraction, AI-powered research with citations
        - Writer: content generation, summarization, report writing, formatting
        - Code: file system operations, browser automation for testing
        - Data: database queries, structured data extraction from web
        - Browser: full browser control, navigation, screenshots, form interaction

        Execution modes:
        - "parallel": agents run concurrently — use when agents are independent of each other
        - "sequential": agents run one at a time, each receiving the prior agent's output — use when one agent's output feeds the next

        Respond with ONLY valid JSON in this exact format — no explanation, no markdown:
        {
          "plan": "one sentence describing the overall approach",
          "execution_mode": "parallel",
          "agents": ["Research", "Writer"],
          "execution_order": ["Research", "Writer"],
          "reasoning": {
            "Research": "why this agent is needed",
            "Writer": "why this agent is needed"
          },
          "agent_prompts": {
            "Research": "specific prompt for the research agent",
            "Writer": "specific prompt for the writer agent"
          }
        }

        Only include agents that are actually needed. Most tasks need 1-3 agents.
        For sequential mode, execution_order determines which agent runs first.
        """;

    private const string ReviewSystemPrompt =
        """
        You are a quality control manager reviewing the outputs of your AI agent team.
        You have received the outputs from each agent that worked on a task.
        Your job is to:
        1. Assess whether each agent completed their assigned work successfully
        2. Identify any gaps, errors, or missing information
        3. Synthesize all outputs into a single, coherent final result
        4. Note any agents that failed or produced poor quality output

        Be concise in your assessment. Lead with the synthesized result, then add a brief
        quality note at the end. Format: synthesized content first, then "---\nQuality Note: ..."
        """;

    public OrchestratorAgent(
        ILlmProviderFactory llmProviderFactory,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        ITaskCheckpointRepository checkpointRepository,
        IAgentMemoryRepository memoryRepository,
        IAgentRetryAttemptRepository agentRetryAttemptRepository,
        IPiiRedactor piiRedactor,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IModelPricingCache modelPricingCache,
        IOptions<RetryPolicyOptions> retryOptions,
        IToolRegistry toolRegistry,
        ITaskToolCallBudget taskToolCallBudget,
        IRejectionEventRepository rejectionEventRepository,
        ILoggerFactory loggerFactory)
        : base(llmProviderFactory, agentExecutionRepository, agentMessageRepository,
               costLedgerRepository, mcpToolCallRepository, checkpointRepository,
               memoryRepository, agentRetryAttemptRepository, piiRedactor, eventBus, agentOptions, modelPricingCache,
               retryOptions, toolRegistry, taskToolCallBudget, rejectionEventRepository, loggerFactory)
    {
    }

    public async Task<OrchestrationPlan> PlanAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var execution = await SetupExecutionAsync(orchestrationTaskId, userPrompt, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var messages = new List<ConversationMessage> { new("user", userPrompt) };

            var (text, inputTokens, outputTokens, costUsd) =
                await RunLlmTurnAsync(execution, SystemPrompt, messages, 1, cancellationToken).ConfigureAwait(false);

            var totalInputTokens = inputTokens;
            var totalOutputTokens = outputTokens;
            var totalCostUsd = costUsd;

            if (!TryParseOrchestrationPlan(text, out var plan))
            {
                _logger.LogWarning(
                    "Orchestrator returned invalid JSON, retrying. ExecutionId: {ExecutionId}", execution.Id);

                messages.Add(new ConversationMessage("assistant", text));
                messages.Add(new ConversationMessage(
                    "user", "Your previous response was not valid JSON. Return ONLY the JSON object, no other text."));

                var (retryText, retryInput, retryOutput, retryCost) =
                    await RunLlmTurnAsync(execution, SystemPrompt, messages, 2, cancellationToken).ConfigureAwait(false);

                totalInputTokens += retryInput;
                totalOutputTokens += retryOutput;
                totalCostUsd += retryCost;

                if (!TryParseOrchestrationPlan(retryText, out plan))
                    throw new InvalidOperationException(
                        "Orchestrator failed to return valid JSON after retry.");

                text = retryText;
            }

            var executionResult = await FinalizeSuccessAsync(
                execution, text, totalInputTokens, totalOutputTokens, totalCostUsd, cancellationToken)
                .ConfigureAwait(false);

            _eventBus.Publish(orchestrationTaskId, new SseEvent(
                "orchestrator_plan",
                orchestrationTaskId,
                new
                {
                    plan = plan!.Plan,
                    executionMode = plan.ExecutionMode.ToString(),
                    selectedAgents = plan.SelectedAgents.Select(a => a.ToString()).ToList(),
                    executionOrder = plan.ExecutionOrder.Select(a => a.ToString()).ToList(),
                    agentPrompts = plan.AgentPrompts.ToDictionary(
                        kvp => kvp.Key.ToString(),
                        kvp => kvp.Value)
                },
                DateTimeOffset.UtcNow));

            return plan! with { OrchestratorExecution = executionResult };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Orchestrator failed for task {TaskId}", orchestrationTaskId);
            await FinalizeFailureAsync(execution, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AgentExecutionResult> ReviewAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        OrchestrationPlan plan,
        IReadOnlyList<AgentExecutionResult> results,
        CancellationToken cancellationToken = default)
    {
        var execution = await SetupExecutionAsync(
            orchestrationTaskId, userPrompt, cancellationToken, plan.OrchestratorExecution.SpanId)
            .ConfigureAwait(false);

        try
        {
            _eventBus.Publish(orchestrationTaskId, new SseEvent(
                "manager_review_started",
                orchestrationTaskId,
                new { taskId = orchestrationTaskId },
                DateTimeOffset.UtcNow));

            var agentOutputs = string.Join("\n", plan.ExecutionOrder.Zip(results, (agentType, result) =>
                $"[{agentType}]: {(result.Success ? result.Output : $"FAILED: {result.ErrorMessage}")}"));

            var reviewPrompt =
                $"""
                User's original task: {userPrompt}

                Agent outputs:
                {agentOutputs}

                Synthesize these outputs into the best possible final result for the user.
                """;

            var messages = new List<ConversationMessage> { new("user", reviewPrompt) };

            var (text, inputTokens, outputTokens, costUsd) =
                await RunLlmTurnAsync(execution, ReviewSystemPrompt, messages, 1, cancellationToken)
                    .ConfigureAwait(false);

            var executionResult = await FinalizeSuccessAsync(
                execution, text, inputTokens, outputTokens, costUsd, cancellationToken)
                .ConfigureAwait(false);

            _eventBus.Publish(orchestrationTaskId, new SseEvent(
                "manager_review_completed",
                orchestrationTaskId,
                new { taskId = orchestrationTaskId, result = text },
                DateTimeOffset.UtcNow));

            return executionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manager review failed for task {TaskId}", orchestrationTaskId);
            return await FinalizeFailureAsync(execution, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    // Thin wrapper — the parsing logic lives in Domain.Models.OrchestrationPlanParser so
    // Application-layer handlers (e.g. ResumeOrchestrationTaskHandler) can reconstruct a
    // plan from stored JSON without referencing Infrastructure.
    public static bool TryParseOrchestrationPlan(string json, out OrchestrationPlan? plan)
        => OrchestrationPlanParser.TryParse(json, out plan);
}
