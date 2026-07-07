using OpenAI.Chat;

namespace OrchestAI.Infrastructure.Providers;

// Testing seam over the OpenAI/Azure SDK's ChatClient — mirrors IAnthropicClientWrapper.
// Accepts the bare model/deployment name per call since a single OpenAIClient or
// AzureOpenAIClient can serve multiple agents configured with different models.
public interface IChatCompletionClient
{
    Task<ChatCompletion> CompleteChatAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken = default);
}
