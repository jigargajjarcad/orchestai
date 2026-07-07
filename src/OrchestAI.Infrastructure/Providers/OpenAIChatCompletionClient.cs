using OpenAI;
using OpenAI.Chat;

namespace OrchestAI.Infrastructure.Providers;

public sealed class OpenAIChatCompletionClient : IChatCompletionClient
{
    private readonly OpenAIClient _client;

    public OpenAIChatCompletionClient(OpenAIClient client) => _client = client;

    public async Task<ChatCompletion> CompleteChatAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.GetChatClient(model)
            .CompleteChatAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);
        return result.Value;
    }
}
