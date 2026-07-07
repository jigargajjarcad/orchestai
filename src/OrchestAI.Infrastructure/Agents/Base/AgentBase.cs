using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Agents.Base;

public abstract class AgentBase : IAgent
{
    protected abstract string SystemPrompt { get; }
    protected abstract AgentType AgentType { get; }
    protected virtual IReadOnlyList<string> AvailableToolNames => [];

    private const int MaxAgenticIterations = 10;

    protected readonly ILlmProviderFactory _llmProviderFactory;
    protected readonly IAgentExecutionRepository _agentExecutionRepository;
    protected readonly IAgentMessageRepository _agentMessageRepository;
    protected readonly ICostLedgerRepository _costLedgerRepository;
    protected readonly IMcpToolCallRepository _mcpToolCallRepository;
    protected readonly IOrchestrationEventBus _eventBus;
    protected readonly IOptions<AgentOptions> _agentOptions;
    protected readonly IOptions<Dictionary<string, PricingEntry>> _pricingOptions;
    protected readonly IToolRegistry _toolRegistry;
    protected readonly ILogger _logger;

    protected AgentBase(
        ILlmProviderFactory llmProviderFactory,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IOptions<Dictionary<string, PricingEntry>> pricingOptions,
        IToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
    {
        _llmProviderFactory = llmProviderFactory;
        _agentExecutionRepository = agentExecutionRepository;
        _agentMessageRepository = agentMessageRepository;
        _costLedgerRepository = costLedgerRepository;
        _mcpToolCallRepository = mcpToolCallRepository;
        _eventBus = eventBus;
        _agentOptions = agentOptions;
        _pricingOptions = pricingOptions;
        _toolRegistry = toolRegistry;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var execution = await SetupExecutionAsync(orchestrationTaskId, userPrompt, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var modelRef = ModelRef.Parse(_agentOptions.Value.Models[AgentType.ToString()]);
            var maxTokens = _agentOptions.Value.MaxTokens[AgentType.ToString()];
            var provider = _llmProviderFactory.Resolve(modelRef.ProviderId);

            var toolDefinitions = _toolRegistry.GetTools(AvailableToolNames)
                .Select(BuildToolDefinition)
                .ToList();

            var conversation = new AgentConversation(
                SystemPrompt,
                Messages: [new ConversationMessage("user", userPrompt)],
                Tools: toolDefinitions,
                Model: modelRef.ModelName,
                MaxTokens: maxTokens);

            var totalInputTokens = 0;
            var totalOutputTokens = 0;
            var totalCostUsd = 0m;
            var finalText = string.Empty;
            var sequenceNumber = 1;

            for (int iteration = 0; iteration < MaxAgenticIterations; iteration++)
            {
                var turn = await provider.SendAsync(conversation, cancellationToken).ConfigureAwait(false);

                totalInputTokens += turn.InputTokens;
                totalOutputTokens += turn.OutputTokens;
                totalCostUsd += CalculateCost(modelRef.ModelName, turn.InputTokens, turn.OutputTokens);

                if (turn.Text.Length > 0)
                {
                    finalText = turn.Text;
                    var agentMessage = AgentMessage.Create(
                        execution.Id, MessageRole.Assistant, finalText, sequenceNumber++);
                    await _agentMessageRepository.AddAsync(agentMessage, cancellationToken).ConfigureAwait(false);

                    _eventBus.Publish(orchestrationTaskId, new SseEvent(
                        "message_written",
                        orchestrationTaskId,
                        new
                        {
                            agentExecutionId = execution.Id,
                            agentType = AgentType.ToString(),
                            messageId = agentMessage.Id,
                            role = "Assistant",
                            contentPreview = finalText.Length > 200 ? finalText[..200] : finalText
                        },
                        DateTimeOffset.UtcNow));
                }

                if (turn.StopReason == "end_turn" || turn.StopReason == "max_tokens")
                    break;

                if (turn.StopReason == "tool_use" && turn.ToolRequests.Count > 0)
                {
                    var toolResults = new List<ToolResultContent>();

                    foreach (var request in turn.ToolRequests)
                    {
                        var result = await InvokeToolAsync(execution, request, cancellationToken)
                            .ConfigureAwait(false);

                        toolResults.Add(new ToolResultContent(
                            request.Id,
                            result.Success ? result.Output : $"Tool error: {result.ErrorMessage ?? "Unknown error"}",
                            !result.Success));
                    }

                    conversation = conversation.AppendToolResults(turn, toolResults);
                    continue;
                }

                break;
            }

            return await FinalizeSuccessAsync(
                execution, finalText, totalInputTokens, totalOutputTokens, totalCostUsd, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent {AgentType} execution {ExecutionId} failed", AgentType, execution.Id);
            return await FinalizeFailureAsync(execution, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    protected async Task<AgentExecution> SetupExecutionAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var execution = AgentExecution.Create(orchestrationTaskId, AgentType, userPrompt);
        await _agentExecutionRepository.AddAsync(execution, cancellationToken).ConfigureAwait(false);

        execution.Start();
        await _agentExecutionRepository.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(orchestrationTaskId, new SseEvent(
            "agent_started",
            orchestrationTaskId,
            new { agentExecutionId = execution.Id, agentType = AgentType.ToString(), status = "Running" },
            DateTimeOffset.UtcNow));

        _logger.LogInformation("Agent {AgentType} started execution {ExecutionId}", AgentType, execution.Id);
        return execution;
    }

    // Used by OrchestratorAgent for its JSON-routing and review flows (no tools, single-turn)
    protected async Task<(string Text, int InputTokens, int OutputTokens, decimal CostUsd)> RunLlmTurnAsync(
        AgentExecution execution,
        string systemPrompt,
        List<ConversationMessage> messages,
        int sequenceNumber,
        CancellationToken cancellationToken)
    {
        var modelRef = ModelRef.Parse(_agentOptions.Value.Models[AgentType.ToString()]);
        var maxTokens = _agentOptions.Value.MaxTokens[AgentType.ToString()];
        var provider = _llmProviderFactory.Resolve(modelRef.ProviderId);

        var conversation = new AgentConversation(systemPrompt, messages, Tools: [], modelRef.ModelName, maxTokens);
        var turn = await provider.SendAsync(conversation, cancellationToken).ConfigureAwait(false);

        var inputTokens = turn.InputTokens;
        var outputTokens = turn.OutputTokens;
        var costUsd = CalculateCost(modelRef.ModelName, inputTokens, outputTokens);

        var agentMessage = AgentMessage.Create(execution.Id, MessageRole.Assistant, turn.Text, sequenceNumber);
        await _agentMessageRepository.AddAsync(agentMessage, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(execution.OrchestrationTaskId, new SseEvent(
            "message_written",
            execution.OrchestrationTaskId,
            new
            {
                agentExecutionId = execution.Id,
                agentType = AgentType.ToString(),
                messageId = agentMessage.Id,
                role = "Assistant",
                contentPreview = turn.Text.Length > 200 ? turn.Text[..200] : turn.Text
            },
            DateTimeOffset.UtcNow));

        return (turn.Text, inputTokens, outputTokens, costUsd);
    }

    protected async Task<AgentExecutionResult> FinalizeSuccessAsync(
        AgentExecution execution,
        string output,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        CancellationToken cancellationToken)
    {
        execution.Complete(output, inputTokens, outputTokens, costUsd);
        await _agentExecutionRepository.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

        var ledger = CostLedger.Create(
            execution.OrchestrationTaskId,
            _agentOptions.Value.Models[AgentType.ToString()],
            inputTokens, outputTokens, costUsd,
            execution.Id);
        await _costLedgerRepository.AddAsync(ledger, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(execution.OrchestrationTaskId, new SseEvent(
            "agent_completed",
            execution.OrchestrationTaskId,
            new { agentExecutionId = execution.Id, agentType = AgentType.ToString(), inputTokens, outputTokens, costUsd },
            DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "Agent {AgentType} completed execution {ExecutionId}. Tokens: {In}+{Out}, Cost: ${Cost:F6}",
            AgentType, execution.Id, inputTokens, outputTokens, costUsd);

        return new AgentExecutionResult(execution.Id, output, true, inputTokens, outputTokens, costUsd);
    }

    protected async Task<AgentExecutionResult> FinalizeFailureAsync(
        AgentExecution execution,
        string error,
        CancellationToken cancellationToken)
    {
        execution.Fail(error);
        await _agentExecutionRepository.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(execution.OrchestrationTaskId, new SseEvent(
            "agent_failed",
            execution.OrchestrationTaskId,
            new { agentExecutionId = execution.Id, agentType = AgentType.ToString(), errorMessage = error },
            DateTimeOffset.UtcNow));

        return new AgentExecutionResult(execution.Id, string.Empty, false, 0, 0, 0m, error);
    }

    private async Task<McpToolResult> InvokeToolAsync(
        AgentExecution execution,
        ToolRequest request,
        CancellationToken cancellationToken)
    {
        var inputJson = string.IsNullOrWhiteSpace(request.ArgsJson) ? "{}" : request.ArgsJson;
        var parameters = ParseParameters(inputJson);

        _eventBus.Publish(execution.OrchestrationTaskId, new SseEvent(
            "tool_started",
            execution.OrchestrationTaskId,
            new
            {
                agentExecutionId = execution.Id,
                agentType = AgentType.ToString(),
                toolName = request.Name,
                inputParameters = inputJson
            },
            DateTimeOffset.UtcNow));

        _logger.LogInformation("Agent {AgentType} invoking tool '{ToolName}'", AgentType, request.Name);

        var sw = Stopwatch.StartNew();
        McpToolResult result;

        try
        {
            var tool = _toolRegistry.Get(request.Name);
            result = await tool.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new McpToolResult(false, string.Empty, ex.Message);
        }

        sw.Stop();
        var durationMs = (int)sw.ElapsedMilliseconds;

        var toolCall = McpToolCall.Create(execution.Id, request.Name, inputJson);
        if (result.Success)
            toolCall.RecordSuccess(result.Output, durationMs);
        else
            toolCall.RecordFailure(result.ErrorMessage ?? "Unknown error", durationMs);

        await _mcpToolCallRepository.AddAsync(toolCall, cancellationToken).ConfigureAwait(false);

        var outputPreview = result.Success
            ? (result.Output.Length > 200 ? result.Output[..200] : result.Output)
            : result.ErrorMessage;

        _eventBus.Publish(execution.OrchestrationTaskId, new SseEvent(
            "tool_completed",
            execution.OrchestrationTaskId,
            new
            {
                agentExecutionId = execution.Id,
                agentType = AgentType.ToString(),
                toolName = request.Name,
                success = result.Success,
                durationMs,
                outputPreview
            },
            DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "Tool '{ToolName}' completed in {DurationMs}ms, success={Success}",
            request.Name, durationMs, result.Success);

        return result;
    }

    private static Dictionary<string, string> ParseParameters(string inputJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? string.Empty
                    : prop.Value.GetRawText();
            }
        }
        catch { /* malformed input — return empty dict, tool will report missing params */ }
        return result;
    }

    private decimal CalculateCost(string model, int inputTokens, int outputTokens)
    {
        if (!_pricingOptions.Value.TryGetValue(model, out var pricing))
        {
            _logger.LogWarning("No pricing configured for model '{Model}', cost recorded as zero", model);
            return 0m;
        }

        return (inputTokens / 1_000_000m) * pricing.InputPerMillion
             + (outputTokens / 1_000_000m) * pricing.OutputPerMillion;
    }

    private static ToolDefinition BuildToolDefinition(IMcpTool tool)
    {
        var schema = tool.GetInputSchema();

        var propertiesObj = new JsonObject();
        foreach (var (propName, prop) in schema.Properties)
        {
            var propObj = new JsonObject { ["type"] = prop.Type };
            if (!string.IsNullOrEmpty(prop.Description))
                propObj["description"] = prop.Description;
            if (prop.Enum is { Length: > 0 })
            {
                var enumArr = new JsonArray();
                foreach (var e in prop.Enum) enumArr.Add(e);
                propObj["enum"] = enumArr;
            }
            propertiesObj[propName] = propObj;
        }

        var requiredArr = new JsonArray();
        foreach (var r in schema.Required) requiredArr.Add(r);

        var schemaNode = new JsonObject
        {
            ["type"] = schema.Type,
            ["properties"] = propertiesObj,
            ["required"] = requiredArr
        };

        return new ToolDefinition(tool.ToolName, tool.Description, schemaNode.ToJsonString());
    }
}
