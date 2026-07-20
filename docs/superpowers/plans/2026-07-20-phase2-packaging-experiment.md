# Phase 2 — Packaging Validation Experiment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove, with a real local NuGet feed and a real out-of-repo console project, that `OrchestAI.Domain` + `OrchestAI.Application` + `OrchestAI.Infrastructure` can be consumed as libraries — submit one task, get one result, no ASP.NET Core host involved — and fix the one real packaging gap the investigation found (agent model/token config has no shipped default and no fail-fast check).

**Architecture:** No production code changes to the three core projects. This phase adds: (1) a local-only packing script, (2) a documented minimal config sample (the packaging-gap fix), (3) a disposable console project outside `OrchestAI.sln` that references the packed `.nupkg`s via a project-scoped `NuGet.Config`, and (4) an ADR recording what was found. The console project is intentionally left in the repo under `spikes/` after merge as evidence backing the ADR — it is not deleted, but it is explicitly not Phase 3's sample app and must not grow beyond the one path below.

**Tech Stack:** .NET 8 SDK (`dotnet pack`, `dotnet restore`), the existing MediatR/EF Core/Npgsql stack already in the core projects — nothing new is added to them.

## Global Constraints

- Do not modify `OrchestAI.Domain.csproj`, `OrchestAI.Application.csproj`, or `OrchestAI.Infrastructure.csproj` — packaging is proven via `dotnet pack -p:PackageVersion=...` at the CLI, not by editing checked-in project files with permanent package metadata (no semver policy yet — that's Phase 5).
- Do not publish anywhere but the local folder `artifacts/nupkgs/` (already covered by `.gitignore` line 95 `artifacts/`).
- The console consumer (`spikes/phase2-console-consumer/`) must NOT be added to `OrchestAI.sln` and must use `<PackageReference>`, never `<ProjectReference>`, to the core packages. Verify this explicitly in Task 3.
- The console consumer's scope is fixed: configure the library, submit one task, receive one result. Do not add config abstractions, retry/error-handling frameworks, CLI argument parsing, or a second scenario. Once Task 4 prints a final result, stop.
- Reuse `Tenant.DefaultTenantId` (`00000000-0000-0000-0000-000000000001`, provisioned by the `AddTenantIsolation` migration) and `DatabaseSeeder.DevUserId` (`3fa85f64-5717-4562-b3fc-2c963f66afa6`) — both are already public, stable, well-known IDs in this codebase. Do not invent new bootstrap/seeding code for the spike.
- Admission control (concurrency + budget) is enforced by the `OrchestrationTask` state machine itself (`Pending → Running` CAS in `OrchestrationAdmissionRepository.TryAdmitAsync`) regardless of caller — the console consumer must call `AdmitOrchestrationTaskCommand` before `StartOrchestrationCommand`, exactly like the API controller does, or `StartOrchestrationHandler` throws `InvalidOperationException`. Do not build a workaround.
- Per-minute HTTP rate limiting (`RateLimiterSetup`) is confirmed-accepted as an API-host-only concern — do not build a host-agnostic rate limiter for the console consumer. This was confirmed with the user during investigation (2026-07-20): a direct in-process library consumer is the trusted caller; rate limiting exists to protect a host from an untrusted network caller, a threat model that doesn't apply here.

---

### Task 1: Local NuGet packing script

**Files:**
- Create: `scripts/pack-local-nuget.sh`

**Interfaces:**
- Produces: three `.nupkg` files in `artifacts/nupkgs/` — `OrchestAI.Domain.0.1.0-phase2.nupkg`, `OrchestAI.Application.0.1.0-phase2.nupkg`, `OrchestAI.Infrastructure.0.1.0-phase2.nupkg` — consumed by Task 3's `NuGet.Config`.

- [ ] **Step 1: Write the packing script**

```bash
#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="$REPO_ROOT/artifacts/nupkgs"
VERSION="0.1.0-phase2"

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

for PROJECT in Domain Application Infrastructure; do
  dotnet pack "$REPO_ROOT/src/OrchestAI.$PROJECT/OrchestAI.$PROJECT.csproj" \
    -c Release \
    -p:PackageVersion="$VERSION" \
    -o "$OUT_DIR"
done

echo ""
echo "Packed to $OUT_DIR:"
ls "$OUT_DIR"
```

- [ ] **Step 2: Make it executable and run it**

Run: `chmod +x scripts/pack-local-nuget.sh && ./scripts/pack-local-nuget.sh`
Expected: three `Successfully created package '...'` lines, then a directory listing showing `OrchestAI.Domain.0.1.0-phase2.nupkg`, `OrchestAI.Application.0.1.0-phase2.nupkg`, `OrchestAI.Infrastructure.0.1.0-phase2.nupkg`.

- [ ] **Step 3: Verify the dependency graph converted correctly**

Run:
```bash
mkdir -p /tmp/nuspec-check && cd /tmp/nuspec-check && \
unzip -o -q "$REPO_ROOT/artifacts/nupkgs/OrchestAI.Infrastructure.0.1.0-phase2.nupkg" -d infra && \
cat infra/OrchestAI.Infrastructure.nuspec
```
Expected: a `<dependencies>` block listing `OrchestAI.Application` and `OrchestAI.Domain` at version `0.1.0-phase2`, alongside the third-party packages (Anthropic.SDK, Npgsql.EntityFrameworkCore.PostgreSQL, etc.). If either core project is missing from the list, the `ProjectReference → PackageReference` conversion silently failed — stop and investigate before Task 3, don't proceed with a broken graph.

- [ ] **Step 4: Confirm `artifacts/` stays untracked**

Run: `git status --short`
Expected: no output (the `.gitignore` `artifacts/` rule already covers this — nothing to add).

- [ ] **Step 5: Commit**

```bash
git add scripts/pack-local-nuget.sh
git commit -m "build: add local packing script for Phase 2 packaging validation"
```

---

### Task 2: Ship the minimal config sample (the packaging-gap fix)

**Context:** Investigation found `AgentOptions.Models`/`AgentOptions.MaxTokens` (bound from config section `Agents`) default to empty `Dictionary<string,string>()`/`Dictionary<string,int>()` in `src/OrchestAI.Infrastructure/Configuration/AgentOptions.cs`. `RequiredConfigurationValidator` (`src/OrchestAI.Infrastructure/Configuration/RequiredConfigurationValidator.cs`) deliberately does not check these — by design, it only fails fast on values required for the process to *start* (`ConnectionStrings:DefaultConnection`, `Anthropic:ApiKey`), not per-feature values. The full required shape exists today only as `src/OrchestAI.API/appsettings.json`, undocumented outside that one file. A consumer supplying only the two required keys gets a `KeyNotFoundException` deep inside `AgentBase.ExecuteAsync` the first time the orchestrator picks an agent type — not at startup, not with a clear message. This task ships a documented, copy-pasteable minimal config so that gap is closed without touching `RequiredConfigurationValidator`'s deliberately narrow fail-fast scope.

**Files:**
- Create: `docs/packaging/minimal-appsettings.json`
- Create: `docs/packaging/README.md`

- [ ] **Step 1: Write the minimal config sample**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme"
  },
  "Anthropic": {
    "ApiKey": ""
  },
  "Agents": {
    "Models": {
      "Orchestrator": "anthropic/claude-haiku-4-5-20251001",
      "Research": "anthropic/claude-haiku-4-5-20251001",
      "Writer": "anthropic/claude-haiku-4-5-20251001",
      "Code": "anthropic/claude-haiku-4-5-20251001",
      "Data": "anthropic/claude-haiku-4-5-20251001",
      "Browser": "anthropic/claude-haiku-4-5-20251001"
    },
    "MaxTokens": {
      "Orchestrator": 1024,
      "Research": 4096,
      "Writer": 8192,
      "Code": 4096,
      "Data": 4096,
      "Browser": 2048
    }
  },
  "Tools": {
    "Firecrawl": {
      "ApiKey": "",
      "BaseUrl": "https://api.firecrawl.dev/v1"
    },
    "Perplexity": {
      "ApiKey": "",
      "BaseUrl": "https://api.perplexity.ai"
    },
    "FileSystem": {
      "SandboxPath": "./agent-workspace"
    }
  }
}
```

- [ ] **Step 2: Write the README explaining what's required vs. defaulted**

```markdown
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
```

- [ ] **Step 3: Commit**

```bash
git add docs/packaging/minimal-appsettings.json docs/packaging/README.md
git commit -m "docs: ship minimal config sample for library consumers (Phase 2 packaging gap fix)"
```

---

### Task 3: Build the disposable console consumer

**Files:**
- Create: `spikes/phase2-console-consumer/PackagingSpike.csproj`
- Create: `spikes/phase2-console-consumer/NuGet.Config`
- Create: `spikes/phase2-console-consumer/appsettings.json`
- Create: `spikes/phase2-console-consumer/Program.cs`

**Interfaces:**
- Consumes: `AddApplication()` (`OrchestAI.Application.DependencyInjection`), `AddInfrastructure(IConfiguration)` (`OrchestAI.Infrastructure.DependencyInjection`), `ICurrentTenantAccessor.SetTenant(Guid)` (`OrchestAI.Domain.Interfaces`), `IMediator.Send` for `CreateOrchestrationTaskCommand`, `AdmitOrchestrationTaskCommand`, `StartOrchestrationCommand`, `GetOrchestrationTaskQuery` (all `OrchestAI.Application`), `Tenant.DefaultTenantId` and `DatabaseSeeder.DevUserId` (`OrchestAI.Domain.Entities` / `OrchestAI.Infrastructure.Data`).

- [ ] **Step 1: Write the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OrchestAI.Infrastructure" Version="0.1.0-phase2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.11" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the project-scoped NuGet source**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="orchestai-local" value="../../artifacts/nupkgs" />
  </packageSources>
</configuration>
```

