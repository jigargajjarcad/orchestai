namespace OrchestAI.Infrastructure.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    public Dictionary<string, string> Models { get; init; } = new();
    public Dictionary<string, int> MaxTokens { get; init; } = new();
}
