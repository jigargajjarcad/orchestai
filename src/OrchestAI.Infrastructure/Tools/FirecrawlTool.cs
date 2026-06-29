using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Tools;

public sealed class FirecrawlTool : IMcpTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ToolOptions> _options;
    private readonly ILogger<FirecrawlTool> _logger;

    public string ToolName => "firecrawl_scrape";
    public string Description => "Scrape and extract clean content from any URL. Use when you need the full text content of a specific webpage.";

    public FirecrawlTool(
        IHttpClientFactory httpClientFactory,
        IOptions<ToolOptions> options,
        ILogger<FirecrawlTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public ToolInputSchema GetInputSchema() => new(
        Type: "object",
        Properties: new Dictionary<string, ToolProperty>
        {
            ["url"] = new("string", "The URL to scrape"),
            ["formats"] = new("string", "Output format, e.g. 'markdown'")
        },
        Required: ["url"]
    );

    public async Task<McpToolResult> ExecuteAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            return new McpToolResult(false, string.Empty, "Missing required parameter: url");

        var opts = _options.Value.Firecrawl;
        if (string.IsNullOrEmpty(opts.ApiKey))
        {
            _logger.LogWarning("Firecrawl API key not configured");
            return new McpToolResult(false, string.Empty, "Firecrawl API key not configured");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("firecrawl");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(30);

            var body = JsonSerializer.Serialize(new { url, formats = new[] { "markdown" } });
            using var request = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await client
                .PostAsync($"{opts.BaseUrl}/scrape", request, cancellationToken)
                .ConfigureAwait(false);

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Firecrawl returned {StatusCode}: {Body}", (int)response.StatusCode, content[..Math.Min(200, content.Length)]);
                return new McpToolResult(false, string.Empty,
                    $"Firecrawl error: {(int)response.StatusCode} {content[..Math.Min(500, content.Length)]}");
            }

            using var doc = JsonDocument.Parse(content);
            var markdown = doc.RootElement
                .GetProperty("data")
                .GetProperty("markdown")
                .GetString() ?? string.Empty;

            _logger.LogInformation("Firecrawl scraped {Url}, got {Chars} chars", url, markdown.Length);
            return new McpToolResult(true, markdown);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new McpToolResult(false, string.Empty, "Firecrawl request timed out after 30 seconds");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firecrawl tool error for URL {Url}", url);
            return new McpToolResult(false, string.Empty, $"Firecrawl error: {ex.Message}");
        }
    }
}