This is scoped to this directory only (`dotnet nuget` resolves `NuGet.Config` by walking up from the current project) — it does not touch the user's global NuGet sources, unlike a `dotnet nuget add source` call.

- [ ] **Step 3: Copy the minimal config sample into place**

Run: `cp docs/packaging/minimal-appsettings.json spikes/phase2-console-consumer/appsettings.json`

- [ ] **Step 4: Write `Program.cs`**

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrchestAI.Application;
using OrchestAI.Application.Commands.AdmitOrchestrationTask;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Application.Queries.GetOrchestrationTask;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure;
using OrchestAI.Infrastructure.Data;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddApplication();
services.AddInfrastructure(configuration);

await using var provider = services.BuildServiceProvider();

// AddInfrastructure() only registers IDbContextFactory<AppDbContext> — nothing runs
// migrations automatically outside the ASP.NET Core host's own Program.cs startup block.
// A library consumer owns this step exactly like OrchestAI.API's Program.cs does.
await using (var scope = provider.CreateAsyncScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    await dbContext.Database.MigrateAsync();
}

// No HTTP request exists to resolve a tenant from — this is the direct, non-HTTP
// equivalent of what TenantAuthenticationMiddleware does per-request.
var tenantAccessor = provider.GetRequiredService<ICurrentTenantAccessor>();
using var tenantScope = tenantAccessor.SetTenant(Tenant.DefaultTenantId);

