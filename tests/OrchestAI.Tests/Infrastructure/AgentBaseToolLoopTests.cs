using System.Text.Json.Nodes;
using Anthropic.SDK.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;
using CommonFunction = Anthropic.SDK.Common.Function;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AgentBaseToolLoopTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private const string ModelName = "claude-haiku-4-5-20251001";

    private readonly Mock<IAnthropicClientWrapper> _clientMock;
    private readonly Mock<IAgentExecutionRepository> _execRepoMock;
    private readonly Mock<IAgentMessageRepository> _msgRepoMock;
    private readonly Mock<ICostLedgerRepository> _costRepoMock;
    private readonly Mock<IMcpToolCallRepository> _toolCallRepoMock;
    private readonly Mock<IOrchestrationEventBus> _eventBusMock;
    private readonly Mock<IToolRegistry> _toolRegistryMock;
    private readonly IOptions<AgentOptions> _agentOptions;
    private readonly IOptions<Dictionary<string, PricingEntry>> _pricingOptions;

    public AgentBaseToolLoopTests()
    {
        _clientMock = new Mock<IAnthropicClientWrapper>();
        _execRepoMock = new Mock<IAgentExecutionRepository>();
        _msgRepoMock = new Mock<IAgentMessageRepository>();
        _costRepoMock = new Mock<ICostLedgerRepository>();
        _toolCallRepoMock = new Mock<IMcpToolCallRepository>();
        _eventBusMock = new Mock<IOrchestrationEventBus>();
        _toolRegistryMock = new Mock<IToolRegistry>();

        _agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Code"] = ModelName },
            MaxTokens = new Dictionary<string, int> { ["Code"] = 1024 }
        });
        _pricingOptions = Options.Create(new Dictionary<string, PricingEntry>
        {
            [ModelName] = new PricingEntry { InputPerMillion = 0.80m, OutputPerMillion = 4.00m }
        });

        _execRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _execRepoMock.Setup(r => r.UpdateAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _msgRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _costRepoMock.Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _toolCallRepoMock.Setup(r => r.AddAsync(It.IsAny<McpToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private TestAgent BuildAgent()
    {
        return new TestAgent(
            _clientMock.Object,
            _execRepoMock.Object,
            _msgRepoMock.Object,
            _costRepoMock.Object,
            _toolCallRepoMock.Object,
            _eventBusMock.Object,
            _agentOptions,
            _pricingOptions,
            _toolRegistryMock.Object,
            NullLoggerFactory.Instance);
    }

    private static MessageResponse MakeEndTurnResponse(string text = "Final answer")
    {
        return new MessageResponse
        {
            StopReason = "end_turn",
            Content = [new TextContent { Text = text }],
            ToolCalls = [],
            Usage = new Usage { InputTokens = 100, OutputTokens = 50 }
        };
    }

    private static MessageResponse MakeToolUseResponse(string toolName, string toolId, string argsJson = "{}")
    {
        var func = MakeToolCallFunction(toolName, toolId, argsJson);
        return new MessageResponse
        {
            StopReason = "tool_use",
            Content = [],
            ToolCalls = [func],
            Usage = new Usage { InputTokens = 80, OutputTokens = 40 }
        };
    }

    // CommonFunction.Name, Id, and Arguments have private setters set during SDK deserialization.
    // We use reflection to construct valid tool call instances for testing.
    private static CommonFunction MakeToolCallFunction(string name, string id, string argsJson = "{}")
    {
        var func = new CommonFunction(name, "test tool", (JsonNode?)null);
        var funcType = typeof(CommonFunction);
        funcType.GetProperty("Id")!.GetSetMethod(nonPublic: true)!.Invoke(func, [id]);
        funcType.GetProperty("Arguments")!.GetSetMethod(nonPublic: true)!.Invoke(func, [JsonNode.Parse(argsJson)]);
        return func;
    }

    [Fact]
    public async Task ExecuteAsync_EndTurnOnFirstCall_ReturnsSuccessWithText()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);

        _clientMock.Setup(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEndTurnResponse("Task completed successfully."));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, "Do something", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Task completed successfully.");
        result.InputTokens.Should().Be(100);
        result.OutputTokens.Should().Be(50);
    }

    [Fact]
    public async Task ExecuteAsync_EndTurnOnFirstCall_PublishesAgentStartedAndCompleted()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);
        _clientMock.Setup(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEndTurnResponse());

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, "Do something", CancellationToken.None);

        _eventBusMock.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "agent_started")),
            Times.Once);
        _eventBusMock.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "agent_completed")),
            Times.Once);
        _eventBusMock.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "message_written")),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ToolUseThenEndTurn_InvokesToolAndContinuesLoop()
    {
        const string toolName = "test_tool";
        const string toolId = "call_abc123";

        var mockTool = new Mock<IMcpTool>();
        mockTool.Setup(t => t.ToolName).Returns(toolName);
        mockTool.Setup(t => t.Description).Returns("A test tool");
        mockTool.Setup(t => t.GetInputSchema()).Returns(new ToolInputSchema(
            "object",
            new Dictionary<string, ToolProperty>(),
            []));
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult(true, "tool output here"));

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>()))
            .Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName))
            .Returns(mockTool.Object);

        _clientMock.SetupSequence(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeToolUseResponse(toolName, toolId, @"{""key"":""value""}"))
            .ReturnsAsync(MakeEndTurnResponse("Done after tool use."));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, "Use a tool", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Done after tool use.");

        // LLM was called twice
        _clientMock.Verify(
            c => c.CreateMessageAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Tool was invoked once
        mockTool.Verify(
            t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ToolUseThenEndTurn_PublishesToolStartedAndCompleted()
    {
        const string toolName = "test_tool";

        var mockTool = new Mock<IMcpTool>();
        mockTool.Setup(t => t.ToolName).Returns(toolName);
        mockTool.Setup(t => t.Description).Returns("A test tool");
        mockTool.Setup(t => t.GetInputSchema()).Returns(new ToolInputSchema("object", new Dictionary<string, ToolProperty>(), []));
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult(true, "success output"));

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>()))
            .Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName)).Returns(mockTool.Object);

        _clientMock.SetupSequence(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeToolUseResponse(toolName, "call_xyz", "{}"))
            .ReturnsAsync(MakeEndTurnResponse("Done."));

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, "Use a tool", CancellationToken.None);

        _eventBusMock.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "tool_started")),
            Times.Once);
        _eventBusMock.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "tool_completed")),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ToolFailure_RecordsFailureAndContinues()
    {
        const string toolName = "test_tool";

        var mockTool = new Mock<IMcpTool>();
        mockTool.Setup(t => t.ToolName).Returns(toolName);
        mockTool.Setup(t => t.Description).Returns("A test tool");
        mockTool.Setup(t => t.GetInputSchema()).Returns(new ToolInputSchema("object", new Dictionary<string, ToolProperty>(), []));
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult(false, string.Empty, "External API down"));

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>()))
            .Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName)).Returns(mockTool.Object);

        _clientMock.SetupSequence(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeToolUseResponse(toolName, "call_fail", "{}"))
            .ReturnsAsync(MakeEndTurnResponse("Recovered."));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, "Try tool", CancellationToken.None);

        // Even with tool failure, agent should complete successfully
        result.Success.Should().BeTrue();

        // Tool call should still be persisted
        _toolCallRepoMock.Verify(
            r => r.AddAsync(It.IsAny<McpToolCall>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GetToolsCalledWithAgentToolNames()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);
        _clientMock.Setup(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeEndTurnResponse());

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, "Do something", CancellationToken.None);

        _toolRegistryMock.Verify(
            r => r.GetTools(It.Is<IReadOnlyList<string>>(names =>
                names.Contains("test_tool"))),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AnthropicClientThrows_ReturnsFailure()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);
        _clientMock.Setup(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Anthropic API unreachable"));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, "Do something", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Anthropic API unreachable");
    }

    [Fact]
    public async Task ExecuteAsync_AccumulatesTokensAndCostAcrossIterations()
    {
        const string toolName = "test_tool";

        var mockTool = new Mock<IMcpTool>();
        mockTool.Setup(t => t.ToolName).Returns(toolName);
        mockTool.Setup(t => t.Description).Returns("A test tool");
        mockTool.Setup(t => t.GetInputSchema()).Returns(new ToolInputSchema("object", new Dictionary<string, ToolProperty>(), []));
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult(true, "ok"));

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName)).Returns(mockTool.Object);

        _clientMock.SetupSequence(c => c.CreateMessageAsync(
                It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageResponse
            {
                StopReason = "tool_use",
                Content = [],
                ToolCalls = [MakeToolCallFunction(toolName, "id1", "{}")],
                Usage = new Usage { InputTokens = 200, OutputTokens = 100 }
            })
            .ReturnsAsync(new MessageResponse
            {
                StopReason = "end_turn",
                Content = [new TextContent { Text = "Done" }],
                ToolCalls = [],
                Usage = new Usage { InputTokens = 150, OutputTokens = 75 }
            });

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, "Test", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.InputTokens.Should().Be(350);  // 200 + 150
        result.OutputTokens.Should().Be(175); // 100 + 75
        result.CostUsd.Should().BeGreaterThan(0m);
    }

    // Concrete subclass of AgentBase used for testing
    private sealed class TestAgent : AgentBase
    {
        protected override string SystemPrompt => "You are a test agent.";
        protected override AgentType AgentType => AgentType.Code;
        protected override IReadOnlyList<string> AvailableToolNames => ["test_tool"];

        public TestAgent(
            IAnthropicClientWrapper client,
            IAgentExecutionRepository execRepo,
            IAgentMessageRepository msgRepo,
            ICostLedgerRepository costRepo,
            IMcpToolCallRepository toolCallRepo,
            IOrchestrationEventBus eventBus,
            IOptions<AgentOptions> agentOptions,
            IOptions<Dictionary<string, PricingEntry>> pricingOptions,
            IToolRegistry toolRegistry,
            ILoggerFactory loggerFactory)
            : base(client, execRepo, msgRepo, costRepo, toolCallRepo, eventBus,
                   agentOptions, pricingOptions, toolRegistry, loggerFactory)
        { }
    }
}
