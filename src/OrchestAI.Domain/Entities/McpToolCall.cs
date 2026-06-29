namespace OrchestAI.Domain.Entities;

public sealed class McpToolCall
{
    private McpToolCall() { }

    public Guid Id { get; private set; }
    public Guid AgentExecutionId { get; private set; }
    public string ToolName { get; private set; } = string.Empty;
    public string InputParameters { get; private set; } = "{}";
    public string? OutputResult { get; private set; }
    public bool Success { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? DurationMs { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public AgentExecution AgentExecution { get; private set; } = null!;

    public static McpToolCall Create(
        Guid agentExecutionId,
        string toolName,
        string inputParameters)
    {
        return new McpToolCall
        {
            Id = Guid.NewGuid(),
            AgentExecutionId = agentExecutionId,
            ToolName = toolName,
            InputParameters = inputParameters,
            Success = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void RecordSuccess(string output, int durationMs)
    {
        Success = true;
        OutputResult = output;
        DurationMs = durationMs;
    }

    public void RecordFailure(string error, int durationMs)
    {
        Success = false;
        ErrorMessage = error;
        DurationMs = durationMs;
    }
}
