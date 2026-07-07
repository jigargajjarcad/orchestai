using System.Text.Json.Nodes;
using Anthropic.SDK.Messaging;
using FluentAssertions;
using Moq;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Providers;
using CommonFunction = Anthropic.SDK.Common.Function;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AnthropicProviderTests
{
    [Fact]
    public void ProviderId_IsAnthropic()
    {
        new AnthropicProvider(new Mock<IAnthropicClientWrapper>().Object).ProviderId.Should().Be("anthropic");
    }

    [Fact]
    public async Task SendAsync_MapsConversationToMessageParametersAndBack()
    {
        var clientMock = new Mock<IAnthropicClientWrapper>();
        MessageParameters? captured = null;
        clientMock
            .Setup(c => c.CreateMessageAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .Callback<MessageParameters, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new MessageResponse
            {
                StopReason = "end_turn",
                Content = [new TextContent { Text = "Hello there." }],
                ToolCalls = [],
                Usage = new Usage { InputTokens = 42, OutputTokens = 24 }
            });

        var provider = new AnthropicProvider(clientMock.Object);

        var conversation = new AgentConversation(
            "System prompt",
            [new ConversationMessage("user", "Hi")],
            [new ToolDefinition("my_tool", "does a thing", """{"type":"object","properties":{},"required":[]}""")],
            "claude-haiku-4-5-20251001",
            1024);

        var turn = await provider.SendAsync(conversation, CancellationToken.None);

        turn.StopReason.Should().Be("end_turn");
        turn.Text.Should().Be("Hello there.");
        turn.InputTokens.Should().Be(42);
        turn.OutputTokens.Should().Be(24);

        captured.Should().NotBeNull();
        captured!.Model.Should().Be("claude-haiku-4-5-20251001");
        captured.MaxTokens.Should().Be(1024);
        captured.Tools.Should().ContainSingle();
        captured.Tools![0].Function.Name.Should().Be("my_tool");
    }

    [Fact]
    public async Task SendAsync_ToolUseResponse_MapsToolCallsToToolRequests()
    {
        var clientMock = new Mock<IAnthropicClientWrapper>();
        clientMock
            .Setup(c => c.CreateMessageAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageResponse
            {
                StopReason = "tool_use",
                Content = [],
                ToolCalls = [MakeToolCallFunction("perplexity_search", "call_1", """{"query":"test"}""")],
                Usage = new Usage { InputTokens = 10, OutputTokens = 5 }
            });

        var provider = new AnthropicProvider(clientMock.Object);
        var conversation = new AgentConversation("sys", [new ConversationMessage("user", "hi")], [], "model", 100);

        var turn = await provider.SendAsync(conversation, CancellationToken.None);

        turn.StopReason.Should().Be("tool_use");
        turn.ToolRequests.Should().ContainSingle();
        turn.ToolRequests[0].Name.Should().Be("perplexity_search");
        turn.ToolRequests[0].Id.Should().Be("call_1");
        turn.ToolRequests[0].ArgsJson.Should().Contain("test");
    }

    [Fact]
    public async Task SendAsync_ToolResultTurn_BuildsToolResultMessage()
    {
        var clientMock = new Mock<IAnthropicClientWrapper>();
        MessageParameters? captured = null;
        clientMock
            .Setup(c => c.CreateMessageAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .Callback<MessageParameters, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new MessageResponse
            {
                StopReason = "end_turn",
                Content = [new TextContent { Text = "ok" }],
                ToolCalls = [],
                Usage = new Usage { InputTokens = 1, OutputTokens = 1 }
            });

        var provider = new AnthropicProvider(clientMock.Object);

        var turn = new AgentTurn("tool_use", "", [new ToolRequest("call_1", "my_tool", "{}")], 5, 5);
        var conversation = new AgentConversation("sys", [new ConversationMessage("user", "hi")], [], "model", 100)
            .AppendToolResults(turn, [new OrchestAI.Domain.Models.ToolResultContent("call_1", "tool output", false)]);

        await provider.SendAsync(conversation, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Messages.Should().HaveCount(3); // user, assistant(tool_use), user(tool_result)
        var toolResultMessage = captured.Messages[2];
        toolResultMessage.Role.Should().Be(RoleType.User);
        var toolResultContent = toolResultMessage.Content.OfType<Anthropic.SDK.Messaging.ToolResultContent>().Single();
        toolResultContent.ToolUseId.Should().Be("call_1");
    }

    // CommonFunction.Id/Arguments have private setters — reflection stays confined to this
    // provider-boundary test, matching the ADR-001 rationale for isolating SDK plumbing.
    private static CommonFunction MakeToolCallFunction(string name, string id, string argsJson)
    {
        var func = new CommonFunction(name, "test tool", (JsonNode?)null);
        var funcType = typeof(CommonFunction);
        funcType.GetProperty("Id")!.GetSetMethod(nonPublic: true)!.Invoke(func, [id]);
        funcType.GetProperty("Arguments")!.GetSetMethod(nonPublic: true)!.Invoke(func, [JsonNode.Parse(argsJson)]);
        return func;
    }
}
