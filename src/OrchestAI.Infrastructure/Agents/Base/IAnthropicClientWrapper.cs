using Anthropic.SDK.Messaging;

namespace OrchestAI.Infrastructure.Agents.Base;

public interface IAnthropicClientWrapper
{
    Task<MessageResponse> CreateMessageAsync(
        MessageParameters parameters,
        CancellationToken cancellationToken = default);
}
