namespace OrchestAI.Infrastructure.Configuration;

public sealed class ToolOptions
{
    public const string SectionName = "Tools";

    public FirecrawlOptions Firecrawl { get; init; } = new();
    public PerplexityOptions Perplexity { get; init; } = new();
    public FileSystemOptions FileSystem { get; init; } = new();
}

public sealed class FirecrawlOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.firecrawl.dev/v1";
}

public sealed class PerplexityOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.perplexity.ai";
}

public sealed class FileSystemOptions
{
    public string SandboxPath { get; init; } = "./agent-workspace";
}
