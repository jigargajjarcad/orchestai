using System.Net;
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

public sealed class RetryPolicyTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string ModelName = "claude-haiku-4-5-20251001";

    [Fact]
    public async Task ExecuteAsync_TransientError_RetriesAndFiresAgentRetryEvent()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);

        mocks.Provider.SetupSequence(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests))
            .ReturnsAsync(new AgentTurn("end_turn", "Recovered after retry.", [], 10, 5));

        var result = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Recovered after retry.");

        mocks.EventBus.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e =>
                e.Event == "agent_retry"
                && e.Payload.ToString()!.Contains("attempt"))),
            Times.Once);

        mocks.RetryAttemptRepo.Verify(
            r => r.AddAsync(
                It.Is<AgentRetryAttempt>(a => a.AttemptNumber == 1 && a.Reason.Contains("rate limited")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientError_FailsImmediatelyWithoutRetry()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);

        mocks.Provider
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized));

        var result = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        result.Success.Should().BeFalse();

        mocks.Provider.Verify(
            p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()),
            Times.Once);
        mocks.EventBus.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "agent_retry")),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TransientErrorExceedsMaxAttempts_FailsWithAgentExecutionException()
    {
        var (agent, mocks) = BuildAgent(maxAttempts: 3);
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);

        mocks.Provider
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("service down", null, HttpStatusCode.ServiceUnavailable));

        var result = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("failed after 3 attempts");

        mocks.Provider.Verify(
            p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        mocks.EventBus.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "agent_retry")),
            Times.Exactly(2)); // fired before each of the 2 retries, not before the final failed attempt
    }

    [Fact]
    public void IsTransient_TooManyRequests_IsTransient()
    {
        var ex = new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests);
        InvokeIsTransient(ex).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_Unauthorized_IsNotTransient()
    {
        var ex = new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized);
        InvokeIsTransient(ex).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_RateLimitMessageWithoutStatusCode_IsTransient()
    {
        var ex = new InvalidOperationException("Request was rate limited by upstream provider");
        InvokeIsTransient(ex).Should().BeTrue();
    }

    private static bool InvokeIsTransient(Exception ex)
    {
        var method = typeof(AgentBase).GetMethod(
            "IsTransient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (bool)method.Invoke(null, [ex, CancellationToken.None])!;
    }

    private static (TestAgent Agent, AgentMocks Mocks) BuildAgent(int maxAttempts = 3)
    {
        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");

        var providerFactoryMock = new Mock<ILlmProviderFactory>();
        providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var execRepoMock = new Mock<IAgentExecutionRepository>();
        execRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        execRepoMock.Setup(r => r.UpdateAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var msgRepoMock = new Mock<IAgentMessageRepository>();
        msgRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        costRepoMock.Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var toolCallRepoMock = new Mock<IMcpToolCallRepository>();

        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        checkpointRepoMock.Setup(r => r.UpsertAsync(It.IsAny<TaskCheckpoint>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var memoryRepoMock = new Mock<IAgentMemoryRepository>();
        memoryRepoMock
            .Setup(r => r.GetRelevantAsync(It.IsAny<Guid>(), It.IsAny<AgentType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var retryAttemptRepoMock = new Mock<IAgentRetryAttemptRepository>();
        retryAttemptRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AgentRetryAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var piiRedactorMock = new Mock<IPiiRedactor>();
        piiRedactorMock.Setup(r => r.IsEnabled).Returns(false);

        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var toolRegistryMock = new Mock<IToolRegistry>();

        var agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Code"] = $"anthropic/{ModelName}" },
            MaxTokens = new Dictionary<string, int> { ["Code"] = 1024 }
        });
        var modelPricingCacheMock = new Mock<IModelPricingCache>();
        modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 0.80m, 4.00m));
        // Zero-ish delays so retry tests run fast.
        var retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = maxAttempts, InitialDelayMs = 1, MaxDelayMs = 2, BackoffMultiplier = 1.0, JitterMs = 0
        });

        var agent = new TestAgent(
            providerFactoryMock.Object, execRepoMock.Object, msgRepoMock.Object, costRepoMock.Object,
            toolCallRepoMock.Object, checkpointRepoMock.Object, memoryRepoMock.Object, retryAttemptRepoMock.Object,
            piiRedactorMock.Object,
            eventBusMock.Object, agentOptions, modelPricingCacheMock.Object, retryOptions, toolRegistryMock.Object,
            new AsyncLocalTaskToolCallBudget(), Mock.Of<IRejectionEventRepository>(),
            NullLoggerFactory.Instance);

        return (agent, new AgentMocks(providerMock, eventBusMock, toolRegistryMock, retryAttemptRepoMock));
    }

    private sealed record AgentMocks(
        Mock<ILlmProvider> Provider,
        Mock<IOrchestrationEventBus> EventBus,
        Mock<IToolRegistry> ToolRegistry,
        Mock<IAgentRetryAttemptRepository> RetryAttemptRepo);

    private sealed class TestAgent : AgentBase
    {
        protected override string SystemPrompt => "You are a test agent.";
        protected override AgentType AgentType => AgentType.Code;

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
