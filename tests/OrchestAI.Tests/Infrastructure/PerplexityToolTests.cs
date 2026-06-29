using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Tools;

namespace OrchestAI.Tests.Infrastructure;

public sealed class PerplexityToolTests
{
    private static PerplexityTool BuildTool(
        string apiKey,
        Func<HttpRequestMessage, HttpResponseMessage> httpHandler)
    {
        var fakeHandler = new FakeHttpMessageHandler(httpHandler);
        var client = new HttpClient(fakeHandler);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var options = Options.Create(new ToolOptions
        {
            Perplexity = new PerplexityOptions { ApiKey = apiKey, BaseUrl = "https://api.perplexity.ai" }
        });

        return new PerplexityTool(httpFactory.Object, options, NullLogger<PerplexityTool>.Instance);
    }

    [Fact]
    public void ToolName_IsPerplexitySearch()
    {
        var tool = BuildTool("key", _ => new HttpResponseMessage(HttpStatusCode.OK));
        tool.ToolName.Should().Be("perplexity_search");
    }

    [Fact]
    public void GetInputSchema_RequiresQuery()
    {
        var tool = BuildTool("key", _ => new HttpResponseMessage(HttpStatusCode.OK));
        var schema = tool.GetInputSchema();
        schema.Required.Should().Contain("query");
        schema.Properties.Should().ContainKey("query");
    }

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ReturnsFailure()
    {
        var tool = BuildTool("key", _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("query");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyApiKey_ReturnsFailure()
    {
        var tool = BuildTool(string.Empty, _ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "What is LangGraph?"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("API key not configured");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessResponse_ReturnsAnswer()
    {
        const string answer = "LangGraph is a library for building stateful, multi-actor applications.";
        var responseJson = $$"""
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{{answer}}"
                  }
                }
              ]
            }
            """;

        var tool = BuildTool("test-key", _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "What is LangGraph?"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be(answer);
    }

    [Fact]
    public async Task ExecuteAsync_HttpError_ReturnsFailure()
    {
        var tool = BuildTool("test-key", _ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"error":"Rate limited"}""", Encoding.UTF8, "application/json")
            });

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "What is LangGraph?"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("429");
    }

    [Fact]
    public async Task ExecuteAsync_HttpException_ReturnsFailure()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Throws(new HttpRequestException("Connection refused"));

        var options = Options.Create(new ToolOptions
        {
            Perplexity = new PerplexityOptions { ApiKey = "test-key", BaseUrl = "https://api.perplexity.ai" }
        });
        var tool = new PerplexityTool(factory.Object, options, NullLogger<PerplexityTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "What is LangGraph?"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Perplexity error");
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
