using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Agents.Base;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Agents;

public sealed class CodeAgent : AgentBase
{
    protected override AgentType AgentType => AgentType.Code;
    protected override IReadOnlyList<string> AvailableToolNames => ["file_write"];

    protected override string SystemPrompt =>
        """
        You are a code specialist. Given a task, produce clean, well-commented,
        production-ready code. Use file_write to save code files to the workspace.
        Include error handling, explain your approach briefly,
        and provide usage examples where relevant.
        """;

    public CodeAgent(
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
