namespace OrchestAI.Infrastructure.Configuration;

public sealed class PiiRedactionOptions
{
    public const string SectionName = "PiiRedaction";

    public bool Enabled { get; init; }
    public IReadOnlyList<PiiCustomRule> CustomRules { get; init; } = [];
}

public sealed class PiiCustomRule
{
    public string Pattern { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
}
