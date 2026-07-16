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
    protected override IReadOnlyList<string> AvailableToolNames => ["firecrawl_scrape", "db_query"];

    protected override string SystemPrompt =>
        """
        You are a data analysis specialist. Given a data task or question, analyze
        the data, identify patterns, and present findings clearly with supporting
        evidence. Use firecrawl_scrape to extract structured data from webpages, and
        db_query to run read-only SQL against configured databases (PostgreSQL or
        SQL Server) when needed. Structure your response with summary, findings, and
        recommendations.
        """;

    public DataAgent(
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
        ITaskToolCallBudget taskToolCallBudget,
        IRejectionEventRepository rejectionEventRepository,
        ILoggerFactory loggerFactory)
        : base(llmProviderFactory, agentExecutionRepository, agentMessageRepository,
               costLedgerRepository, mcpToolCallRepository, checkpointRepository,
               memoryRepository, agentRetryAttemptRepository, piiRedactor, eventBus, agentOptions, modelPricingCache,
               retryOptions, toolRegistry, taskToolCallBudget, rejectionEventRepository, loggerFactory)
    {
    }
}
