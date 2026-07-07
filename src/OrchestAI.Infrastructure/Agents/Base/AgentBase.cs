using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Exceptions;
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
    private const int MaxMemoryEntries = 5;
    private const int MemoryExtractionInputCharLimit = 4000;
    private const int MemoryExtractionMaxTokens = 512;

    private const string MemoryExtractionSystemPrompt =
        """
        Extract 1-3 key facts from this agent output worth remembering for future tasks.
        Return ONLY valid JSON array, no markdown:
        [
          {"key": "short_identifier", "value": "the fact to remember", "importance": 7}
        ]
        Importance 1-10. Only extract genuinely useful facts. Return [] if nothing worth remembering.
        """;

    protected readonly ILlmProviderFactory _llmProviderFactory;
    protected readonly IAgentExecutionRepository _agentExecutionRepository;
    protected readonly IAgentMessageRepository _agentMessageRepository;
    protected readonly ICostLedgerRepository _costLedgerRepository;
    protected readonly IMcpToolCallRepository _mcpToolCallRepository;
    protected readonly ITaskCheckpointRepository _checkpointRepository;
    protected readonly IAgentMemoryRepository _memoryRepository;
    protected readonly IPiiRedactor _piiRedactor;
    protected readonly IOrchestrationEventBus _eventBus;
    protected readonly IOptions<AgentOptions> _agentOptions;
    protected readonly IOptions<Dictionary<string, PricingEntry>> _pricingOptions;
    protected readonly IOptions<RetryPolicyOptions> _retryOptions;
    protected readonly IToolRegistry _toolRegistry;
    protected readonly ILogger _logger;

    protected AgentBase(
        ILlmProviderFactory llmProviderFactory,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        ITaskCheckpointRepository checkpointRepository,
        IAgentMemoryRepository memoryRepository,
        IPiiRedactor piiRedactor,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IOptions<Dictionary<string, PricingEntry>> pricingOptions,
        IOptions<RetryPolicyOptions> retryOptions,
        IToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
    {
        _llmProviderFactory = llmProviderFactory;
        _agentExecutionRepository = agentExecutionRepository;
        _agentMessageRepository = agentMessageRepository;
        _costLedgerRepository = costLedgerRepository;
        _mcpToolCallRepository = mcpToolCallRepository;
        _checkpointRepository = checkpointRepository;
        _memoryRepository = memoryRepository;
        _piiRedactor = piiRedactor;
        _eventBus = eventBus;
        _agentOptions = agentOptions;
        _pricingOptions = pricingOptions;
        _retryOptions = retryOptions;
        _toolRegistry = toolRegistry;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        Guid orchestrationTaskId,
        Guid userId,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var safePrompt = RedactIfEnabled(userPrompt);

        var execution = await SetupExecutionAsync(orchestrationTaskId, safePrompt, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var modelRef = ModelRef.Parse(_agentOptions.Value.Models[AgentType.ToString()]);
            var maxTokens = _agentOptions.Value.MaxTokens[AgentType.ToString()];
            var provider = _llmProviderFactory.Resolve(modelRef.ProviderId);

            var toolDefinitions = _toolRegistry.GetTools(AvailableToolNames)
                .Select(BuildToolDefinition)
                .ToList();

            var systemPrompt = await BuildSystemPromptWithMemoryAsync(userId, cancellationToken)
                .ConfigureAwait(false);

            var conversation = new AgentConversation(
                systemPrompt,
                Messages: [new ConversationMessage("user", safePrompt)],
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
                var turn = await SendWithRetryAsync(provider, conversation, orchestrationTaskId, cancellationToken)
                    .ConfigureAwait(false);

                totalInputTokens += turn.InputTokens;
                totalOutputTokens += turn.OutputTokens;
                totalCostUsd += CalculateCost(modelRef.ModelName, turn.InputTokens, turn.OutputTokens);

                if (turn.Text.Length > 0)
                {
                    finalText = RedactIfEnabled(turn.Text);
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

            var executionResult = await FinalizeSuccessAsync(
                execution, finalText, totalInputTokens, totalOutputTokens, totalCostUsd, cancellationToken)
                .ConfigureAwait(false);

            await SaveCheckpointAsync(
                orchestrationTaskId, execution.Id, finalText, totalInputTokens, totalOutputTokens, totalCostUsd,
                cancellationToken).ConfigureAwait(false);

            await ExtractAndStoreMemoryAsync(
                userId, provider, modelRef, finalText, cancellationToken).ConfigureAwait(false);

            return executionResult;
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

        var safeMessages = messages
            .Select(m => m.TextContent is null ? m : m with { TextContent = RedactIfEnabled(m.TextContent) })
            .ToList();

        var conversation = new AgentConversation(systemPrompt, safeMessages, Tools: [], modelRef.ModelName, maxTokens);
        var turn = await SendWithRetryAsync(provider, conversation, execution.OrchestrationTaskId, cancellationToken)
            .ConfigureAwait(false);

        var inputTokens = turn.InputTokens;
        var outputTokens = turn.OutputTokens;
        var costUsd = CalculateCost(modelRef.ModelName, inputTokens, outputTokens);
        var safeText = RedactIfEnabled(turn.Text);

        var agentMessage = AgentMessage.Create(execution.Id, MessageRole.Assistant, safeText, sequenceNumber);
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
                contentPreview = safeText.Length > 200 ? safeText[..200] : safeText
            },
            DateTimeOffset.UtcNow));

        return (safeText, inputTokens, outputTokens, costUsd);
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

    // ── Checkpointing ──────────────────────────────────────────────────────
    // Only called from the sub-agent ExecuteAsync loop — Orchestrator plan/review
    // turns (RunLlmTurnAsync) are not resumable pipeline steps, so they're not checkpointed.

    private async Task SaveCheckpointAsync(
        Guid orchestrationTaskId,
        Guid agentExecutionId,
        string output,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        CancellationToken cancellationToken)
    {
        try
        {
            var checkpoint = TaskCheckpoint.Create(
                orchestrationTaskId, AgentType, agentExecutionId, output, inputTokens, outputTokens, costUsd);
            await _checkpointRepository.UpsertAsync(checkpoint, cancellationToken).ConfigureAwait(false);

            _eventBus.Publish(orchestrationTaskId, new SseEvent(
                "checkpoint_saved",
                orchestrationTaskId,
                new { taskId = orchestrationTaskId, agentType = AgentType.ToString(), agentExecutionId },
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            // Checkpointing is a resilience optimization, not a correctness requirement —
            // losing one just means a future resume can't skip this agent.
            _logger.LogWarning(ex, "Failed to save checkpoint for agent {AgentType}, continuing", AgentType);
        }
    }

    // ── Agent memory ───────────────────────────────────────────────────────

    private async Task<string> BuildSystemPromptWithMemoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentMemory> memories;
        try
        {
            memories = await _memoryRepository
                .GetRelevantAsync(userId, AgentType, MaxMemoryEntries, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load memory for agent {AgentType}, continuing without it", AgentType);
            return SystemPrompt;
        }

        if (memories.Count == 0)
            return SystemPrompt;

        // The memory-augmented prompt is only ever sent to the LLM, never persisted
        // (AgentExecution.InputPrompt holds the task instruction, not agent-internal
        // context) — this log line is the only record of which memories were used.
        _logger.LogInformation(
            "Agent {AgentType} injecting {Count} memories for user {UserId}: {Keys}",
            AgentType, memories.Count, userId, string.Join(", ", memories.Select(m => m.Key)));

        var memorySection = string.Join("\n", memories.Select(m => $"- {m.Key}: {m.Value}"));

        return $"""
            {SystemPrompt}

            --- MEMORY FROM PREVIOUS INTERACTIONS ---
            {memorySection}
            --- END MEMORY ---
            """;
    }

    // Best-effort — a lightweight second LLM call. Never fails agent execution.
    private async Task ExtractAndStoreMemoryAsync(
        Guid userId,
        ILlmProvider provider,
        ModelRef modelRef,
        string agentOutput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentOutput))
            return;

        try
        {
            var truncated = agentOutput.Length > MemoryExtractionInputCharLimit
                ? agentOutput[..MemoryExtractionInputCharLimit]
                : agentOutput;

            var conversation = new AgentConversation(
                MemoryExtractionSystemPrompt,
                Messages: [new ConversationMessage("user", truncated)],
                Tools: [],
                Model: modelRef.ModelName,
                MaxTokens: MemoryExtractionMaxTokens);

            var turn = await provider.SendAsync(conversation, cancellationToken).ConfigureAwait(false);
            var extracted = ParseMemoryExtractions(turn.Text);

            foreach (var item in extracted)
            {
                var memory = AgentMemory.Create(userId, AgentType, item.Key, item.Value, item.Importance);
                await _memoryRepository.UpsertAsync(memory, cancellationToken).ConfigureAwait(false);
            }

            if (extracted.Count > 0)
                _logger.LogInformation(
                    "Agent {AgentType} extracted {Count} memories for user {UserId}",
                    AgentType, extracted.Count, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory extraction failed for agent {AgentType}, continuing", AgentType);
        }
    }

    private static IReadOnlyList<(string Key, string Value, int Importance)> ParseMemoryExtractions(string json)
    {
        var cleaned = json.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var start = cleaned.IndexOf('\n') + 1;
            var end = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) cleaned = cleaned[start..end].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<(string, string, int)>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("key", out var keyEl) || !item.TryGetProperty("value", out var valueEl))
                    continue;

                var key = keyEl.GetString();
                var value = valueEl.GetString();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                var importance = item.TryGetProperty("importance", out var impEl) && impEl.TryGetInt32(out var imp)
                    ? imp
                    : 5;

                results.Add((key, value, importance));
            }
            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ── PII redaction ──────────────────────────────────────────────────────

    private string RedactIfEnabled(string text)
    {
        if (!_piiRedactor.IsEnabled || string.IsNullOrEmpty(text))
            return text;

        var redacted = _piiRedactor.Redact(text, out var matchCount);
        if (matchCount > 0)
            _logger.LogDebug("PII redacted in {AgentType} input: {Count} patterns matched", AgentType, matchCount);

        return redacted;
    }

    // ── Retry with exponential backoff ────────────────────────────────────

    private async Task<AgentTurn> SendWithRetryAsync(
        ILlmProvider provider,
        AgentConversation conversation,
        Guid orchestrationTaskId,
        CancellationToken cancellationToken)
    {
        var policy = _retryOptions.Value;

        for (int attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            try
            {
                return await provider.SendAsync(conversation, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!IsTransient(ex, cancellationToken))
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= policy.MaxAttempts)
                    throw new AgentExecutionException(
                        $"LLM call failed after {policy.MaxAttempts} attempts", ex);

                var delay = CalculateDelay(attempt, policy);
                _logger.LogWarning(
                    "LLM call attempt {Attempt} failed (transient) for agent {AgentType}: {Error}. Retrying in {Delay}ms",
                    attempt, AgentType, ex.Message, delay);

                _eventBus.Publish(orchestrationTaskId, new SseEvent(
                    "agent_retry",
                    orchestrationTaskId,
                    new { agentType = AgentType.ToString(), attempt, delayMs = delay, reason = ex.Message },
                    DateTimeOffset.UtcNow));

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new AgentExecutionException($"LLM call failed after {policy.MaxAttempts} attempts");
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        TaskCanceledException when cancellationToken.IsCancellationRequested => false,
        HttpRequestException http => (int?)http.StatusCode is 429 or 500 or 502 or 503 or 504,
        TaskCanceledException => true,
        TimeoutException => true,
        _ when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => true,
        _ when ex.Message.Contains("overloaded", StringComparison.OrdinalIgnoreCase) => true,
        _ => false
    };

    private static int CalculateDelay(int attempt, RetryPolicyOptions policy)
    {
        var exponential = (int)(policy.InitialDelayMs * Math.Pow(policy.BackoffMultiplier, attempt - 1));
        var capped = Math.Min(exponential, policy.MaxDelayMs);
        var jitter = Random.Shared.Next(-policy.JitterMs, policy.JitterMs);
        return Math.Max(100, capped + jitter);
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
