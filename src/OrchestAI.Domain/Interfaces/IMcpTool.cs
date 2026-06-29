using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IMcpTool
{
    string ToolName { get; }
    string Description { get; }
    ToolInputSchema GetInputSchema();

    Task<McpToolResult> ExecuteAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}

public record McpToolResult(
    bool Success,
    string Output,
    string? ErrorMessage = null,
    int DurationMs = 0
);
