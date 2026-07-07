namespace OrchestAI.Domain.Models;

public sealed record AgentTurn(
    string StopReason,      // "end_turn" | "tool_use" | "max_tokens"
    string Text,            // extracted text content, empty if tool_use
    IReadOnlyList<ToolRequest> ToolRequests,
    int InputTokens,
    int OutputTokens
);

public sealed record ToolRequest(
    string Id,              // tool call id for result correlation
    string Name,            // tool name e.g. "perplexity_search"
    string ArgsJson         // JSON string of arguments
);
