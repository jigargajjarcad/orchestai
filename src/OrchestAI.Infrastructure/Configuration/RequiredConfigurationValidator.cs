using Microsoft.Extensions.Configuration;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Infrastructure.Configuration;

// Single choke point for "is the process configured well enough to start" — called as the very
// first line of AddInfrastructure, before any service registration, so a missing required value
// fails the container build immediately with one clear, aggregated message instead of a scattered
// per-dependency throw (the old shape: only Anthropic:ApiKey was checked, ConnectionStrings
// :DefaultConnection had no check at all and would only surface lazily, whenever Npgsql first
// tried to open a connection). Deliberately does NOT include Admin:BootstrapSecret — see
// DESIGN_PRINCIPLES.md-style reasoning in ADR-016: that secret already fails gracefully per-request
// (RequireAdminSecretFilter returns 503) rather than needing the whole process to refuse to start.
// Agents:Models/MaxTokens ARE included (ADR-017 Confirmation #6): every AgentType dispatches
// through AgentBase.ExecuteAsync, which indexes both dictionaries directly, so a missing entry
// used to surface only as a KeyNotFoundException deep in agent execution on first dispatch, not a
// clear startup error. Unlike Admin:BootstrapSecret, there's no per-request graceful degradation
// for a missing agent model/token budget, so it belongs in this same fail-fast choke point.
public static class RequiredConfigurationValidator
{
    private static readonly string[] RequiredKeys =
    [
        "ConnectionStrings:DefaultConnection",
        "Anthropic:ApiKey"
    ];

    public static void Validate(IConfiguration configuration)
    {
        var missing = RequiredKeys
            .Where(key => string.IsNullOrWhiteSpace(configuration[key]))
            .ToList();

        missing.AddRange(MissingAgentConfigurationKeys(configuration));

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            "Required configuration is missing or blank: " + string.Join(", ", missing) +
            ". Set the corresponding environment variable(s) (e.g. ConnectionStrings__DefaultConnection, " +
            "Anthropic__ApiKey) before starting the application.");
    }

    private static IEnumerable<string> MissingAgentConfigurationKeys(IConfiguration configuration)
    {
        foreach (var agentType in Enum.GetValues<AgentType>())
        {
            var modelsKey = $"Agents:Models:{agentType}";
            if (string.IsNullOrWhiteSpace(configuration[modelsKey]))
                yield return modelsKey;

            var maxTokensKey = $"Agents:MaxTokens:{agentType}";
            if (string.IsNullOrWhiteSpace(configuration[maxTokensKey]))
                yield return maxTokensKey;
        }
    }
}
