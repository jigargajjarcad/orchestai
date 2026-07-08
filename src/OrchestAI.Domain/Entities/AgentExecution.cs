using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Entities;

public sealed class AgentExecution
{
    private AgentExecution() { }

    public Guid Id { get; private set; }
    public Guid OrchestrationTaskId { get; private set; }
    public AgentType AgentType { get; private set; }
    public ExecutionStatus Status { get; private set; }
    public string InputPrompt { get; private set; } = string.Empty;
    public string? OutputResult { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public string? ErrorMessage { get; private set; }
    public ExecutionErrorCategory? ErrorCategory { get; private set; }
    public string SpanId { get; private set; } = string.Empty;
    public string? ParentSpanId { get; private set; }
    public Guid? EvalRunId { get; private set; }
    public int MemoriesInjectedCount { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public OrchestrationTask OrchestrationTask { get; private set; } = null!;
    private readonly List<AgentMessage> _messages = [];
    public IReadOnlyCollection<AgentMessage> Messages => _messages.AsReadOnly();
    private readonly List<McpToolCall> _toolCalls = [];
    public IReadOnlyCollection<McpToolCall> ToolCalls => _toolCalls.AsReadOnly();

    public static AgentExecution Create(
        Guid taskId, AgentType agentType, string inputPrompt, string? parentSpanId = null,
        Guid? evalRunId = null)
    {
        return new AgentExecution
        {
            Id = Guid.NewGuid(),
            OrchestrationTaskId = taskId,
            AgentType = agentType,
            Status = ExecutionStatus.Pending,
            InputPrompt = inputPrompt,
            SpanId = TraceIdentifiers.NewSpanId(),
            ParentSpanId = parentSpanId,
            EvalRunId = evalRunId,
            InputTokens = 0,
            OutputTokens = 0,
            CostUsd = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Start()
    {
        Status = ExecutionStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(string result, int inputTokens, int outputTokens, decimal costUsd)
    {
        Status = ExecutionStatus.Completed;
        OutputResult = result;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string error, ExecutionErrorCategory category = ExecutionErrorCategory.Unknown)
    {
        Status = ExecutionStatus.Failed;
        ErrorMessage = error;
        ErrorCategory = category;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void SetMemoriesInjected(int count)
    {
        MemoriesInjectedCount = count;
    }
}
