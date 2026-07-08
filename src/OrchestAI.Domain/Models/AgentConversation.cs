namespace OrchestAI.Domain.Models;

public sealed record AgentConversation(
    string SystemPrompt,
    IReadOnlyList<ConversationMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    string Model,
    int MaxTokens,
    double? Temperature = null)
{
    // Appends the assistant's tool-requesting turn plus the resulting tool outputs in one step.
    // ToolRequests must travel with the assistant message so OpenAI/Azure providers can
    // reconstruct the tool_call_id correlation their APIs require on the next turn.
    public AgentConversation AppendToolResults(AgentTurn turn, IReadOnlyList<ToolResultContent> results) => this with
    {
        Messages =
        [
            .. Messages,
            new ConversationMessage(
                "assistant",
                turn.Text.Length > 0 ? turn.Text : null,
                ToolRequests: turn.ToolRequests.Count > 0 ? turn.ToolRequests : null),
            new ConversationMessage("user", null, ToolResults: results)
        ]
    };
}

public sealed record ConversationMessage(
    string Role,            // "user" | "assistant"
    string? TextContent,
    IReadOnlyList<ToolRequest>? ToolRequests = null,
    IReadOnlyList<ToolResultContent>? ToolResults = null
);

public sealed record ToolResultContent(
    string ToolCallId,
    string Content,
    bool IsError
);

public sealed record ToolDefinition(
    string Name,
    string Description,
    string InputSchemaJson  // JSON Schema as string
);
