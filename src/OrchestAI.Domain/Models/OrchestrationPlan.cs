using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

public sealed record OrchestrationPlan(
    string Plan,
    ExecutionMode ExecutionMode,
    IReadOnlyList<AgentType> SelectedAgents,
    IReadOnlyList<AgentType> ExecutionOrder,
    Dictionary<AgentType, string> AgentPrompts,
    AgentExecutionResult OrchestratorExecution
);