await using var requestScope = provider.CreateAsyncScope();
var mediator = requestScope.ServiceProvider.GetRequiredService<IMediator>();

Console.WriteLine("Submitting task...");
var created = await mediator.Send(new CreateOrchestrationTaskCommand(
    UserId: DatabaseSeeder.DevUserId,
    Title: "Phase 2 packaging spike",
    UserPrompt: "In one sentence, what is the capital of France?"));

Console.WriteLine($"Created task {created.Id}, status {created.Status}");

// Admission is not optional here — StartOrchestrationCommand throws InvalidOperationException
// on a task still in Pending. This is the Application-layer state machine from ADR-015,
// unrelated to and unbypassable via the HTTP rate limiter, which this process never touches.
await mediator.Send(new AdmitOrchestrationTaskCommand(created.Id));
Console.WriteLine("Admitted. Running orchestration synchronously (no HTTP, no SSE, no fire-and-forget)...");

await mediator.Send(new StartOrchestrationCommand(created.Id));

var result = await mediator.Send(new GetOrchestrationTaskQuery(created.Id));

Console.WriteLine();
Console.WriteLine($"Final status: {result!.Status}");
Console.WriteLine($"Result: {result.FinalResult}");
Console.WriteLine($"Cost: ${result.TotalCostUsd:F4} — tokens in/out: {result.TotalInputTokens}/{result.TotalOutputTokens}");

