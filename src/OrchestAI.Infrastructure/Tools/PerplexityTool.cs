using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Tools;

public sealed class PerplexityTool : IMcpTool
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ToolOptions> _options;
    private readonly ILogger<PerplexityTool> _logger;

    public string ToolName => "perplexity_search";
    public string Description => "AI-powered research with cited answers. Use when you need synthesized information about a topic with source citations.";

    public PerplexityTool(
        IHttpClientFactory httpClientFactory,
        IOptions<ToolOptions> options,
        ILogger<PerplexityTool> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public ToolInputSchema GetInputSchema() => new(
        Type: "object",
        Properties: new Dictionary<string, ToolProperty>
        {
            ["query"] = new("string", "The research query to answer with citations")
        },
        Required: ["query"]
    );

    public async Task<McpToolResult> ExecuteAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            return new McpToolResult(false, string.Empty, "Missing required parameter: query");

        var opts = _options.Value.Perplexity;
        if (string.IsNullOrEmpty(opts.ApiKey))
        {
            _logger.LogWarning("Perplexity API key not configured");
            return new McpToolResult(false, string.Empty, "Perplexity API key not configured");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("perplexity");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", opts.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(30);

            var body = JsonSerializer.Serialize(new
            {
                model = "sonar",
                messages = new[] { new { role = "user", content = query } }
            });
            using var request = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await client
                .PostAsync($"{opts.BaseUrl}/chat/completions", request, cancellationToken)
                .ConfigureAwait(false);

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Perplexity returned {StatusCode}: {Body}", (int)response.StatusCode, content[..Math.Min(200, content.Length)]);
                return new McpToolResult(false, string.Empty,
                    $"Perplexity error: {(int)response.StatusCode} {content[..Math.Min(500, content.Length)]}");
            }

            using var doc = JsonDocument.Parse(content);
            var answer = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            _logger.LogInformation("Perplexity answered query '{Query}', got {Chars} chars", query[..Math.Min(50, query.Length)], answer.Length);
            return new McpToolResult(true, answer);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new McpToolResult(false, string.Empty, "Perplexity request timed out after 30 seconds");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Perplexity tool error for query '{Query}'", query[..Math.Min(50, query.Length)]);
            return new McpToolResult(false, string.Empty, $"Perplexity error: {ex.Message}");
        }
    }
}
