using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Agents;

public sealed class ResearchAgent : AgentBase
{
    protected override AgentType AgentType => AgentType.Research;
    protected override IReadOnlyList<string> AvailableToolNames => ["firecrawl_scrape", "perplexity_search"];

    protected override string SystemPrompt =>
        """
        You are a research specialist. Use your available tools to gather accurate,
        up-to-date information on the given topic. Use perplexity_search for
        synthesized research with citations, and firecrawl_scrape when you need
        the full content of a specific webpage. Synthesize your findings into a
        clear, structured summary with sources.
        """;

    public ResearchAgent(
        ILlmProviderFactory llmProviderFactory,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        ITaskCheckpointRepository checkpointRepository,
        IAgentMemoryRepository memoryRepository,
        IAgentRetryAttemptRepository agentRetryAttemptRepository,
        IPiiRedactor piiRedactor,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IModelPricingCache modelPricingCache,
        IOptions<RetryPolicyOptions> retryOptions,
        IToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
        : base(llmProviderFactory, agentExecutionRepository, agentMessageRepository,
               costLedgerRepository, mcpToolCallRepository, checkpointRepository,
               memoryRepository, agentRetryAttemptRepository, piiRedactor, eventBus, agentOptions, modelPricingCache,
               retryOptions, toolRegistry, loggerFactory)
    {
    }
}
