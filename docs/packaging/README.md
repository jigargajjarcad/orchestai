# Minimal configuration for consuming OrchestAI as a library

`AddInfrastructure(configuration)` needs an `IConfiguration` with the shape below.
Copy `minimal-appsettings.json` into your project and override the two secret
values via environment variables rather than committing them
(`ConnectionStrings__DefaultConnection`, `Anthropic__ApiKey`).

## Required — the process throws `InvalidOperationException` at `AddInfrastructure` if missing

- `ConnectionStrings:DefaultConnection` — Postgres connection string.
- `Anthropic:ApiKey` — checked by `RequiredConfigurationValidator`.

## Required — NOT checked at startup, but the first agent dispatch throws `KeyNotFoundException` if missing

- `Agents:Models:<AgentType>` and `Agents:MaxTokens:<AgentType>` for all six agent types
  (`Orchestrator`, `Research`, `Writer`, `Code`, `Data`, `Browser`) — `AgentBase.ExecuteAsync`
  does `_agentOptions.Value.Models[AgentType.ToString()]` with no fallback. The orchestrator
  picks agent types dynamically per task, so all six must be present, not just the ones you
  expect to use. This is the one packaging gap this phase found and fixed by shipping this
  sample — see ADR-017.

## Optional — safe to omit entirely, code-level defaults apply

- `Tools:Firecrawl:ApiKey`, `Tools:Perplexity:ApiKey` — blank is tolerated at startup; the
  tool only fails if an agent actually invokes it (matches the API host's own
  `appsettings.json`, which ships these blank in Development).
- `TenantLimitsDefaults:*` — `TenantLimitsDefaults` (`src/OrchestAI.Infrastructure/Configuration/TenantLimitsDefaults.cs`)
  has property-initializer defaults (120 req/min, 5 concurrent tasks, $50/day, etc.); omitting
  the whole section is safe.
- `AbuseProtection:*` — same story, defaults live on `AbuseProtectionOptions`
  (`src/OrchestAI.Application/Configuration/AbuseProtectionOptions.cs`).

## Not covered by `IConfiguration` at all

`AddApplication()`/`AddInfrastructure()` do not call `services.AddLogging(...)` — that's a
host responsibility for any Microsoft.Extensions.DependencyInjection-based library, not an
OrchestAI-specific gap. Your composition root must call
`services.AddLogging(b => b.AddConsole())` (or any other provider) before resolving anything,
or DI throws resolving `ILoggerFactory`.
