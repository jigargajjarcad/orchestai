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
    protected readonly IAgentRetryAttemptRepository _agentRetryAttemptRepository;
    protected readonly IPiiRedactor _piiRedactor;
    protected readonly IOrchestrationEventBus _eventBus;
    protected readonly IOptions<AgentOptions> _agentOptions;
    protected readonly IModelPricingCache _modelPricingCache;
    protected readonly IOptions<RetryPolicyOptions> _retryOptions;
    protected readonly IToolRegistry _toolRegistry;
    protected readonly ITaskToolCallBudget _taskToolCallBudget;
    protected readonly IRejectionEventRepository _rejectionEventRepository;
    protected readonly ILogger _logger;

    protected AgentBase(
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
    {
        _llmProviderFactory = llmProviderFactory;
        _agentExecutionRepository = agentExecutionRepository;
        _agentMessageRepository = agentMessageRepository;
        _costLedgerRepository = costLedgerRepository;
        _mcpToolCallRepository = mcpToolCallRepository;
        _checkpointRepository = checkpointRepository;
        _memoryRepository = memoryRepository;
        _agentRetryAttemptRepository = agentRetryAttemptRepository;
        _piiRedactor = piiRedactor;
        _eventBus = eventBus;
        _agentOptions = agentOptions;
        _modelPricingCache = modelPricingCache;
        _retryOptions = retryOptions;
        _toolRegistry = toolRegistry;
        _taskToolCallBudget = taskToolCallBudget;
        _rejectionEventRepository = rejectionEventRepository;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        Guid orchestrationTaskId,
        Guid userId,
        string userPrompt,
        CancellationToken cancellationToken = default,
        string? parentSpanId = null,
        Guid? evalRunId = null)
    {
        var safePrompt = RedactIfEnabled(userPrompt);

        var execution = await SetupExecutionAsync(
            orchestrationTaskId, safePrompt, cancellationToken, parentSpanId, evalRunId)
            .ConfigureAwait(false);

        try
        {
            var modelRef = ModelRef.Parse(_agentOptions.Value.Models[AgentType.ToString()]);
            var maxTokens = _agentOptions.Value.MaxTokens[AgentType.ToString()];
            var provider = _llmProviderFactory.Resolve(modelRef.ProviderId);

            var toolDefinitions = _toolRegistry.GetTools(AvailableToolNames)
                .Select(BuildToolDefinition)
                .ToList();

            var systemPrompt = await BuildSystemPromptWithMemoryAsync(execution, userId, cancellationToken)
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
            var reachedFinalTurn = false;

            for (int iteration = 0; iteration < MaxAgenticIterations; iteration++)
            {
                var turn = await SendWithRetryAsync(provider, conversation, execution, cancellationToken)
                    .ConfigureAwait(false);

                totalInputTokens += turn.InputTokens;
                totalOutputTokens += turn.OutputTokens;
                totalCostUsd += await CalculateCostAsync(
                    modelRef.ModelName, turn.InputTokens, turn.OutputTokens, cancellationToken)
                    .ConfigureAwait(false);

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
                {
                    reachedFinalTurn = true;
                    break;
                }

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

                reachedFinalTurn = true;
                break;
            }

            // The loop above exhausted MaxAgenticIterations without ever reaching end_turn/
            // max_tokens (or the unexpected-stop-reason fallback) — the last thing that happened
            // was a tool_use turn's `continue`, with no iteration left to send those tool
            // results back for a real synthesis turn. `finalText` at this point is at best stale
            // (whatever aside the model attached to that unresolved tool_use turn) and must never
            // be reported as a successful answer. Reject cleanly instead — same
            // RejectionEvent/AgentCapExceededException mechanism InvokeToolAsync already uses for
            // MaxToolCallsPerTask, per ADR-015 Confirmation #7's reject-vs-truncate principle.
            if (!reachedFinalTurn)
            {
                var detailsJson = $$"""{"limit":{{MaxAgenticIterations}}}""";
                try
                {
                    var rejectionEvent = RejectionEvent.Create(
                        RejectionReason.AgentCapExceeded, requestId: null, traceId: execution.SpanId, apiKeyId: null,
                        detailsJson: detailsJson);
                    await _rejectionEventRepository.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to persist RejectionEvent for AgentIterationCapExceeded, execution {ExecutionId}",
                        execution.Id);
                }

                throw new AgentCapExceededException(
                    $"Agent {AgentType} exceeded its maximum of {MaxAgenticIterations} tool-use " +
                    "iterations without reaching a final answer.");
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
            return await FinalizeFailureAsync(execution, ex, cancellationToken).ConfigureAwait(false);
        }
    }

    protected async Task<AgentExecution> SetupExecutionAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        CancellationToken cancellationToken,
        string? parentSpanId = null,
        Guid? evalRunId = null)
    {
        var execution = AgentExecution.Create(orchestrationTaskId, AgentType, userPrompt, parentSpanId, evalRunId);
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
        var turn = await SendWithRetryAsync(provider, conversation, execution, cancellationToken)
            .ConfigureAwait(false);

        var inputTokens = turn.InputTokens;
        var outputTokens = turn.OutputTokens;
        var costUsd = await CalculateCostAsync(modelRef.ModelName, inputTokens, outputTokens, cancellationToken)
            .ConfigureAwait(false);
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
            execution.Id,
            source: execution.EvalRunId is null ? CostSource.Production : CostSource.Eval,
            evalRunId: execution.EvalRunId);
        await _costLedgerRepository.AddAsync(ledger, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(execution.OrchestrationTaskId, new SseEvent(
            "agent_completed",
            execution.OrchestrationTaskId,
            new { agentExecutionId = execution.Id, agentType = AgentType.ToString(), inputTokens, outputTokens, costUsd },
            DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "Agent {AgentType} completed execution {ExecutionId}. Tokens: {In}+{Out}, Cost: ${Cost:F6}",
            AgentType, execution.Id, inputTokens, outputTokens, costUsd);

        return new AgentExecutionResult(
            execution.Id, output, true, inputTokens, outputTokens, costUsd, SpanId: execution.SpanId);
    }

    protected async Task<AgentExecutionResult> FinalizeFailureAsync(
        AgentExecution execution,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var error = exception.Message;
        var category = ErrorClassifier.Classify(exception);
        execution.Fail(error, category);
        await _agentExecutionRepository.UpdateAsync(execution, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(execution.OrchestrationTaskId, new SseEvent(
            "agent_failed",
            execution.OrchestrationTaskId,
            new
            {
                agentExecutionId = execution.Id,
                agentType = AgentType.ToString(),
                errorMessage = error,
                errorCategory = category.ToString()
            },
            DateTimeOffset.UtcNow));

        return new AgentExecutionResult(
            execution.Id, string.Empty, false, 0, 0, 0m, error, SpanId: execution.SpanId);
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

    private async Task<string> BuildSystemPromptWithMemoryAsync(
        AgentExecution execution, Guid userId, CancellationToken cancellationToken)
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

        execution.SetMemoriesInjected(memories.Count);

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
        AgentExecution execution,
        CancellationToken cancellationToken)
    {
        var policy = _retryOptions.Value;
        var orchestrationTaskId = execution.OrchestrationTaskId;

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

                var retryAttempt = AgentRetryAttempt.Create(execution.Id, attempt, delay, ex.Message);
                await _agentRetryAttemptRepository.AddAsync(retryAttempt, cancellationToken).ConfigureAwait(false);

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
        var budgetCheck = _taskToolCallBudget.TryIncrement();
        if (!budgetCheck.Allowed)
        {
            var detailsJson = $$"""{"limit":{{budgetCheck.MaxToolCalls}},"actual":{{budgetCheck.CurrentCount}}}""";
            try
            {
                var rejectionEvent = RejectionEvent.Create(
                    RejectionReason.AgentCapExceeded, requestId: null, traceId: execution.SpanId, apiKeyId: null,
                    detailsJson: detailsJson);
                await _rejectionEventRepository.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to persist RejectionEvent for AgentCapExceeded, execution {ExecutionId}", execution.Id);
            }

            throw new AgentCapExceededException(
                $"Task tool-call cap exceeded: {budgetCheck.CurrentCount} calls attempted, limit is {budgetCheck.MaxToolCalls}.");
        }

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
        ExecutionErrorCategory? exceptionCategory = null;

        try
        {
            var tool = _toolRegistry.Get(request.Name);
            result = await tool.ExecuteAsync(parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new McpToolResult(false, string.Empty, ex.Message);
            exceptionCategory = ErrorClassifier.Classify(ex);
        }

        sw.Stop();
        var durationMs = (int)sw.ElapsedMilliseconds;

        var toolCall = McpToolCall.Create(execution.Id, request.Name, inputJson, execution.SpanId);
        if (result.Success)
            toolCall.RecordSuccess(result.Output, durationMs);
        else
            toolCall.RecordFailure(
                result.ErrorMessage ?? "Unknown error", durationMs,
                exceptionCategory ?? ExecutionErrorCategory.McpToolError);

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

    private async Task<decimal> CalculateCostAsync(
        string model, int inputTokens, int outputTokens, CancellationToken cancellationToken)
    {
        var pricing = await _modelPricingCache.GetAsync(model, cancellationToken).ConfigureAwait(false);
        if (pricing is null)
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
