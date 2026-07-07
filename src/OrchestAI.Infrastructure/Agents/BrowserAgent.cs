using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Agents;

public sealed class BrowserAgent : AgentBase
{
    protected override AgentType AgentType => AgentType.Browser;

    protected override string SystemPrompt =>
        """
        You are a browser automation specialist. Given a web interaction task,
        describe the steps needed and the expected outcomes. Be specific about
        selectors, navigation paths, and validation checkpoints.
        """;

    public BrowserAgent(
        ILlmProviderFactory llmProviderFactory,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        ITaskCheckpointRepository checkpointRepository,
        IAgentMemoryRepository memoryRepository,
        IPiiRedactor piiRedactor,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IOptions<Dictionary<string, PricingEntry>> pricingOptions,
        IOptions<RetryPolicyOptions> retryOptions,
        IToolRegistry toolRegistry,
        ILoggerFactory loggerFactory)
        : base(llmProviderFactory, agentExecutionRepository, agentMessageRepository,
               costLedgerRepository, mcpToolCallRepository, checkpointRepository,
               memoryRepository, piiRedactor, eventBus, agentOptions, pricingOptions,
               retryOptions, toolRegistry, loggerFactory)
    {
    }
}