if (result.Status != "Completed")
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
    Environment.Exit(1);
}
```

- [ ] **Step 5: Verify it's genuinely outside the solution**

Run: `grep -c "PackagingSpike" OrchestAI.sln`
Expected: `0`. If this is anything but 0, remove the accidental solution reference — the whole point of this task is that the consumer is NOT part of `OrchestAI.sln`.

- [ ] **Step 6: Restore against the local feed**

Run: `cd spikes/phase2-console-consumer && dotnet restore`
Expected: `Restored .../PackagingSpike.csproj` with no errors — proves the `PackageReference` (not `ProjectReference`) path resolves the three local `.nupkg`s plus their public transitive dependencies (MediatR, Npgsql, Anthropic.SDK, etc.) from nuget.org.

- [ ] **Step 7: Build**

Run: `dotnet build` (from `spikes/phase2-console-consumer/`)
Expected: `Build succeeded.` If it fails on a missing type/namespace, that itself is a friction-point finding for Task 5's ADR entry — do not silently add packages to make it compile without recording why.

- [ ] **Step 8: Commit**

```bash
cd /path/to/repo/root
git add spikes/phase2-console-consumer
git commit -m "spike: disposable console consumer proving library packaging (Phase 2)"
```

---

### Task 4: Run it live, capture evidence

**Context:** This is the "prove it, don't assert it" step — same standard Phase 1 held itself to for the HTTP/SSE contract.

- [ ] **Step 1: Start a clean local Postgres**

Run: `docker compose up -d postgres` (from repo root, reusing the existing `docker-compose.yml` service — do not hand-roll a new container definition).
Expected: container healthy. If a stale container/volume from a prior session is already running on 5432, stop and remove it first (`docker compose down -v`) — don't layer a fresh migration attempt onto old schema state, matching the lesson already recorded from Phase 1's setup.

- [ ] **Step 2: Set the real Anthropic key for this run only**

Run: `export Anthropic__ApiKey="$(cat /path/to/your/real/key)"` in the shell that will run the spike — never write the real key into `appsettings.json` or commit it. `ConfigurationBuilder().AddEnvironmentVariables()` in `Program.cs` picks this up and overrides the blank value from the JSON file.

- [ ] **Step 3: Run the spike**

Run: `cd spikes/phase2-console-consumer && dotnet run`
Expected output shape:
```
Submitting task...
Created task <guid>, status Pending
Admitted. Running orchestration synchronously (no HTTP, no SSE, no fire-and-forget)...

Final status: Completed
Result: <one-sentence answer about Paris>
Cost: $0.00XX — tokens in/out: <n>/<n>
```

- [ ] **Step 4: Confirm no HTTP host or SSE infrastructure was ever involved**

Run: `ps aux | grep -i orchestai.api` and `lsof -i :5000 -i :5001` (or your API's configured ports) during Step 3.
Expected: nothing — `OrchestAI.API` was never built or started for this run. This is the actual proof of the packaging boundary, not just the restore/build succeeding.

- [ ] **Step 5: Record the real command output and timing as evidence**

Copy the full terminal transcript (Steps 1–4) into the ADR entry written in Task 5 — do not paraphrase or summarize it away. If the run reveals any friction beyond the `AgentOptions` gap already fixed in Task 2, capture the exact exception/message here rather than fixing it silently, and classify it (packaging / ergonomics / architectural) in Task 5 per the brief's three-way scheme.

- [ ] **Step 6: Tear down**

Run: `docker compose down` (leave the `-v` volume flag off unless you want to discard the schema too — this is your call at cleanup time, not scripted).

---

### Task 5: Record findings as ADR-017

**Files:**
- Modify: `DECISIONS.md` (append after the last existing ADR — check with `grep -n "^## ADR-" DECISIONS.md` for the current last entry number before writing, in case another ADR landed on `main` since this plan was written)

**Context:** Follows this repo's established `## ADR-NNN: Title` / `### Confirmation #N — subtitle` convention (see ADR-014, ADR-015, ADR-016 for the exact shape).

