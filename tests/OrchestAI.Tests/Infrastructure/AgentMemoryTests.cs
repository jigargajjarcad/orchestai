using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AgentMemoryTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private const string ModelName = "claude-haiku-4-5-20251001";

    [Fact]
    public async Task ExecuteAsync_ExistingMemories_InjectsThemIntoSystemPrompt()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);

        mocks.MemoryRepo
            .Setup(r => r.GetRelevantAsync(UserId, AgentType.Code, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                AgentMemory.Create(UserId, AgentType.Code, "preferred_language", "C# with nullable reference types", 8)
            ]);

        AgentConversation? capturedConversation = null;
        mocks.Provider
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .Callback<AgentConversation, CancellationToken>((conv, _) =>
            {
                // Only capture the main agent turn — memory extraction fires a second, separate call.
                if (conv.SystemPrompt.StartsWith("You are a test agent.", StringComparison.Ordinal))
                    capturedConversation = conv;
            })
            .ReturnsAsync(new AgentTurn("end_turn", "Done.", [], 10, 5));

        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        capturedConversation.Should().NotBeNull();
        capturedConversation!.SystemPrompt.Should().Contain("MEMORY FROM PREVIOUS INTERACTIONS");
        capturedConversation.SystemPrompt.Should().Contain("preferred_language: C# with nullable reference types");
    }

    [Fact]
    public async Task ExecuteAsync_NoMemories_SystemPromptUnchanged()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        mocks.MemoryRepo
            .Setup(r => r.GetRelevantAsync(UserId, AgentType.Code, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        AgentConversation? capturedConversation = null;
        mocks.Provider
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .Callback<AgentConversation, CancellationToken>((conv, _) =>
            {
                if (conv.SystemPrompt.StartsWith("You are a test agent.", StringComparison.Ordinal))
                    capturedConversation = conv;
            })
            .ReturnsAsync(new AgentTurn("end_turn", "Done.", [], 10, 5));

        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        capturedConversation.Should().NotBeNull();
        capturedConversation!.SystemPrompt.Should().Be("You are a test agent.");
    }

    [Fact]
    public async Task ExecuteAsync_AfterCompletion_ExtractsAndStoresMemories()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        mocks.MemoryRepo
            .Setup(r => r.GetRelevantAsync(It.IsAny<Guid>(), It.IsAny<AgentType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        mocks.Provider.SetupSequence(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "The user prefers dark mode UIs.", [], 10, 5))
            .ReturnsAsync(new AgentTurn(
                "end_turn",
                """[{"key": "ui_preference", "value": "prefers dark mode", "importance": 6}]""",
                [], 20, 10));

        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        mocks.MemoryRepo.Verify(
            r => r.UpsertAsync(
                It.Is<AgentMemory>(m => m.UserId == UserId && m.AgentType == AgentType.Code
                    && m.Key == "ui_preference" && m.Value == "prefers dark mode" && m.Importance == 6),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MemoryExtractionThrows_AgentStillCompletesSuccessfully()
    {
        var (agent, mocks) = BuildAgent();
        mocks.ToolRegistry.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        mocks.MemoryRepo
            .Setup(r => r.GetRelevantAsync(It.IsAny<Guid>(), It.IsAny<AgentType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var callCount = 0;
        mocks.Provider
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                // First call is the main turn; second is memory extraction — make it fail.
                return callCount == 1
                    ? Task.FromResult(new AgentTurn("end_turn", "Main output.", [], 10, 5))
                    : throw new InvalidOperationException("memory extraction LLM call failed");
            });

        var result = await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Main output.");
        mocks.MemoryRepo.Verify(
            r => r.UpsertAsync(It.IsAny<AgentMemory>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetRelevantAsync_ExpiredMemory_NotReturned()
    {
        var repo = new InMemoryFakeAgentMemoryRepository();
        var expired = AgentMemory.Create(UserId, AgentType.Research, "stale_fact", "old info", 5, DateTimeOffset.UtcNow.AddDays(-1));
        var active = AgentMemory.Create(UserId, AgentType.Research, "fresh_fact", "current info", 5, DateTimeOffset.UtcNow.AddDays(1));
        await repo.UpsertAsync(expired, CancellationToken.None);
        await repo.UpsertAsync(active, CancellationToken.None);

        var results = await repo.GetRelevantAsync(UserId, AgentType.Research, 10, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Key.Should().Be("fresh_fact");
    }

    // A minimal in-memory fake — good enough to exercise the "expired excluded" filter
    // logic without a real database; the repository's actual EF query mirrors this filter.
    private sealed class InMemoryFakeAgentMemoryRepository : IAgentMemoryRepository
    {
        private readonly List<AgentMemory> _memories = [];

        public Task<IReadOnlyList<AgentMemory>> GetRelevantAsync(
            Guid userId, AgentType agentType, int maxEntries = 10, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AgentMemory> result = _memories
                .Where(m => m.UserId == userId && m.AgentType == agentType && !m.IsExpired())
                .OrderByDescending(m => m.Importance)
                .Take(maxEntries)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<AgentMemory>> GetAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentMemory>>(_memories.Where(m => m.UserId == userId).ToList());

        public Task<IReadOnlyList<AgentMemory>> GetAllForUserAndAgentTypeAsync(
            Guid userId, AgentType agentType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentMemory>>(
                _memories.Where(m => m.UserId == userId && m.AgentType == agentType).ToList());

        public Task<AgentMemory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_memories.FirstOrDefault(m => m.Id == id));

        public Task UpsertAsync(AgentMemory memory, CancellationToken cancellationToken = default)
        {
            _memories.Add(memory);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _memories.RemoveAll(m => m.Id == id);
            return Task.CompletedTask;
        }

        public Task DeleteExpiredAsync(CancellationToken cancellationToken = default)
        {
            _memories.RemoveAll(m => m.IsExpired());
            return Task.CompletedTask;
        }
    }

    private static (TestAgent Agent, AgentMocks Mocks) BuildAgent()
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
        memoryRepoMock.Setup(r => r.UpsertAsync(It.IsAny<AgentMemory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var piiRedactorMock = new Mock<IPiiRedactor>();
        piiRedactorMock.Setup(r => r.IsEnabled).Returns(false);

        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var toolRegistryMock = new Mock<IToolRegistry>();

        var agentOptions = Options.Create(new AgentOptions
        {
            Models = new Dictionary<string, string> { ["Code"] = $"anthropic/{ModelName}" },
            MaxTokens = new Dictionary<string, int> { ["Code"] = 1024 }
        });
        var pricingOptions = Options.Create(new Dictionary<string, PricingEntry>
        {
            [ModelName] = new PricingEntry { InputPerMillion = 0.80m, OutputPerMillion = 4.00m }
        });
        var retryOptions = Options.Create(new RetryPolicyOptions
        {
            MaxAttempts = 1, InitialDelayMs = 1, MaxDelayMs = 5, BackoffMultiplier = 2.0, JitterMs = 1
        });

        var agent = new TestAgent(
            providerFactoryMock.Object, execRepoMock.Object, msgRepoMock.Object, costRepoMock.Object,
            toolCallRepoMock.Object, checkpointRepoMock.Object, memoryRepoMock.Object, piiRedactorMock.Object,
            eventBusMock.Object, agentOptions, pricingOptions, retryOptions, toolRegistryMock.Object,
            NullLoggerFactory.Instance);

        return (agent, new AgentMocks(providerMock, memoryRepoMock, toolRegistryMock));
    }

    private sealed record AgentMocks(
        Mock<ILlmProvider> Provider,
        Mock<IAgentMemoryRepository> MemoryRepo,
        Mock<IToolRegistry> ToolRegistry);

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
