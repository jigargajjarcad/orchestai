using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IAgent
{
    Task<AgentExecutionResult> ExecuteAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
