using Microsoft.Extensions.Configuration;

namespace OrchestAI.Infrastructure.Configuration;

// Single choke point for "is the process configured well enough to start" — called as the very
// first line of AddInfrastructure, before any service registration, so a missing required value
// fails the container build immediately with one clear, aggregated message instead of a scattered
// per-dependency throw (the old shape: only Anthropic:ApiKey was checked, ConnectionStrings
// :DefaultConnection had no check at all and would only surface lazily, whenever Npgsql first
// tried to open a connection). Deliberately does NOT include Admin:BootstrapSecret — see
// DESIGN_PRINCIPLES.md-style reasoning in ADR-016: that secret already fails gracefully per-request
// (RequireAdminSecretFilter returns 503) rather than needing the whole process to refuse to start.
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

        if (missing.Count == 0)
            return;

        throw new InvalidOperationException(
            "Required configuration is missing or blank: " + string.Join(", ", missing) +
            ". Set the corresponding environment variable(s) (e.g. ConnectionStrings__DefaultConnection, " +
            "Anthropic__ApiKey) before starting the application.");
    }
}
