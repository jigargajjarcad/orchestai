using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Agents;

public sealed class DataAgent : AgentBase
{
    protected override AgentType AgentType => AgentType.Data;
    protected override IReadOnlyList<string> AvailableToolNames => ["firecrawl_scrape"];

    protected override string SystemPrompt =>
        """
        You are a data analysis specialist. Given a data task or question, analyze
        the data, identify patterns, and present findings clearly with supporting
        evidence. Use firecrawl_scrape to extract structured data from webpages when needed.
        Structure your response with summary, findings, and recommendations.
        """;

    public DataAgent(
        IAnthropicClientWrapper anthropicClient,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IOptions<Dictionary<string, PricingEntry>> pricingOptions,
        IToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
        : base(anthropicClient, agentExecutionRepository, agentMessageRepository,
               costLedgerRepository, mcpToolCallRepository, eventBus,
               agentOptions, pricingOptions, toolRegistry, loggerFactory)
    {
    }
}
