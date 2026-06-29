using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Tools;

namespace OrchestAI.Tests.Infrastructure;

public sealed class FirecrawlToolTests
{
    private static FirecrawlTool BuildTool(
        string apiKey,
        Func<HttpRequestMessage, HttpResponseMessage> httpHandler)
    {
        var fakeHandler = new FakeHttpMessageHandler(httpHandler);
        var client = new HttpClient(fakeHandler);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var options = Options.Create(new ToolOptions
        {
            Firecrawl = new FirecrawlOptions { ApiKey = apiKey, BaseUrl = "https://api.firecrawl.dev/v1" }
        });

        return new FirecrawlTool(httpFactory.Object, options, NullLogger<FirecrawlTool>.Instance);
    }

    [Fact]
    public void ToolName_IsFirecrawlScrape()
    {
        var tool = BuildTool("key", _ => new HttpResponseMessage(HttpStatusCode.OK));
        tool.ToolName.Should().Be("firecrawl_scrape");
    }

    [Fact]
    public void GetInputSchema_RequiresUrl()
    {
        var tool = BuildTool("key", _ => new HttpResponseMessage(HttpStatusCode.OK));
        var schema = tool.GetInputSchema();
        schema.Required.Should().Contain("url");
        schema.Properties.Should().ContainKey("url");
    }

    [Fact]
    public async Task ExecuteAsync_MissingUrl_ReturnsFailure()
    {
        var tool = BuildTool("key", _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("url");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyApiKey_ReturnsFailure()
    {
        var tool = BuildTool(string.Empty, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["url"] = "https://example.com"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("API key not configured");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessResponse_ReturnsMarkdown()
    {
        const string markdown = "# Title Some scraped content.";
        var responseJson = System.Text.Json.JsonSerializer.Serialize(
            new { data = new { markdown } });

        var tool = BuildTool("test-key", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["url"] = "https://example.com"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be(markdown);
    }

    [Fact]
    public async Task ExecuteAsync_HttpError_ReturnsFailure()
    {
        var tool = BuildTool("test-key", _ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"error":"Invalid API key"}""", Encoding.UTF8, "application/json")
            });

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["url"] = "https://example.com"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("401");
    }

    [Fact]
    public async Task ExecuteAsync_HttpException_ReturnsFailure()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Throws(new HttpRequestException("Network failure"));

        var options = Options.Create(new ToolOptions
        {
            Firecrawl = new FirecrawlOptions { ApiKey = "test-key", BaseUrl = "https://api.firecrawl.dev/v1" }
        });
        var tool = new FirecrawlTool(factory.Object, options, NullLogger<FirecrawlTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["url"] = "https://example.com"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Firecrawl error");
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
