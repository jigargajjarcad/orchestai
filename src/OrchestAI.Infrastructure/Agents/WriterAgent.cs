using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Agents;

public sealed class WriterAgent : AgentBase
{
    protected override AgentType AgentType => AgentType.Writer;
    protected override IReadOnlyList<string> AvailableToolNames => ["file_write", "firecrawl_scrape"];

    protected override string SystemPrompt =>
        """
        You are a professional writer and content specialist. Given research findings
        or a topic, produce well-structured, clear, and professional written content.
        You can write reports, summaries, articles, and documentation.
        Use file_write to save your output to files when the task requires it.
        Use firecrawl_scrape to fetch additional reference material if needed.
        Always organize with clear headings, concise paragraphs, and actionable conclusions.
        """;

    public WriterAgent(
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
