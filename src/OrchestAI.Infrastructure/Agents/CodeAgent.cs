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
