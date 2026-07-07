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

namespace OrchestAI.Tests.Infrastructure;

public sealed class AgentBaseProviderTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string ModelName = "claude-haiku-4-5-20251001";

    private readonly Mock<ILlmProvider> _providerMock;
    private readonly Mock<ILlmProviderFactory> _providerFactoryMock;
    private readonly Mock<IAgentExecutionRepository> _execRepoMock;
    private readonly Mock<IAgentMessageRepository> _msgRepoMock;
    private readonly Mock<ICostLedgerRepository> _costRepoMock;
    private readonly Mock<IMcpToolCallRepository> _toolCallRepoMock;
    private readonly Mock<ITaskCheckpointRepository> _checkpointRepoMock;
    private readonly Mock<IAgentMemoryRepository> _memoryRepoMock;
    private readonly Mock<IPiiRedactor> _piiRedactorMock;
    private readonly Mock<IOrchestrationEventBus> _eventBusMock;
    private readonly Mock<IToolRegistry> _toolRegistryMock;
    private readonly IOptions<AgentOptions> _agentOptions;
    private readonly IOptions<Dictionary<string, PricingEntry>> _pricingOptions;
    private readonly IOptions<RetryPolicyOptions> _retryOptions;

    public AgentBaseProviderTests()
    {
        _providerMock = new Mock<ILlmProvider>();
        _providerMock.Setup(p => p.ProviderId).Returns("anthropic");

        _providerFactoryMock = new Mock<ILlmProviderFactory>();
        _providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(_providerMock.Object);

        _execRepoMock = new Mock<IAgentExecutionRepository>();
        _msgRepoMock = new Mock<IAgentMessageRepository>();
        _costRepoMock = new Mock<ICostLedgerRepository>();
        _toolCallRepoMock = new Mock<IMcpToolCallRepository>();
        _checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        _memoryRepoMock = new Mock<IAgentMemoryRepository>();
        _piiRedactorMock = new Mock<IPiiRedactor>();
        _eventBusMock = new Mock<IOrchestrationEventBus>();
        _toolRegistryMock = new Mock<IToolRegistry>();

        _agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Code"] = $"anthropic/{ModelName}" },
            MaxTokens = new Dictionary<string, int> { ["Code"] = 1024 }
        });
        _pricingOptions = Options.Create(new Dictionary<string, PricingEntry>
        {
            [ModelName] = new PricingEntry { InputPerMillion = 0.80m, OutputPerMillion = 4.00m }
        });
        _retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = 3, InitialDelayMs = 1, MaxDelayMs = 5, BackoffMultiplier = 2.0, JitterMs = 1
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
        _checkpointRepoMock.Setup(r => r.UpsertAsync(It.IsAny<TaskCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _memoryRepoMock
            .Setup(r => r.GetRelevantAsync(It.IsAny<Guid>(), It.IsAny<AgentType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _piiRedactorMock.Setup(r => r.IsEnabled).Returns(false);
    }

    private TestAgent BuildAgent()
    {
        return new TestAgent(
            _providerFactoryMock.Object,
            _execRepoMock.Object,
            _msgRepoMock.Object,
            _costRepoMock.Object,
            _toolCallRepoMock.Object,
            _checkpointRepoMock.Object,
            _memoryRepoMock.Object,
            _piiRedactorMock.Object,
            _eventBusMock.Object,
            _agentOptions,
            _pricingOptions,
            _retryOptions,
            _toolRegistryMock.Object,
            NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_EndTurnOnFirstCall_ReturnsSuccessWithText()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Task completed successfully.", [], 100, 50));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Task completed successfully.");
        result.InputTokens.Should().Be(100);
        result.OutputTokens.Should().Be(50);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesProviderFromQualifiedModelPrefix()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 10, 5));

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        _providerFactoryMock.Verify(f => f.Resolve("anthropic"), Times.Once);
        // Scoped to the main agent turn — a background memory-extraction call also hits
        // SendAsync with the same model, identified here by its distinct system prompt.
        _providerMock.Verify(
            p => p.SendAsync(
                It.Is<AgentConversation>(c => c.Model == ModelName && c.SystemPrompt == "You are a test agent."),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EndTurnOnFirstCall_PublishesAgentStartedAndCompleted()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Final answer", [], 100, 50));

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

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

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName)).Returns(mockTool.Object);

        _providerMock.SetupSequence(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("tool_use", "", [new ToolRequest(toolId, toolName, @"{""key"":""value""}")], 80, 40))
            .ReturnsAsync(new AgentTurn("end_turn", "Done after tool use.", [], 60, 30));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, UserId, "Use a tool", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Done after tool use.");

        // Scoped to the main agent loop — a background memory-extraction call also hits
        // SendAsync, identified here by its distinct system prompt.
        _providerMock.Verify(
            p => p.SendAsync(
                It.Is<AgentConversation>(c => c.SystemPrompt == "You are a test agent."),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

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

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName)).Returns(mockTool.Object);

        _providerMock.SetupSequence(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("tool_use", "", [new ToolRequest("call_xyz", toolName, "{}")], 10, 5))
            .ReturnsAsync(new AgentTurn("end_turn", "Done.", [], 10, 5));

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Use a tool", CancellationToken.None);

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

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName)).Returns(mockTool.Object);

        _providerMock.SetupSequence(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("tool_use", "", [new ToolRequest("call_fail", toolName, "{}")], 10, 5))
            .ReturnsAsync(new AgentTurn("end_turn", "Recovered.", [], 10, 5));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, UserId, "Try tool", CancellationToken.None);

        result.Success.Should().BeTrue();

        _toolCallRepoMock.Verify(
            r => r.AddAsync(It.IsAny<McpToolCall>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_GetToolsCalledWithAgentToolNames()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 10, 5));

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        _toolRegistryMock.Verify(
            r => r.GetTools(It.Is<IReadOnlyList<string>>(names => names.Contains("test_tool"))),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_LlmProviderThrows_NonTransient_ReturnsFailureWithoutRetry()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("invalid api key"));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("invalid api key");
        _providerMock.Verify(
            p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()),
            Times.Once);
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

        _providerMock.SetupSequence(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("tool_use", "", [new ToolRequest("id1", toolName, "{}")], 200, 100))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 150, 75));

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, UserId, "Test", CancellationToken.None);

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
            ILlmProviderFactory llmProviderFactory,
            IAgentExecutionRepository execRepo,
            IAgentMessageRepository msgRepo,
            ICostLedgerRepository costRepo,
            IMcpToolCallRepository toolCallRepo,
            ITaskCheckpointRepository checkpointRepo,
            IAgentMemoryRepository memoryRepo,
            IPiiRedactor piiRedactor,
            IOrchestrationEventBus eventBus,
            IOptions<AgentOptions> agentOptions,
            IOptions<Dictionary<string, PricingEntry>> pricingOptions,
            IOptions<RetryPolicyOptions> retryOptions,
            IToolRegistry toolRegistry,
            ILoggerFactory loggerFactory)
            : base(llmProviderFactory, execRepo, msgRepo, costRepo, toolCallRepo, checkpointRepo,
                   memoryRepo, piiRedactor, eventBus, agentOptions, pricingOptions, retryOptions,
                   toolRegistry, loggerFactory)
        { }
    }
}