- [ ] **Step 1: Write the ADR entry**

```markdown
## ADR-017: Library Packaging Boundary

Phase 2 tested whether OrchestAI's core (Domain + Application + Infrastructure) separates
cleanly from "the app that happens to host it" — packaged locally, consumed from a genuinely
separate console project outside `OrchestAI.sln`, via `PackageReference` against a local NuGet
feed, with no ASP.NET Core host involved. See `docs/superpowers/plans/2026-07-20-phase2-packaging-experiment.md`
for the full task-by-task record and `spikes/phase2-console-consumer/` for the working proof.

### Confirmation #1 — What "core" means, and that the existing guardrail already covers it

`OrchestAI.Domain` has zero package references. `OrchestAI.Application` depends only on
Domain. `OrchestAI.Infrastructure` depends on Domain and Application plus third-party
packages (EF Core/Npgsql, Anthropic.SDK, etc.) — never on `Microsoft.AspNetCore.*` or
`OrchestAI.API`. `tests/OrchestAI.Tests/Architecture/LayeringTests.cs` (written during Weeks
9–10) already asserts every one of these boundaries via NetArchTest, including the
ASP.NET-Core-leakage checks added after two real violations (Task 8's action filter, Task 9's
middleware). No gap found; no new architecture test was needed.

### Confirmation #2 — Tenant context has a clean non-HTTP entry point already

`ICurrentTenantAccessor.SetTenant(Guid tenantId)` (Domain interface,
`AsyncLocalCurrentTenantAccessor` in Infrastructure) is a plain `IDisposable`-scoped API with
no HTTP dependency. `TenantAuthenticationMiddleware` is one caller of it, not its owner — the
console consumer in `spikes/phase2-console-consumer/Program.cs` calls the exact same method
directly. `TenantScopingInterceptor` reads the same ambient accessor to auto-stamp and
validate `TenantId` on every write, so this protection is not weakened for a direct consumer.

### Confirmation #3 — Admission control is a data-layer guarantee, not a middleware one; HTTP rate limiting is explicitly out of scope for library consumers

Two previously-conflated protections turned out to be structurally different.
`AdmitOrchestrationTaskCommand` performs the tenant's concurrency/budget check via an atomic
`Pending → Running` compare-and-swap (`OrchestrationAdmissionRepository.TryAdmitAsync`), and
`StartOrchestrationHandler` refuses to run a task that isn't already `Running`
(`InvalidOperationException` otherwise) — this is enforced by the domain state machine itself,
regardless of caller, and a direct library consumer cannot bypass it even by skipping the
"expected" call order. Per-minute HTTP rate limiting (`RateLimiterSetup`,
`PartitionedRateLimiter`) is genuinely API-host-only and unavailable outside an HTTP pipeline —
confirmed as the correct, deliberate scope, not a gap: rate limiting exists to protect a host
from an untrusted network caller, and that threat model doesn't apply to in-process code
calling its own embedded library. No host-agnostic rate limiter was built.

### Confirmation #4 — Postgres/EF Core stays a direct dependency of the packaged core

`OrchestAI.Infrastructure` depends on `Npgsql.EntityFrameworkCore.PostgreSQL` directly for its
own `AppDbContext`. This is an accepted limitation for this experimental phase, not a defect —
abstracting persistence further was explicitly out of scope. (Separately,
`Microsoft.Data.SqlClient` is present for `AdoDatabaseQueryExecutor`, the `DataAgent`'s
tenant-configurable database query tool — unrelated to OrchestAI's own storage.)

### Confirmation #5 — Local packaging mechanism

Plain `dotnet pack` on each of the three `.csproj` files (no hand-written `.nuspec`) correctly
converts `ProjectReference` into NuGet `<dependency>` entries at matching versions — verified
by inspecting the extracted `.nuspec`. A project-scoped `NuGet.Config` pointing at the local
output folder, consumed via `<PackageReference>` from a project genuinely outside
`OrchestAI.sln`, restores and builds successfully. See `scripts/pack-local-nuget.sh` and
`spikes/phase2-console-consumer/NuGet.Config`.

### Confirmation #6 — One real packaging gap found and fixed: agent config has no shipped default

`AgentOptions.Models`/`MaxTokens` default to empty dictionaries and are not covered by
`RequiredConfigurationValidator`'s deliberately narrow fail-fast scope (see
`feedback_fail_fast_scope_by_necessity`) — a consumer supplying only the two required keys
hits an undocumented `KeyNotFoundException` deep inside `AgentBase.ExecuteAsync` on first
agent dispatch, not at startup. Classified as a packaging issue (missing shipped
default/sample), not an architecture problem — the config system itself is unchanged.
Fixed by shipping `docs/packaging/minimal-appsettings.json` and
`docs/packaging/README.md`, documenting exactly which keys are required-and-checked,
required-but-unchecked, and code-defaulted.

### Confirmation #7 — The HTTP API's dispatch pattern is HTTP-specific, not a consumer requirement

`TasksController.StartAsync` admits synchronously then dispatches `StartOrchestrationCommand`
via fire-and-forget `Task.Run` + a fresh DI scope, so the HTTP response can return 202 before
the agent pipeline finishes — this exists solely to avoid blocking an HTTP request for the
duration of agent execution. `StartOrchestrationHandler.Handle` itself runs the full pipeline
synchronously and returns the final result directly. A direct library consumer has no
"don't block the response" constraint and gets a *simpler* flow than the HTTP API's own by
just `await`-ing the command chain directly — confirmed live in
`spikes/phase2-console-consumer/Program.cs`, which never touches SSE, tickets, or
`Task.Run`.

### Confirmation #7a — Live execution surfaced a real gap: migrating schema is not the same as seeding it

The plan's own draft `Program.cs` (Task 3) migrated the database but never seeded it, and
crashed on the first live run with `Npgsql.PostgresException 23503`
(`FK_OrchestrationTasks_Users_UserId`) — `DatabaseSeeder.DevUserId` is just a well-known GUID
constant; the actual `Users` row only exists after `DatabaseSeeder.SeedAsync()` runs, which
`OrchestAI.API/Program.cs:99-108` calls and the spike's first draft didn't.
`DatabaseSeeder.SeedAsync()` already calls `MigrateAsync()` itself as its first line and also
seeds `ModelPricing` rows (needed for real cost calculation, not just the FK). Fixed
(commit `fbaa512`) by mirroring the API host's exact startup pattern — resolve
`DatabaseSeeder` from a scope, call `SeedAsync()` — rather than reimplementing a partial
version of it. Classified as packaging/consumer-composition-root responsibility, not an
architecture problem: `DatabaseSeeder` already existed, already did the right thing, and was
simply never invoked outside the one host that happened to call it. This is exactly the kind
of gap that only surfaces by actually running the thing, not by reading the code — the same
lesson Phase 1 already established for HTTP/SSE surfaces, now confirmed for a packaging
boundary too.

### Confirmation #7b — Live execution surfaced a second real gap: `IConfiguration` is not free outside ASP.NET Core

The second live attempt failed with `InvalidOperationException: Unable to resolve service for
type 'Microsoft.Extensions.Configuration.IConfiguration' while attempting to activate
'OrchestAI.Infrastructure.Tools.DatabaseTool'`. `DatabaseTool` (`src/OrchestAI.Infrastructure/Tools/DatabaseTool.cs:24,59`)
takes `IConfiguration` directly — not `IOptions<T>` — because `_configuration.GetConnectionString(database)`
is a genuine dynamic, per-tool-call, arbitrary-named lookup that `IOptions<T>`'s fixed-shape
binding can't express; this is correct design in `DatabaseTool`, not a bug. The actual gap:
`AddInfrastructure(IConfiguration configuration)` uses the `configuration` parameter to bind
`IOptions<T>` classes but never registers `IConfiguration` itself into the container.
`OrchestAI.API` never noticed because `WebApplicationBuilder`/the Generic Host auto-registers
`IConfiguration` as a singleton — a bare `new ServiceCollection()` doesn't get that for free.
Fixed (commit `54a8350`) with one line in the console consumer's composition root,
`services.AddSingleton<IConfiguration>(configuration);`, added before `AddInfrastructure(...)`.
Classified as a packaging issue: a real, load-bearing host-coupling assumption that was
invisible until tested outside ASP.NET Core, fixed cheaply on the consumer side per this
phase's no-core-code-changes constraint. Flagging, not fixing now: `AddInfrastructure()` could
defensively self-register `IConfiguration` (e.g. `services.TryAddSingleton(configuration)`) so
future non-ASP.NET-Core consumers don't have to rediscover this — a cheap one-line hardening
worth a follow-up task, not urgent enough to justify a mid-phase core-project exception here.

### Confirmation #8 — Answering the five success-criteria questions

- **Can OrchestAI be consumed cleanly as libraries from outside this repository?** Yes —
  proven live, not just argued: `dotnet pack` → local feed → separate console project →
  `dotnet restore`/`build`/`run` → a real Anthropic API call → `Completed` with a real
  one-sentence answer ("The capital of France is Paris."), cost $0.0019, 1406/182 tokens
  in/out — no API host process or port listener present before or after the run. Two real
  bugs surfaced and were fixed along the way (Confirmations #7a, #7b) — the proof is stronger
  for having hit and cleared them, not weaker.
- **Does the architecture expose a pleasant or awkward public API?** Pleasant for the
  scope tested — two-line composition root (`AddApplication()` + `AddInfrastructure(config)`),
  a direct `await`-able command chain, a clean tenant-scope API. The warts found
  (Confirmations #6, #7a, #7b) were documentation/composition-root gaps, not architectural
  ones — nothing about the layering itself was wrong.
- **Which specific APIs feel awkward, and why?** `AgentOptions.Models`/`MaxTokens` — no
  fail-fast, no shipped default (fixed by documentation). Composition-root responsibilities
  (seeding, `IConfiguration` registration, logging) that ASP.NET Core provides for free and a
  bare `ServiceCollection` doesn't — expected of any MS-DI-based library, not unique to
  OrchestAI, but worth documenting explicitly since it wasn't before this phase.
- **Is each friction point superficial or architectural?** All findings landed in
  "packaging" or "ergonomics"; none required reopening a Weeks 7–12 decision. Zero items
  escalated to the architectural-boundary category.
- **Is Phase 3 (the real sample application) still the correct next milestone, unchanged?**
  Yes — nothing here suggests adjusting Phase 3's scope or premise.

### Confirmation #9 — Track A (employment/IP) is not tracked anywhere in this repository

While investigating, a grep across `DECISIONS.md` and every `*.md` file in the repo for
"Track A" returned zero hits — the employment/IP question referenced as a standing parallel
track has never been written down anywhere durable in this codebase, only discussed in
conversation. This doesn't block Phase 2, but it's flagged here as a live open dependency for
Phase 3, independent of packaging: worth capturing in at least a private, durable note before
Phase 3 starts, so it isn't resting entirely on chat history.
```

- [ ] **Step 2: Commit**

```bash
git add DECISIONS.md
git commit -m "docs: record ADR-017 (library packaging boundary) — Phase 2 packaging validation findings"
```

---

### Task 6: Finish the branch

- [ ] **Step 1: Run the full existing test suite once more on the worktree, unchanged**

Run: `dotnet test OrchestAI.sln`
Expected: same pass count as `main` before this branch started — this phase adds no new
production code to the three core projects or the test project, so no new tests are expected
and none of the 428 existing ones should move.

- [ ] **Step 2: Use `superpowers:finishing-a-development-branch`**

Follow that skill's structured options (merge to `main` locally, per this project's standing
"no PR-based workflow" pattern used in every prior phase) rather than deciding ad hoc here.
