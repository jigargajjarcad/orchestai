using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace OrchestAI.Infrastructure.Agents.Base;

public sealed class AnthropicClientWrapper : IAnthropicClientWrapper
{
    private readonly AnthropicClient _client;

    public AnthropicClientWrapper(AnthropicClient client) => _client = client;

    public Task<MessageResponse> CreateMessageAsync(
        MessageParameters parameters,
        CancellationToken cancellationToken = default)
        => _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
}
