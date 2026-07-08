using OpenAI.Chat;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Providers;

// Shared request/response mapping for the OpenAI and Azure OpenAI providers — both
// use the same OpenAI.Chat.ChatClient/ChatCompletion types under the hood.
internal static class OpenAiChatMapper
{
    public static (IEnumerable<ChatMessage> Messages, ChatCompletionOptions Options) BuildRequest(
        AgentConversation conversation)
    {
        var messages = new List<ChatMessage> { ChatMessage.CreateSystemMessage(conversation.SystemPrompt) };
        messages.AddRange(conversation.Messages.SelectMany(BuildMessages));

        var options = new ChatCompletionOptions { MaxOutputTokenCount = conversation.MaxTokens };
        if (conversation.Temperature.HasValue)
            options.Temperature = (float)conversation.Temperature.Value;
        foreach (var tool in conversation.Tools)
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name, tool.Description, BinaryData.FromString(tool.InputSchemaJson)));

        return (messages, options);
    }

    public static AgentTurn MapResponse(ChatCompletion completion)
    {
        var text = string.Concat(completion.Content
            .Where(part => part.Kind == ChatMessageContentPartKind.Text)
            .Select(part => part.Text));

        var toolRequests = completion.ToolCalls
            .Select(tc => new ToolRequest(tc.Id, tc.FunctionName, tc.FunctionArguments?.ToString() ?? "{}"))
            .ToList();

        return new AgentTurn(
            MapFinishReason(completion.FinishReason),
            text,
            toolRequests,
            completion.Usage?.InputTokenCount ?? 0,
            completion.Usage?.OutputTokenCount ?? 0);
    }

    private static string MapFinishReason(ChatFinishReason reason) => reason switch
    {
        ChatFinishReason.ToolCalls => "tool_use",
        ChatFinishReason.Length => "max_tokens",
        _ => "end_turn"
    };

    private static IEnumerable<ChatMessage> BuildMessages(ConversationMessage message)
    {
        if (message.Role == "user" && message.ToolResults is { Count: > 0 })
        {
            foreach (var result in message.ToolResults)
                yield return ChatMessage.CreateToolMessage(result.ToolCallId, result.Content);
            yield break;
        }

        if (message.Role == "assistant")
        {
            var assistantMessage = new AssistantChatMessage(message.TextContent ?? string.Empty);
            if (message.ToolRequests is { Count: > 0 })
            {
                foreach (var request in message.ToolRequests)
                    assistantMessage.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                        request.Id, request.Name, BinaryData.FromString(request.ArgsJson)));
            }
            yield return assistantMessage;
            yield break;
        }

        yield return ChatMessage.CreateUserMessage(message.TextContent ?? string.Empty);
    }
}
