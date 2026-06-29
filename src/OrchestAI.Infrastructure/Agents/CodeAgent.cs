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
