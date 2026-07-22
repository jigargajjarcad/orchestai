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
using OrchestAI.Infrastructure.Agents;
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
    private readonly Mock<IAgentRetryAttemptRepository> _retryAttemptRepoMock;
    private readonly Mock<IPiiRedactor> _piiRedactorMock;
    private readonly Mock<IOrchestrationEventBus> _eventBusMock;
    private readonly Mock<IToolRegistry> _toolRegistryMock;
    private readonly IOptions<AgentOptions> _agentOptions;
    private readonly Mock<IModelPricingCache> _modelPricingCacheMock;
    private readonly IOptions<RetryPolicyOptions> _retryOptions;
    private readonly Mock<IRejectionEventRepository> _rejectionEventRepoMock;

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
        _retryAttemptRepoMock = new Mock<IAgentRetryAttemptRepository>();
        _piiRedactorMock = new Mock<IPiiRedactor>();
        _eventBusMock = new Mock<IOrchestrationEventBus>();
        _toolRegistryMock = new Mock<IToolRegistry>();

        _agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Code"] = $"anthropic/{ModelName}" },
            MaxTokens = new Dictionary<string, int> { ["Code"] = 1024 }
        });
        _modelPricingCacheMock = new Mock<IModelPricingCache>();
        _modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 0.80m, 4.00m));
        _retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = 3, InitialDelayMs = 1, MaxDelayMs = 5, BackoffMultiplier = 2.0, JitterMs = 1
        });
        _rejectionEventRepoMock = new Mock<IRejectionEventRepository>();
        _rejectionEventRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RejectionEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
        _retryAttemptRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentRetryAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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
            _retryAttemptRepoMock.Object,
            _piiRedactorMock.Object,
            _eventBusMock.Object,
            _agentOptions,
            _modelPricingCacheMock.Object,
            _retryOptions,
            _toolRegistryMock.Object,
            new AsyncLocalTaskToolCallBudget(),
            _rejectionEventRepoMock.Object,
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
    public async Task ExecuteAsync_ModelPricingChangesAfterExecution_HistoricalCostRemainsUnchanged()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 1_000_000, 0));

        var capturedLedgers = new List<CostLedger>();
        _costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((ledger, _) => capturedLedgers.Add(ledger))
            .Returns(Task.CompletedTask);

        // Price at the time of the first execution: $1.00 per million input tokens.
        _modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 1.00m, 1.00m));

        var agent = BuildAgent();
        var resultAtOldPrice = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        resultAtOldPrice.CostUsd.Should().Be(1.00m);
        capturedLedgers.Should().ContainSingle().Which.CostUsd.Should().Be(1.00m);

        // Pricing changes after the fact (e.g. the provider raises prices, or an admin
        // corrects a typo in ModelPricing) — the already-computed, already-persisted result
        // must not drift, since AgentExecutionResult/CostLedger hold a plain stored decimal
        // with no live reference back to ModelPricing.
        _modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 2.00m, 2.00m));

        resultAtOldPrice.CostUsd.Should().Be(1.00m);
        capturedLedgers[0].CostUsd.Should().Be(1.00m);

        // A fresh computation, by contrast, does pick up the new price — proving this isn't
        // "pricing never updates," only that historical writes are immutable.
        var resultAtNewPrice = await agent.ExecuteAsync(TaskId, UserId, "Do something else", CancellationToken.None);

        resultAtNewPrice.CostUsd.Should().Be(2.00m);
        capturedLedgers.Should().HaveCount(2);
        capturedLedgers[1].CostUsd.Should().Be(2.00m);
        capturedLedgers[0].CostUsd.Should().Be(1.00m); // unaffected by the later write
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
    public async Task ExecuteAsync_EvalRunIdPassed_TagsExecutionAndCostLedgerAsEval()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 10, 5));

        AgentExecution? capturedExecution = null;
        _execRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Callback<AgentExecution, CancellationToken>((e, _) => capturedExecution = e)
            .Returns(Task.CompletedTask);

        CostLedger? capturedLedger = null;
        _costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((l, _) => capturedLedger = l)
            .Returns(Task.CompletedTask);

        var evalRunId = Guid.NewGuid();
        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None, evalRunId: evalRunId);

        capturedExecution!.EvalRunId.Should().Be(evalRunId);
        capturedLedger!.Source.Should().Be(CostSource.Eval);
        capturedLedger.EvalRunId.Should().Be(evalRunId);
    }

    [Fact]
    public async Task ExecuteAsync_NoEvalRunId_TagsCostLedgerAsProduction()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 10, 5));

        CostLedger? capturedLedger = null;
        _costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((l, _) => capturedLedger = l)
            .Returns(Task.CompletedTask);

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        capturedLedger!.Source.Should().Be(CostSource.Production);
        capturedLedger.EvalRunId.Should().BeNull();
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

    // Closes the gap found during Phase 3 live verification (docs/phase3-domain-notes.md):
    // exhausting MaxAgenticIterations mid-tool-call previously fell through to
    // FinalizeSuccessAsync using whatever stray "let me search more" text the model attached to
    // its final, un-synthesized turn — a silently-truncated result reported as Completed. This
    // must fail cleanly instead, reusing the exact RejectionEvent/AgentCapExceededException
    // mechanism already proven for MaxToolCallsPerTask (AgentToolCallCapIntegrationTests), per
    // the same reject-vs-truncate principle established in ADR-015 Confirmation #7.
    [Fact]
    public async Task ExecuteAsync_ExhaustsIterationCapMidToolUse_FailsWithRealErrorAndRejectionEvent()
    {
        const string toolName = "test_tool";

        var mockTool = new Mock<IMcpTool>();
        mockTool.Setup(t => t.ToolName).Returns(toolName);
        mockTool.Setup(t => t.Description).Returns("A test tool");
        mockTool.Setup(t => t.GetInputSchema()).Returns(new ToolInputSchema("object", new Dictionary<string, ToolProperty>(), []));
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult(true, "still searching"));

        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([mockTool.Object]);
        _toolRegistryMock.Setup(r => r.Get(toolName)).Returns(mockTool.Object);

        // Every turn requests another tool call and carries a stray "let me keep searching"
        // aside — the exact real-world shape that previously got persisted as if it were the
        // agent's real final answer. The model never reaches end_turn/max_tokens within the
        // 10-iteration budget (MaxAgenticIterations is not configurable and is not touched by
        // this fix — this test proves what happens when the existing cap is hit, not where it's set).
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn(
                "tool_use", "Let me search for more details.",
                [new ToolRequest("call_n", toolName, "{}")], 50, 20));

        AgentExecution? capturedExecution = null;
        _execRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Callback<AgentExecution, CancellationToken>((e, _) => capturedExecution = e)
            .Returns(Task.CompletedTask);

        var agent = BuildAgent();
        var result = await agent.ExecuteAsync(TaskId, UserId, "Investigate something", CancellationToken.None);

        // Must fail cleanly — never silently succeed with the stray "let me search more" text
        // as the reported output.
        result.Success.Should().BeFalse();
        result.Output.Should().BeEmpty();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.ErrorMessage.Should().NotContain("Let me search for more details.");

        capturedExecution.Should().NotBeNull();
        capturedExecution!.Status.Should().Be(ExecutionStatus.Failed);
        capturedExecution.ErrorMessage.Should().Be(result.ErrorMessage);

        // The loop stops at exactly the configured cap — not earlier, not later.
        _providerMock.Verify(
            p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()),
            Times.Exactly(10));

        // Reuses the same RejectionEvent + RejectionReason.AgentCapExceeded surface already
        // proven for MaxToolCallsPerTask — same observable rejection list, different cap.
        _rejectionEventRepoMock.Verify(
            r => r.AddAsync(It.Is<RejectionEvent>(e => e.Reason == RejectionReason.AgentCapExceeded), It.IsAny<CancellationToken>()),
            Times.Once);
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
            IAgentRetryAttemptRepository retryAttemptRepo,
            IPiiRedactor piiRedactor,
            IOrchestrationEventBus eventBus,
            IOptions<AgentOptions> agentOptions,
            IModelPricingCache modelPricingCache,
            IOptions<RetryPolicyOptions> retryOptions,
            IToolRegistry toolRegistry,
            ITaskToolCallBudget taskToolCallBudget,
            IRejectionEventRepository rejectionEventRepository,
            ILoggerFactory loggerFactory)
            : base(llmProviderFactory, execRepo, msgRepo, costRepo, toolCallRepo, checkpointRepo,
                   memoryRepo, retryAttemptRepo, piiRedactor, eventBus, agentOptions, modelPricingCache, retryOptions,
                   toolRegistry, taskToolCallBudget, rejectionEventRepository, loggerFactory)
        { }
    }
}
