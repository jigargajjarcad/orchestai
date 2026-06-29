using System.Text.Json;
using Anthropic.SDK.Messaging;
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

    public OrchestratorAgent(
        IAnthropicClientWrapper anthropicClient,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IOptions<Dictionary<string, PricingEntry>> pricingOptions,
        IToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
        : base(anthropicClient, agentExecutionRepository, agentMessageRepository,
               costLedgerRepository, mcpToolCallRepository, eventBus,
               agentOptions, pricingOptions, toolRegistry, loggerFactory)
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
            var messages = new List<Message>
            {
                new() { Role = RoleType.User, Content = [new TextContent { Text = userPrompt }] }
            };

            var (text, inputTokens, outputTokens, costUsd) =
                await RunLlmTurnAsync(execution, messages, 1, cancellationToken).ConfigureAwait(false);

            var totalInputTokens = inputTokens;
            var totalOutputTokens = outputTokens;
            var totalCostUsd = costUsd;

            if (!TryParseOrchestrationPlan(text, out var plan))
            {
                _logger.LogWarning(
                    "Orchestrator returned invalid JSON, retrying. ExecutionId: {ExecutionId}", execution.Id);

                messages.Add(new Message
                {
                    Role = RoleType.Assistant,
                    Content = [new TextContent { Text = text }]
                });
                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content = [new TextContent
                    {
                        Text = "Your previous response was not valid JSON. Return ONLY the JSON object, no other text."
                    }]
                });

                var (retryText, retryInput, retryOutput, retryCost) =
                    await RunLlmTurnAsync(execution, messages, 2, cancellationToken).ConfigureAwait(false);

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
            await FinalizeFailureAsync(execution, ex.Message, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public static bool TryParseOrchestrationPlan(string json, out OrchestrationPlan? plan)
    {
        plan = null;
        try
        {
            var cleaned = json.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                var start = cleaned.IndexOf('\n') + 1;
                var end = cleaned.LastIndexOf("```", StringComparison.Ordinal);
                if (end > start) cleaned = cleaned[start..end].Trim();
            }

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var planText = root.GetProperty("plan").GetString() ?? string.Empty;

            var executionMode = ExecutionMode.Parallel;
            if (root.TryGetProperty("execution_mode", out var modeEl))
            {
                var modeStr = modeEl.GetString();
                if (Enum.TryParse<ExecutionMode>(modeStr, ignoreCase: true, out var parsedMode))
                    executionMode = parsedMode;
            }

            var selectedAgents = new List<AgentType>();
            foreach (var agentEl in root.GetProperty("agents").EnumerateArray())
            {
                var agentStr = agentEl.GetString();
                if (Enum.TryParse<AgentType>(agentStr, ignoreCase: true, out var agentType))
                    selectedAgents.Add(agentType);
            }

            var executionOrder = new List<AgentType>();
            if (root.TryGetProperty("execution_order", out var orderEl))
            {
                foreach (var orderAgentEl in orderEl.EnumerateArray())
                {
                    var agentStr = orderAgentEl.GetString();
                    if (Enum.TryParse<AgentType>(agentStr, ignoreCase: true, out var agentType)
                        && selectedAgents.Contains(agentType))
                        executionOrder.Add(agentType);
                }
            }

            if (executionOrder.Count == 0)
                executionOrder.AddRange(selectedAgents);

            var agentPrompts = new Dictionary<AgentType, string>();
            if (root.TryGetProperty("agent_prompts", out var promptsEl))
            {
                foreach (var item in promptsEl.EnumerateObject())
                {
                    if (Enum.TryParse<AgentType>(item.Name, ignoreCase: true, out var agentType))
                        agentPrompts[agentType] = item.Value.GetString() ?? string.Empty;
                }
            }

            plan = new OrchestrationPlan(planText, executionMode, selectedAgents, executionOrder, agentPrompts, null!);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
