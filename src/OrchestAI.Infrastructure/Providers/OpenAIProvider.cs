namespace OrchestAI.Infrastructure.Providers;

public sealed class OpenAIProvider : OpenAiCompatibleProviderBase
{
    public override string ProviderId => "openai";

    public OpenAIProvider(IChatCompletionClient client) : base(client) { }
}
