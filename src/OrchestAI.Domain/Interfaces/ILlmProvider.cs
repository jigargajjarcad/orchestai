using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface ILlmProvider
{
    /// <summary>
    /// Short provider prefix matching the "provider" segment of a qualified model
    /// string (e.g. "anthropic", "azure", "openai"). Used by ILlmProviderFactory
    /// to route a call and combined with the model name for CostLedger.Model.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Send a conversation turn and receive the model's response.
    /// All SDK-specific mapping happens inside the implementation.
    /// AgentBase never imports any SDK namespace.
    /// </summary>
    Task<AgentTurn> SendAsync(
        AgentConversation conversation,
        CancellationToken cancellationToken = default);
}
