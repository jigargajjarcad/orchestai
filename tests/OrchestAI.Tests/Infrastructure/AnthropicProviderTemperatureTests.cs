using FluentAssertions;
using Moq;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Providers;
using Anthropic.SDK.Messaging;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AnthropicProviderTemperatureTests
{
    [Fact]
    public async Task SendAsync_ConversationHasTemperature_ForwardsItToTheClient()
    {
        var clientMock = new Mock<IAnthropicClientWrapper>();
        MessageParameters? captured = null;
        clientMock
            .Setup(c => c.CreateMessageAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .Callback<MessageParameters, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new MessageResponse
            {
                Content = [new TextContent { Text = "0.42" }],
                StopReason = "end_turn",
                Usage = new Usage { InputTokens = 1, OutputTokens = 1 }
            });

        var provider = new AnthropicProvider(clientMock.Object);
        var conversation = new AgentConversation(
            "system", Messages: [new ConversationMessage("user", "hi")], Tools: [],
            Model: "claude-haiku-4-5-20251001", MaxTokens: 10, Temperature: 0.0);

        await provider.SendAsync(conversation, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Temperature.Should().Be(0.0m);
    }
}
