using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IOrchestratorAgent
{
    Task<OrchestrationPlan> PlanAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        CancellationToken cancellationToken = default);

    Task<AgentExecutionResult> ReviewAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        OrchestrationPlan plan,
        IReadOnlyList<AgentExecutionResult> results,
        CancellationToken cancellationToken = default);
}
