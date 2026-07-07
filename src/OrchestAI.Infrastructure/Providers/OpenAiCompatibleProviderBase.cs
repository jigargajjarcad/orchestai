using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Providers;

public abstract class OpenAiCompatibleProviderBase : ILlmProvider
{
    private readonly IChatCompletionClient _client;

    protected OpenAiCompatibleProviderBase(IChatCompletionClient client) => _client = client;

    public abstract string ProviderId { get; }

    public async Task<AgentTurn> SendAsync(
        AgentConversation conversation,
        CancellationToken cancellationToken = default)
    {
        var (messages, options) = OpenAiChatMapper.BuildRequest(conversation);
        var completion = await _client
            .CompleteChatAsync(conversation.Model, messages, options, cancellationToken)
            .ConfigureAwait(false);
        return OpenAiChatMapper.MapResponse(completion);
    }
}
