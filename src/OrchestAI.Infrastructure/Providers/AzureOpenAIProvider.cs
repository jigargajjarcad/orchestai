namespace OrchestAI.Infrastructure.Providers;

public sealed class AzureOpenAIProvider : OpenAiCompatibleProviderBase
{
    public override string ProviderId => "azure";

    public AzureOpenAIProvider(IChatCompletionClient client) : base(client) { }
}
