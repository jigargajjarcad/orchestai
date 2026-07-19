using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Application;

// Closes the Week 11 deferred gap tracked in ADR-015 (Task 13, DECISIONS.md): nothing previously
// drove AgentBase.InvokeToolAsync's MaxToolCallsPerTask budget check end-to-end through real
// agent execution — only AsyncLocalTaskToolCallBudgetTests (the counter in isolation) and
// StartOrchestrationAgentCapTests (the unrelated MaxAgentsPerTask pre-dispatch check) existed.
// Both tests below use a REAL ResearchAgent/CodeAgent (concrete AgentBase subclasses) and a REAL
// AsyncLocalTaskToolCallBudget, driven through the real StartOrchestrationHandler.Handle — only
// the LLM provider and the leaf MCP tools are faked.
public sealed class AgentToolCallCapIntegrationTests
{
    private const string ModelName = "claude-haiku-4-5-20251001";

    // ── Test 1: primary cap-exceeded case, single sequential agent ─────────────────────────

    [Fact]
    public async Task Handle_ResearchAgentExceedsToolCallCap_TaskFailsWithRejectionEventAndExactErrorMessage()
    {
        const int cap = 2;

        var task = OrchestrationTask.Create(Guid.NewGuid(), "Research something", "Find out X", false);
        task.MarkRunning();
        var tenantId = Guid.NewGuid();

        // ── Fake LLM: a single turn requesting 3 tool calls against a cap of 2. The first two
        // increments succeed (count 1, 2); the third (count 3 > 2) throws AgentCapExceededException
        // from inside AgentBase.InvokeToolAsync, before that third tool is ever invoked — proven
        // below by verifying the underlying tool executed exactly twice, not three times.
        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn(
                "tool_use",
                "",
                [
                    new ToolRequest("call_1", "perplexity_search", "{}"),
                    new ToolRequest("call_2", "perplexity_search", "{}"),
                    new ToolRequest("call_3", "perplexity_search", "{}")
                ],
                10, 5));

