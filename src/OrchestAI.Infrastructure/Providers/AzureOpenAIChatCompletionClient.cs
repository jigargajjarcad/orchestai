using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace OrchestAI.Infrastructure.Providers;

public sealed class AzureOpenAIChatCompletionClient : IChatCompletionClient
{
    private readonly AzureOpenAIClient _client;
    private readonly string _deploymentName;

    public AzureOpenAIChatCompletionClient(AzureOpenAIClient client, string deploymentName)
    {
        _client = client;
        _deploymentName = deploymentName;
    }

    // Azure OpenAI addresses models by deployment name, not the bare model id used in
    // Agents:Models config (e.g. "azure/gpt-4o") — the single configured DeploymentName
    // is always used regardless of the model segment passed here.
    public async Task<ChatCompletion> CompleteChatAsync(
        string model,
        IEnumerable<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = await _client.GetChatClient(_deploymentName)
            .CompleteChatAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);
        return result.Value;
    }
}
