using FluentAssertions;
using Moq;
using OpenAI.Chat;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Providers;

namespace OrchestAI.Tests.Infrastructure;

public sealed class OpenAiCompatibleProviderTests
{
    [Fact]
    public void OpenAIProvider_ProviderId_IsOpenai()
    {
        new OpenAIProvider(new Mock<IChatCompletionClient>().Object).ProviderId.Should().Be("openai");
    }

    [Fact]
    public void AzureOpenAIProvider_ProviderId_IsAzure()
    {
        new AzureOpenAIProvider(new Mock<IChatCompletionClient>().Object).ProviderId.Should().Be("azure");
    }

    [Fact]
    public async Task SendAsync_MapsConversationAndResponse()
    {
        IEnumerable<ChatMessage>? capturedMessages = null;
        ChatCompletionOptions? capturedOptions = null;
        string? capturedModel = null;

        var clientMock = new Mock<IChatCompletionClient>();
        clientMock
            .Setup(c => c.CompleteChatAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<ChatMessage>, ChatCompletionOptions, CancellationToken>(
                (model, messages, options, _) =>
                {
                    capturedModel = model;
                    capturedMessages = messages.ToList();
                    capturedOptions = options;
                })
            .ReturnsAsync(OpenAIChatModelFactory.ChatCompletion(
                finishReason: ChatFinishReason.Stop,
                content: new ChatMessageContent("Hello there."),
                usage: OpenAIChatModelFactory.ChatTokenUsage(outputTokenCount: 24, inputTokenCount: 42)));

        var provider = new OpenAIProvider(clientMock.Object);

        var conversation = new AgentConversation(
            "System prompt",
            [new ConversationMessage("user", "Hi")],
            [new ToolDefinition("my_tool", "does a thing", """{"type":"object","properties":{},"required":[]}""")],
            "gpt-4o-mini",
            512);

        var turn = await provider.SendAsync(conversation, CancellationToken.None);

        turn.StopReason.Should().Be("end_turn");
        turn.Text.Should().Be("Hello there.");
        turn.InputTokens.Should().Be(42);
        turn.OutputTokens.Should().Be(24);

        capturedModel.Should().Be("gpt-4o-mini");
        capturedMessages.Should().HaveCount(2); // system + user
        capturedOptions!.Tools.Should().ContainSingle(t => t.FunctionName == "my_tool");
        capturedOptions.MaxOutputTokenCount.Should().Be(512);
    }

    [Fact]
    public async Task SendAsync_ToolCallsFinishReason_MapsToToolUseWithRequests()
    {
        var clientMock = new Mock<IChatCompletionClient>();
        clientMock
            .Setup(c => c.CompleteChatAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenAIChatModelFactory.ChatCompletion(
                finishReason: ChatFinishReason.ToolCalls,
                content: new ChatMessageContent(string.Empty),
                toolCalls: [ChatToolCall.CreateFunctionToolCall("call_1", "perplexity_search", BinaryData.FromString("""{"query":"test"}"""))],
                usage: OpenAIChatModelFactory.ChatTokenUsage(outputTokenCount: 5, inputTokenCount: 10)));

        var provider = new AzureOpenAIProvider(clientMock.Object);
        var conversation = new AgentConversation("sys", [new ConversationMessage("user", "hi")], [], "gpt-4o", 100);

        var turn = await provider.SendAsync(conversation, CancellationToken.None);

        turn.StopReason.Should().Be("tool_use");
        turn.ToolRequests.Should().ContainSingle();
        turn.ToolRequests[0].Name.Should().Be("perplexity_search");
        turn.ToolRequests[0].Id.Should().Be("call_1");
        turn.ToolRequests[0].ArgsJson.Should().Contain("test");
    }

    [Fact]
    public async Task SendAsync_ToolResultTurn_FlattensEachResultToItsOwnToolMessage()
    {
        IEnumerable<ChatMessage>? capturedMessages = null;

        var clientMock = new Mock<IChatCompletionClient>();
        clientMock
            .Setup(c => c.CompleteChatAsync(
                It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatCompletionOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<ChatMessage>, ChatCompletionOptions, CancellationToken>(
                (_, messages, _, _) => capturedMessages = messages.ToList())
            .ReturnsAsync(OpenAIChatModelFactory.ChatCompletion(
                finishReason: ChatFinishReason.Stop,
                content: new ChatMessageContent("ok"),
                usage: OpenAIChatModelFactory.ChatTokenUsage(outputTokenCount: 1, inputTokenCount: 1)));

        var provider = new OpenAIProvider(clientMock.Object);

        var turn = new AgentTurn("tool_use", "", [new ToolRequest("call_1", "my_tool", "{}")], 5, 5);
        var conversation = new AgentConversation("sys", [new ConversationMessage("user", "hi")], [], "gpt-4o", 100)
            .AppendToolResults(turn, [new ToolResultContent("call_1", "tool output", false)]);

        await provider.SendAsync(conversation, CancellationToken.None);

        capturedMessages.Should().HaveCount(4); // system, user, assistant(tool call), tool(result)
        var toolMessage = capturedMessages!.OfType<ToolChatMessage>().Single();
        toolMessage.ToolCallId.Should().Be("call_1");
    }
}