        var providerFactoryMock = new Mock<ILlmProviderFactory>();
        providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var mockTool = new Mock<IMcpTool>();
        mockTool.Setup(t => t.ToolName).Returns("perplexity_search");
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult(true, "search result"));

        var toolRegistryMock = new Mock<IToolRegistry>();
        toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        toolRegistryMock.Setup(r => r.Get("perplexity_search")).Returns(mockTool.Object);

        AgentExecution? capturedExecution = null;
        var execRepoMock = new Mock<IAgentExecutionRepository>();
        execRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        execRepoMock.Setup(r => r.UpdateAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Callback<AgentExecution, CancellationToken>((e, _) => capturedExecution = e)
            .Returns(Task.CompletedTask);

        var msgRepoMock = new Mock<IAgentMessageRepository>();
        msgRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        costRepoMock.Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolCallRepoMock = new Mock<IMcpToolCallRepository>();
        toolCallRepoMock.Setup(r => r.AddAsync(It.IsAny<McpToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        checkpointRepoMock.Setup(r => r.UpsertAsync(It.IsAny<TaskCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        checkpointRepoMock.Setup(r => r.DeleteByTaskIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var memoryRepoMock = new Mock<IAgentMemoryRepository>();
        memoryRepoMock
            .Setup(r => r.GetRelevantAsync(It.IsAny<Guid>(), It.IsAny<AgentType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var retryAttemptRepoMock = new Mock<IAgentRetryAttemptRepository>();
        retryAttemptRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentRetryAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var piiRedactorMock = new Mock<IPiiRedactor>();
        piiRedactorMock.Setup(r => r.IsEnabled).Returns(false);

        var eventBusMock = new Mock<IOrchestrationEventBus>();

        var agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Research"] = $"anthropic/{ModelName}" },
            MaxTokens = new Dictionary<string, int> { ["Research"] = 1024 }
        });

        var modelPricingCacheMock = new Mock<IModelPricingCache>();
        modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 0.80m, 4.00m));

        var retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = 3, InitialDelayMs = 1, MaxDelayMs = 5, BackoffMultiplier = 2.0, JitterMs = 1
        });

        // The mechanism under test: a REAL AsyncLocalTaskToolCallBudget, shared (via its static
        // AsyncLocal, per its own implementation) between the Handler's BeginScope call and the
        // ResearchAgent's own TryIncrement calls — exactly like production DI.
        var toolCallBudget = new AsyncLocalTaskToolCallBudget();

        var rejectionEventRepoMock = new Mock<IRejectionEventRepository>();
        rejectionEventRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RejectionEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var researchAgent = new ResearchAgent(
            providerFactoryMock.Object,
            execRepoMock.Object,
            msgRepoMock.Object,
            costRepoMock.Object,
            toolCallRepoMock.Object,
            checkpointRepoMock.Object,
            memoryRepoMock.Object,
            retryAttemptRepoMock.Object,
            piiRedactorMock.Object,
            eventBusMock.Object,
            agentOptions,
            modelPricingCacheMock.Object,
            retryOptions,
            toolRegistryMock.Object,
            toolCallBudget,
            rejectionEventRepoMock.Object,
            NullLoggerFactory.Instance);

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(researchAgent);

        var orchestratorExecution = new AgentExecutionResult(
            Guid.NewGuid(), "plan text", true, 10, 10, 0.01m, ErrorMessage: null, SpanId: "span-1");
        var plan = new OrchestrationPlan(
            Plan: "plan text",
            ExecutionMode: ExecutionMode.Sequential,
            SelectedAgents: [AgentType.Research],
            ExecutionOrder: [AgentType.Research],
            AgentPrompts: new Dictionary<AgentType, string> { [AgentType.Research] = "Find out X" },
            OrchestratorExecution: orchestratorExecution);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        orchestratorMock
            .Setup(o => o.ReviewAsync(
                task.Id, task.UserPrompt, plan, It.IsAny<IReadOnlyList<AgentExecutionResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), "review output", true, 5, 5, 0.001m, SpanId: "span-2"));

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();
        reservationRepoMock.Setup(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 4, cap, 50m, 500m, 100)); // MaxToolCallsPerTask = 2

        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(tenantId);

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object, eventBusMock.Object,
            approvalGatewayMock.Object, checkpointRepoMock.Object, reservationRepoMock.Object,
            limitsProviderMock.Object, accessorMock.Object, rejectionEventRepoMock.Object, toolCallBudget,
            NullLogger<StartOrchestrationHandler>.Instance);

        await handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        // The task never completes — it fails, driven entirely by the real AgentBase execution
        // path, not a shortcut.
        task.Status.Should().Be(OrchestrationTaskStatus.Failed);
        task.Status.Should().NotBe(OrchestrationTaskStatus.Completed);

        // The exact wording from AgentBase.cs:601 survives all the way up through
        // AgentExecutionResult.ErrorMessage -> StartOrchestrationHandler's errorSummary ->
        // OrchestrationTask.ErrorMessage (a single failed agent, so no "; " join noise).
        const string expectedMessage = "Task tool-call cap exceeded: 3 calls attempted, limit is 2.";
        task.ErrorMessage.Should().Be(expectedMessage);

        // The AgentExecutionResult that AgentBase.ExecuteAsync's own catch built (via
        // FinalizeFailureAsync) — captured at the point it was persisted, proving
        // Success == false with the exact wording, not just the aggregated task message.
        capturedExecution.Should().NotBeNull();
        capturedExecution!.Status.Should().Be(ExecutionStatus.Failed);
        capturedExecution.ErrorMessage.Should().Be(expectedMessage);

        // A RejectionEvent was persisted from inside AgentBase.InvokeToolAsync's cap-exceeded
        // branch — the same mechanism StartOrchestrationAgentCapTests proves for MaxAgentsPerTask,
        // now proven for MaxToolCallsPerTask.
        rejectionEventRepoMock.Verify(
            r => r.AddAsync(It.Is<RejectionEvent>(e => e.Reason == RejectionReason.AgentCapExceeded), It.IsAny<CancellationToken>()),
            Times.Once);

        // Proves the cap check runs BEFORE tool invocation: exactly 2 of the 3 requested tool
        // calls actually reached the underlying IMcpTool, not 3 (which would mean the cap was
        // checked too late) and not 0/1 (which would mean the loop stopped for the wrong reason).
        mockTool.Verify(
            t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ── Test 2: parallel-execution concurrency case ─────────────────────────────────────────

    [Fact]
    public async Task Handle_ParallelAgentsShareToolCallCap_NeverExceedsCapAcrossConcurrentSubAgents()
    {
        const int cap = 4;

        var task = OrchestrationTask.Create(Guid.NewGuid(), "Research and code", "Do two things", false);
        task.MarkRunning();
        var tenantId = Guid.NewGuid();

        // Shared budget instance — its counter is backed by a static AsyncLocal (see
        // AsyncLocalTaskToolCallBudget), so this mirrors production's single-scope-per-task
        // design: the Handler opens one scope, and both concurrently-dispatched sub-agents
        // (forked via Task.WhenAll, exactly like RunSubAgentAsync/Handle) increment the same
        // counter.
        var toolCallBudget = new AsyncLocalTaskToolCallBudget();

        var rejectionEventRepoMock = new Mock<IRejectionEventRepository>();
        rejectionEventRepoMock
            .Setup(r => r.AddAsync(It.IsAny<RejectionEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var capturedExecutions = new List<AgentExecution>();
        var execRepoMock = new Mock<IAgentExecutionRepository>();
        execRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        execRepoMock.Setup(r => r.UpdateAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Callback<AgentExecution, CancellationToken>((e, _) => capturedExecutions.Add(e))
            .Returns(Task.CompletedTask);

        var msgRepoMock = new Mock<IAgentMessageRepository>();
        msgRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        costRepoMock.Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var toolCallRepoMock = new Mock<IMcpToolCallRepository>();
        toolCallRepoMock.Setup(r => r.AddAsync(It.IsAny<McpToolCall>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        checkpointRepoMock.Setup(r => r.UpsertAsync(It.IsAny<TaskCheckpoint>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        checkpointRepoMock.Setup(r => r.DeleteByTaskIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var memoryRepoMock = new Mock<IAgentMemoryRepository>();
        memoryRepoMock
            .Setup(r => r.GetRelevantAsync(It.IsAny<Guid>(), It.IsAny<AgentType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var retryAttemptRepoMock = new Mock<IAgentRetryAttemptRepository>();
        retryAttemptRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentRetryAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var piiRedactorMock = new Mock<IPiiRedactor>();
        piiRedactorMock.Setup(r => r.IsEnabled).Returns(false);

        var eventBusMock = new Mock<IOrchestrationEventBus>();

        var agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string>
            {
                ["Research"] = $"anthropic/{ModelName}",
                ["Code"] = $"anthropic/{ModelName}"
            },
            MaxTokens = new Dictionary<string, int> { ["Research"] = 1024, ["Code"] = 1024 }
        });

        var modelPricingCacheMock = new Mock<IModelPricingCache>();
        modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 0.80m, 4.00m));

        var retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = 3, InitialDelayMs = 1, MaxDelayMs = 5, BackoffMultiplier = 2.0, JitterMs = 1
        });

        // Two distinct concrete tools, each invoked with a real await-yielding delay so the two
        // agents' foreach-over-ToolRequests loops genuinely interleave on the thread pool rather
        // than resolving synchronously in list order — a real concurrency test, not a
        // sequential one with a misleading name.
        var researchInvocations = 0;
        var mockResearchTool = new Mock<IMcpTool>();
        mockResearchTool.Setup(t => t.ToolName).Returns("perplexity_search");
        mockResearchTool
            .Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref researchInvocations);
                await Task.Delay(15);
                return new McpToolResult(true, "search ok");
            });

        var codeInvocations = 0;
        var mockCodeTool = new Mock<IMcpTool>();
        mockCodeTool.Setup(t => t.ToolName).Returns("file_write");
        mockCodeTool
            .Setup(t => t.ExecuteAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref codeInvocations);
                await Task.Delay(15);
                return new McpToolResult(true, "write ok");
            });

        var toolRegistryMock = new Mock<IToolRegistry>();
        toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        toolRegistryMock.Setup(r => r.Get("perplexity_search")).Returns(mockResearchTool.Object);
        toolRegistryMock.Setup(r => r.Get("file_write")).Returns(mockCodeTool.Object);

        // Each agent attempts 3 tool calls in its single turn — more than half the shared cap
        // of 4 — so combined demand (6) exceeds the cap, forcing real contention on the shared
        // counter.
        var researchProviderMock = new Mock<ILlmProvider>();
        researchProviderMock.Setup(p => p.ProviderId).Returns("anthropic");
        researchProviderMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn(
                "tool_use", "",
                [
                    new ToolRequest("r1", "perplexity_search", "{}"),
                    new ToolRequest("r2", "perplexity_search", "{}"),
                    new ToolRequest("r3", "perplexity_search", "{}")
                ],
                10, 5));
        var researchProviderFactoryMock = new Mock<ILlmProviderFactory>();
        researchProviderFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(researchProviderMock.Object);

        var codeProviderMock = new Mock<ILlmProvider>();
        codeProviderMock.Setup(p => p.ProviderId).Returns("anthropic");
        codeProviderMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn(
                "tool_use", "",
                [
                    new ToolRequest("c1", "file_write", "{}"),
                    new ToolRequest("c2", "file_write", "{}"),
                    new ToolRequest("c3", "file_write", "{}")
                ],
                10, 5));
        var codeProviderFactoryMock = new Mock<ILlmProviderFactory>();
        codeProviderFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(codeProviderMock.Object);

        var researchAgent = new ResearchAgent(
            researchProviderFactoryMock.Object, execRepoMock.Object, msgRepoMock.Object, costRepoMock.Object,
            toolCallRepoMock.Object, checkpointRepoMock.Object, memoryRepoMock.Object, retryAttemptRepoMock.Object,
            piiRedactorMock.Object, eventBusMock.Object, agentOptions, modelPricingCacheMock.Object, retryOptions,
            toolRegistryMock.Object, toolCallBudget, rejectionEventRepoMock.Object, NullLoggerFactory.Instance);

        var codeAgent = new CodeAgent(
            codeProviderFactoryMock.Object, execRepoMock.Object, msgRepoMock.Object, costRepoMock.Object,
            toolCallRepoMock.Object, checkpointRepoMock.Object, memoryRepoMock.Object, retryAttemptRepoMock.Object,
            piiRedactorMock.Object, eventBusMock.Object, agentOptions, modelPricingCacheMock.Object, retryOptions,
            toolRegistryMock.Object, toolCallBudget, rejectionEventRepoMock.Object, NullLoggerFactory.Instance);

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(researchAgent);
        agentFactoryMock.Setup(f => f.Create(AgentType.Code)).Returns(codeAgent);

        var orchestratorExecution = new AgentExecutionResult(
            Guid.NewGuid(), "plan text", true, 10, 10, 0.01m, ErrorMessage: null, SpanId: "span-1");
        var plan = new OrchestrationPlan(
            Plan: "plan text",
            ExecutionMode: ExecutionMode.Parallel,
            SelectedAgents: [AgentType.Research, AgentType.Code],
            ExecutionOrder: [AgentType.Research, AgentType.Code],
            AgentPrompts: new Dictionary<AgentType, string>
            {
                [AgentType.Research] = "Research something",
                [AgentType.Code] = "Write some code"
            },
            OrchestratorExecution: orchestratorExecution);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);
        orchestratorMock
            .Setup(o => o.ReviewAsync(
                task.Id, task.UserPrompt, plan, It.IsAny<IReadOnlyList<AgentExecutionResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutionResult(Guid.NewGuid(), "review output", true, 5, 5, 0.001m, SpanId: "span-2"));

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();
        reservationRepoMock.Setup(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 4, cap, 50m, 500m, 100)); // MaxToolCallsPerTask = 4

        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(tenantId);

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object, eventBusMock.Object,
            approvalGatewayMock.Object, checkpointRepoMock.Object, reservationRepoMock.Object,
            limitsProviderMock.Object, accessorMock.Object, rejectionEventRepoMock.Object, toolCallBudget,
            NullLogger<StartOrchestrationHandler>.Instance);

        await handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        // Combined demand was 3 + 3 = 6 tool calls against a shared cap of 4. Because
        // AsyncLocalTaskToolCallBudget.TryIncrement is a single Interlocked-guarded counter,
        // exactly `cap` calls can ever succeed regardless of how the two agents' real,
        // concurrent (Task.Delay-yielding) executions interleave: successes are monotonically
        // the first 4 tickets handed out, and every agent stops immediately on its own first
        // rejection, so total successes can never fall short of or exceed the cap here.
        var totalSuccessfulInvocations = researchInvocations + codeInvocations;
        totalSuccessfulInvocations.Should().Be(cap,
            "the shared counter must let exactly the cap's worth of concurrent tool calls through — no bypass, no under-block");

        // Both agents got to run concurrently and share the counter — neither monopolized nor
        // was starved (each agent alone requests only 3, less than the cap of 4, so each must
        // have contributed at least one successful call for the total to reach 4).
        researchInvocations.Should().BeInRange(1, 3);
        codeInvocations.Should().BeInRange(1, 3);

        // Since combined demand (6) exceeds the cap (4), at least one agent necessarily hit a
        // rejection — proven via a real RejectionEvent persisted from inside
        // AgentBase.InvokeToolAsync, not merely inferred.
        rejectionEventRepoMock.Verify(
            r => r.AddAsync(It.Is<RejectionEvent>(e => e.Reason == RejectionReason.AgentCapExceeded), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // At least one of the two real AgentExecutionResults (captured via the persisted
        // AgentExecution) failed with the exact cap-exceeded wording — the task-level failure
        // is a direct consequence of the real per-tool-call budget check, not a fabricated one.
        capturedExecutions.Should().Contain(e =>
            e.Status == ExecutionStatus.Failed &&
            e.ErrorMessage != null &&
            e.ErrorMessage.StartsWith("Task tool-call cap exceeded:", StringComparison.Ordinal));

        // The task can never end Completed when at least one sub-agent failed the cap check.
        task.Status.Should().Be(OrchestrationTaskStatus.Failed);
    }
}
