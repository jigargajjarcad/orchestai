using System.Text.Json.Nodes;
using Anthropic.SDK.Messaging;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents.Base;
using CommonFunction = Anthropic.SDK.Common.Function;
using CommonTool = Anthropic.SDK.Common.Tool;
using DomainToolResultContent = OrchestAI.Domain.Models.ToolResultContent;

namespace OrchestAI.Infrastructure.Providers;

// The ADR-001 ACL: the only place in the codebase allowed to see Anthropic.SDK types.
public sealed class AnthropicProvider : ILlmProvider
{
    public string ProviderId => "anthropic";

    private readonly IAnthropicClientWrapper _client;

    public AnthropicProvider(IAnthropicClientWrapper client) => _client = client;

    public async Task<AgentTurn> SendAsync(
        AgentConversation conversation,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.CreateMessageAsync(
            new MessageParameters
            {
                Model = conversation.Model,
                MaxTokens = conversation.MaxTokens,
                System = [new SystemMessage(conversation.SystemPrompt)],
                Messages = conversation.Messages.Select(BuildMessage).ToList(),
                Stream = false,
                Tools = BuildTools(conversation.Tools)
            }, cancellationToken).ConfigureAwait(false);

        var text = response.Content.OfType<TextContent>().LastOrDefault()?.Text ?? string.Empty;

        var toolRequests = (response.ToolCalls ?? [])
            .Select(f => new ToolRequest(f.Id, f.Name, f.Arguments?.ToJsonString() ?? "{}"))
            .ToList();

        return new AgentTurn(
            response.StopReason ?? "end_turn",
            text,
            toolRequests,
            response.Usage.InputTokens,
            response.Usage.OutputTokens);
    }

    private static Message BuildMessage(ConversationMessage message)
    {
        if (message.Role == "user" && message.ToolResults is { Count: > 0 })
        {
            return new Message
            {
                Role = RoleType.User,
                Content = message.ToolResults.Select((DomainToolResultContent tr) => new Anthropic.SDK.Messaging.ToolResultContent
                {
                    ToolUseId = tr.ToolCallId,
                    Content = [new TextContent { Text = tr.Content }],
                    IsError = tr.IsError
                }).Cast<ContentBase>().ToList()
            };
        }

        if (message.Role == "assistant")
        {
            var content = new List<ContentBase>();
            if (!string.IsNullOrEmpty(message.TextContent))
                content.Add(new TextContent { Text = message.TextContent });

            if (message.ToolRequests is { Count: > 0 })
                content.AddRange(message.ToolRequests.Select(req => new ToolUseContent
                {
                    Id = req.Id,
                    Name = req.Name,
                    Input = JsonNode.Parse(string.IsNullOrWhiteSpace(req.ArgsJson) ? "{}" : req.ArgsJson)
                }));

            return new Message { Role = RoleType.Assistant, Content = content };
        }

        return new Message
        {
            Role = RoleType.User,
            Content = [new TextContent { Text = message.TextContent ?? string.Empty }]
        };
    }

    private static IList<CommonTool>? BuildTools(IReadOnlyList<ToolDefinition> tools)
    {
        if (tools.Count == 0) return null;

        return tools.Select(tool => new CommonTool(new CommonFunction(
            tool.Name,
            tool.Description,
            JsonNode.Parse(tool.InputSchemaJson)))).ToList();
    }
}
