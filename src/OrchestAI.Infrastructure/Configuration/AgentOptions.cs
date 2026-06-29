namespace OrchestAI.Infrastructure.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agents";

    public Dictionary<string, string> Models { get; init; } = new();
    public Dictionary<string, int> MaxTokens { get; init; } = new();
}

public sealed class PricingEntry
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}
