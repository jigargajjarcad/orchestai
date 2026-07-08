using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IAgent
{
    Task<AgentExecutionResult> ExecuteAsync(
        Guid orchestrationTaskId,
        Guid userId,
        string userPrompt,
        CancellationToken cancellationToken = default,
        string? parentSpanId = null,
        Guid? evalRunId = null);
}
