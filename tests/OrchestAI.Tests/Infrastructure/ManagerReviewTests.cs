using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Infrastructure;

public sealed class ManagerReviewTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private const string ModelName = "claude-haiku-4-5-20251001";

    private readonly Mock<ILlmProvider> _providerMock;
    private readonly Mock<ILlmProviderFactory> _providerFactoryMock;
    private readonly Mock<IAgentExecutionRepository> _execRepoMock;
    private readonly Mock<IOrchestrationEventBus> _eventBusMock;

    public ManagerReviewTests()
    {
        _providerMock = new Mock<ILlmProvider>();
        _providerMock.Setup(p => p.ProviderId).Returns("anthropic");

        _providerFactoryMock = new Mock<ILlmProviderFactory>();
        _providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(_providerMock.Object);

        _execRepoMock = new Mock<IAgentExecutionRepository>();
        _execRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _execRepoMock.Setup(r => r.UpdateAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventBusMock = new Mock<IOrchestrationEventBus>();
    }

    private OrchestratorAgent BuildAgent()
    {
        var msgRepoMock = new Mock<IAgentMessageRepository>();
        msgRepoMock.Setup(r => r.AddAsync(It.IsAny<AgentMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        costRepoMock.Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Orchestrator"] = $"anthropic/{ModelName}" },
            MaxTokens = new Dictionary<string, int> { ["Orchestrator"] = 1024 }
        });
        var modelPricingCacheMock = new Mock<IModelPricingCache>();
        modelPricingCacheMock
            .Setup(c => c.GetAsync(ModelName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create(ModelName, 0.80m, 4.00m));
        var retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = 3, InitialDelayMs = 1, MaxDelayMs = 5, BackoffMultiplier = 2.0, JitterMs = 1
        });

        var piiRedactorMock = new Mock<IPiiRedactor>();
        piiRedactorMock.Setup(r => r.IsEnabled).Returns(false);

        var retryAttemptRepoMock = new Mock<IAgentRetryAttemptRepository>();
        retryAttemptRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AgentRetryAttempt>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new OrchestratorAgent(
            _providerFactoryMock.Object,
            _execRepoMock.Object,
            msgRepoMock.Object,
            costRepoMock.Object,
            new Mock<IMcpToolCallRepository>().Object,
            new Mock<ITaskCheckpointRepository>().Object,
            new Mock<IAgentMemoryRepository>().Object,
            retryAttemptRepoMock.Object,
            piiRedactorMock.Object,
            _eventBusMock.Object,
            agentOptions,
            modelPricingCacheMock.Object,
            retryOptions,
            new Mock<IToolRegistry>().Object,
            new AsyncLocalTaskToolCallBudget(),
            Mock.Of<IRejectionEventRepository>(),
            NullLoggerFactory.Instance);
    }

    private static OrchestrationPlan MakePlan(params AgentType[] executionOrder) => new(
        "Research then write",
        ExecutionMode.Sequential,
        executionOrder,
        executionOrder,
        executionOrder.ToDictionary(a => a, a => $"{a} prompt"),
        new AgentExecutionResult(Guid.NewGuid(), "{}", true, 10, 5, 0.0001m));

    [Fact]
    public async Task ReviewAsync_AllAgentsSucceeded_ReturnsSynthesizedResult()
    {
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Final synthesized answer.", [], 120, 60));

        var plan = MakePlan(AgentType.Research, AgentType.Writer);
        var results = new List<AgentExecutionResult>
        {
            new(Guid.NewGuid(), "Research findings here.", true, 100, 50, 0.001m),
            new(Guid.NewGuid(), "Written report here.", true, 200, 100, 0.002m)
        };

        var agent = BuildAgent();
        var review = await agent.ReviewAsync(TaskId, "Original task prompt", plan, results, CancellationToken.None);

        review.Success.Should().BeTrue();
        review.Output.Should().Be("Final synthesized answer.");

        _eventBusMock.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "manager_review_started")),
            Times.Once);
        _eventBusMock.Verify(
            b => b.Publish(TaskId, It.Is<SseEvent>(e => e.Event == "manager_review_completed")),
            Times.Once);
    }

    [Fact]
    public async Task ReviewAsync_FailedAgentOutput_IncludedAsFailedInReviewPrompt()
    {
        AgentConversation? capturedConversation = null;
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .Callback<AgentConversation, CancellationToken>((conv, _) => capturedConversation = conv)
            .ReturnsAsync(new AgentTurn("end_turn", "Partial synthesis noting the failure.", [], 80, 40));

        var plan = MakePlan(AgentType.Research, AgentType.Writer);
        var results = new List<AgentExecutionResult>
        {
            new(Guid.NewGuid(), string.Empty, false, 0, 0, 0m, "Perplexity API timeout"),
            new(Guid.NewGuid(), "Written report despite missing research.", true, 200, 100, 0.002m)
        };

        var agent = BuildAgent();
        await agent.ReviewAsync(TaskId, "Original task prompt", plan, results, CancellationToken.None);

        capturedConversation.Should().NotBeNull();
        var userPrompt = capturedConversation!.Messages.Single().TextContent;
        userPrompt.Should().Contain("FAILED: Perplexity API timeout");
        userPrompt.Should().Contain("Written report despite missing research.");
    }
}
