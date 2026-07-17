# Week 12: Delivery and Operational Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **This is the operational-safety hardening pass, not a feature week.** Every deliverable here exists to make Railway deploys safer and more observable when something goes wrong, not to add product functionality. Do not skip the empirical-verification steps (actually running `dotnet ef`, actually building the Docker image, actually pushing a scratch branch and watching CI fail) in favor of reasoning about what "should" work — this plan's own investigation phase already found one real, non-obvious runtime bug (`HostAbortedException` mishandling in `Program.cs`) purely by running a command that looked harmless on paper. Two steps in this plan (Task 13 repo-settings changes, Task 14 scratch-branch push) touch shared/live infrastructure (GitHub repo settings, a pushed branch, GitHub Actions runs) — confirm with the user before executing those specific steps, even under subagent-driven-development's normal task-review flow.

**Goal:** Ship automated CI (build/test/migrate/container-smoke/security-scan on every push to `main`), a liveness/readiness health-check split wired into Railway, fail-fast startup configuration validation, a migration-reversibility policy with an enforcing test, and a `RUNBOOK.md` + `ADR-016` documenting all of it — so a bad deploy has a clear, fast, low-risk recovery path and CI catches regressions before a human notices them in production.

**Architecture:** Four independent, parallel GitHub Actions jobs in one `ci.yml` triggered on push to `main` (plus `workflow_dispatch` for manual/scratch-branch runs): `build-and-test` (real Postgres service, full suite), `migration-validation` (two Postgres services proving fresh-install and upgrade paths converge to an identical schema), `container-smoke-test` (builds the real production `Dockerfile`, runs it, polls the new `/health/ready`), and `security-scan` (`dotnet list package --vulnerable`, Trivy image scan). `/health/live` is a zero-dependency liveness probe; `/health/ready` is backed by a new `IReadinessChecker`/`DatabaseReadinessChecker` (Domain interface, Infrastructure implementation, following this codebase's existing interface-in-Domain convention) that checks DB connectivity and pending EF Core migrations. A new centralized configuration validator fails the process at startup — before `/health/ready` can ever report healthy — if required config (DB connection string, Anthropic API key) is missing, and a `Program.cs` fix ensures a real startup failure actually exits non-zero (today it silently exits 0, which would defeat Railway's `ON_FAILURE` restart policy).

**Tech Stack:** C# .NET 8 minimal APIs (no new ASP.NET Core HealthChecks middleware package — two `MapGet` endpoints backed by a plain interface, consistent with this project's existing `/health` minimal-API style), EF Core 8 `IMigrator` (direct migration-to-target API, used by both the new readiness test and the CI migration-validation job), GitHub Actions (`actions/setup-dotnet@v4`, Postgres `services:`, `aquasecurity/trivy-action`), Dependabot.

## Global Constraints

- Keep the 0-warning, all-green bar. Confirmed baseline before Task 1: `dotnet test tests/OrchestAI.Tests` → **404 passing, 0 failed, 0 skipped**, against the local `docker-compose` Postgres. Every task must end at 0 warnings / all-green, growing the total from 404.
- No PR-based workflow. `main` continues to be merged-to-locally-and-pushed exactly as it has been for 11 prior weeks (see `CONTRIBUTING.md`, `DECISIONS.md` process notes). CI triggers on `push: branches: [main]` — it is a post-hoc safety net and portfolio signal, not a merge gate. The only exception: `workflow_dispatch` is also enabled, purely so Task 14's scratch-branch proof can trigger a run on a non-`main` branch without changing the `push` trigger itself.
- No multi-OS/multi-framework test matrix, no blue-green/canary deploy strategy, no enterprise secrets manager, no mandatory PR gate. Railway's own "redeploy previous build" is the rollback mechanism; `RUNBOOK.md` documents it, nothing in this plan builds a replacement for it.
- Every new cross-cutting interface follows this codebase's established Domain-defines/Infrastructure-implements split (`ICurrentTenantAccessor`, `ITenantLimitsProvider`, etc.) — `IReadinessChecker` lives in `OrchestAI.Domain.Interfaces`, `DatabaseReadinessChecker` in `OrchestAI.Infrastructure`. `Microsoft.Extensions.Diagnostics.HealthChecks` (the interface `IHealthCheck` type) is **not** used — it would need a new NuGet dependency and buys nothing over a plain interface for a two-endpoint use case with no existing HealthChecks middleware anywhere in this codebase (reuse-before-rebuild / no premature abstraction).
- `LayeringTests` (`tests/OrchestAI.Tests/Architecture/LayeringTests.cs`) must keep passing untouched by every task — no new type placed in `Infrastructure` may reference `Microsoft.AspNetCore.Mvc` or `Microsoft.AspNetCore.Http`; `DatabaseReadinessChecker` only needs EF Core + logging, so this is a non-issue if built as specified.
- CI's Postgres service credentials must exactly match `docker-compose.yml`/`appsettings.json` (`orchestai` / `orchestai` / `changeme`) — every existing real-Postgres integration test (`AdmissionConcurrencyRaceTests`, `TenantBackfillIntegrationTests`, etc.) hardcodes `Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme` as a `private const string`, confirmed via grep across `tests/OrchestAI.Tests/Infrastructure/`. Using different CI credentials would require touching 7 existing test files for no benefit.
- Required-config validation covers exactly `ConnectionStrings:DefaultConnection` and `Anthropic:ApiKey` — **not** `Admin:BootstrapSecret`. The admin secret already fails gracefully today (`RequireAdminSecretFilter` returns `503` per-request if unset, confirmed by reading the file in full) rather than crashing, and that's a deliberate, already-shipped, already-correct posture: a fresh Railway deploy that hasn't set up admin bootstrap yet should still serve normal tenant traffic. Treating it as "required" would make the whole app fail to start over an optional operator-only surface. This reasoning goes into `ADR-016` explicitly, not left implicit.
- `docker build` in CI uses the **exact root `Dockerfile`** with no build args (confirmed: `railway.json` sets `"builder": "DOCKERFILE", "dockerfilePath": "Dockerfile"` with no `startCommand` override, so Railway just runs the Dockerfile's own `ENTRYPOINT` unmodified) — no parallel CI-only Dockerfile, per confirmation #4.

---

## Investigation summary (already done — do not re-derive)

Verified directly against the current codebase (not assumed):

- **No `.github/` directory exists at all.** This is a fully greenfield CI/Dependabot setup — no prior workflow to preserve or migrate.
- **`Program.cs`** (`src/OrchestAI.API/Program.cs`, read in full, 127 lines): single `/health` minimal-API endpoint today (`app.MapGet("/health", ...)`, always 200, no DB check). Auto-migrates on every startup (`await dbContext.Database.MigrateAsync()`) before `app.Run()`. Top-level `try { ... } catch (Exception ex) { Log.Fatal(...); } finally { Log.CloseAndFlush(); }` — **the catch block does not set a non-zero exit code**, so today a startup failure (bad config, unreachable DB) logs Fatal and then the process exits with code `0`, which would defeat Railway's `restartPolicyType: ON_FAILURE` (a `0` exit looks like an intentional, successful shutdown, not a failure to restart from).
- **A real, empirically-confirmed bug interacts directly with the above fix.** Running `dotnet ef migrations list` (needed for Task 9's migration-validation job) executes `Program.cs`'s top-level statements far enough to hit the try/catch — EF Core's design-time tooling throws `Microsoft.Extensions.Hosting.HostAbortedException` synchronously inside `WebApplicationBuilder.Build()` as its documented mechanism for capturing the built host without actually running it. Confirmed by directly running the command locally: it logs `"OrchestAI API terminated unexpectedly"` at Fatal level and prints the full `HostAbortedException` stack trace, even though the command itself still succeeds (the migrations list prints correctly afterward — EF's tooling captures what it needs via a `DiagnosticListener` side-channel before the exception even needs to propagate, confirmed by the list output being correct despite the current unconditional catch swallowing the exception). **If Task 1's exit-code fix is added to the current unconditional `catch (Exception ex)` without excluding `HostAbortedException`, every single `dotnet ef` command (migrations list/add, database update) would poison `Environment.ExitCode` to `1`** — since `dotnet ef` invokes `Program.Main` via reflection in the *same process* rather than a subprocess, and `Environment.ExitCode` is one mutable global the tool's own successful-completion path does not reset. This would silently break every CI step in Task 9 that checks `dotnet ef`'s exit code, and would not be caught by a passing build — only by actually running the command, exactly the kind of failure `DESIGN_PRINCIPLES.md`'s "Empirical verification over plausible-sounding review" section warns about. The fix is the standard, documented pattern: `catch (Exception ex) when (ex is not HostAbortedException)`.
- **`DependencyInjection.cs`** (`src/OrchestAI.Infrastructure/DependencyInjection.cs`, read in full): already has one ad-hoc fail-fast check — `configuration["Anthropic:ApiKey"] ?? throw new InvalidOperationException(...)` — but `ConnectionStrings:DefaultConnection` has **zero** validation; a missing/malformed value is only ever discovered lazily, whenever Npgsql first attempts to open a connection (inside the `MigrateAsync` call at startup today, so it happens to fail reasonably fast in practice, but with no clear, purpose-built diagnostic message — just whatever exception Npgsql/EF happens to throw).
- **`AddDbContextFactory<AppDbContext>(...)` also registers `AppDbContext` itself as a directly-injectable Scoped service** (confirmed: `Program.cs` line 90 does `scope.ServiceProvider.GetRequiredService<AppDbContext>()` directly, and the app runs successfully in production today) — this is EF Core 8's actual behavior for `AddDbContextFactory`, not a bug to fix. `DatabaseReadinessChecker` can take `AppDbContext` directly in its constructor, exactly like the existing pattern.
- **Two hosted services exist, both `BackgroundService`s registered via `AddHostedService`**: `CostRollupBackgroundService` (polls every 5 minutes, `ExecuteAsync`'s first action inside the loop is the actual rollup work — no async init phase) and `EvalRunBackgroundWorker` (blocks on `_queue.DequeueAsync(stoppingToken)` as its very first statement — no async init phase either). **Neither has any meaningful asynchronous initialization before it can process work** — both are immediately ready the instant `ExecuteAsync` starts. This directly answers confirmation #7: no readiness-gating mechanism is needed for hosted-service startup; `ADR-016` documents this as an explicit investigated-and-ruled-out finding, not a silent omission.
- **All 12 existing EF Core migrations already have non-empty, fully-reversible, EF-generated `Down()` methods** (confirmed via grep across every migration file in `src/OrchestAI.Infrastructure/Migrations/`) — every migration so far has been purely additive (new tables/columns/indexes), so every `Down()` is a straightforward structural inverse (`DropTable`/`DropColumn`/`DropIndex`/`DropForeignKey`). **No existing migration needs to change.** Task 4 is purely: (a) add the enforcing test so a *future* non-additive migration can't ship with an empty/thoughtless `Down()`, and (b) document the policy (working `Down()` for additive changes; `Down()` throwing `NotSupportedException` with a documented reason for irreversible ones) in `ADR-016`/`RUNBOOK.md`.
- **Existing "integration" tests are split across two real backing stores**, not one: `tests/OrchestAI.Tests/Integration/*.cs` (5 files: `CrossTenantIsolationSweepTests`, etc.) use EF Core's **InMemory** provider; `tests/OrchestAI.Tests/Infrastructure/*.cs` (7 files: `AdmissionConcurrencyRaceTests`, `TenantBackfillIntegrationTests`, `CostRollupUniqueIndexIntegrationTests`, `IdempotencyRecordUniqueIndexIntegrationTests`, `TenantFilterExecuteDeleteTests`, `OrchestrationAdmissionRepositoryTests`, `DatabaseToolTests`) hit **real Postgres** directly via a hardcoded connection string, because EF Core's InMemory provider cannot translate `ExecuteUpdateAsync`, `FOR UPDATE` row locking, or real unique-index constraint violations. `CONTRIBUTING.md` already documents `dotnet test tests/OrchestAI.Tests` as the standard command — meaning **a local Postgres must already be running** for the existing suite to pass; CI's `build-and-test` job just needs to provide that same Postgres, pre-migrated, nothing else changes about how tests run.
- **No `Microsoft.AspNetCore.Mvc.Testing`/`WebApplicationFactory` usage anywhere in this codebase today.** Every existing "integration" test constructs its own `DbContextOptionsBuilder`/`AppDbContext` directly rather than booting the full ASP.NET Core host. This plan does not introduce `WebApplicationFactory` either (see Task 2's design note) — consistent with existing test-authoring conventions, and avoids a first-time new test-infrastructure dependency for a narrow use case.
- **`TenantAuthenticationMiddleware.IsExemptPath` and `RateLimiterSetup.IsExemptPath`** both exempt via `path.StartsWithSegments("/health")` (segment-prefix match) — this already covers `/health/live` and `/health/ready` with **zero code changes needed** to either exemption list; confirmed by reading `PathString.StartsWithSegments`'s segment-boundary semantics (`/health` is a path-segment prefix of `/health/live`).
- **`dotnet-ef` is not preinstalled on GitHub-hosted runners** (confirmed: not present as a global tool in this local shell either, despite `dotnet ef` working once explicitly installed) — every CI job that runs `dotnet ef` must install it first (`dotnet tool install --global dotnet-ef --version 8.0.28`, matching the version already used locally) and add `$HOME/.dotnet/tools` to `$GITHUB_PATH`.
- **`dotnet list package --vulnerable` always exits `0`** regardless of findings (confirmed: NuGet's own tooling design — it's a reporting command, not a gate) — CI must `grep` its text output for `High`/`Critical` severity markers and fail explicitly; there is no built-in `--fail-on-severity` flag.
- **Repo is public** (`jigargajjarcad/orchestai`, confirmed via unauthenticated `GET /repos/jigargajjarcad/orchestai` → `200`, `"private": false`, `"default_branch": "main"`) — GitHub Actions run logs and the Actions API are publicly readable with no token, which matters for Task 14's scratch-branch proof (can poll run status without auth). `gh` CLI is **not installed** in this shell — Tasks 13/14's live-GitHub-interaction steps are written for either `gh` (if the user installs/authenticates it) or the web UI, and are explicitly gated for the user's participation rather than run unattended.
- **GitHub secret scanning / push protection is a repo Settings toggle, not something expressible in workflow YAML** — cannot be verified via the public unauthenticated API (that field is only visible to an authenticated admin). Task 13 is a manual-verification checklist item against `https://github.com/jigargajjarcad/orchestai/settings/security_analysis`, not an automated CI check.

---

## Blocking-confirmation answers (all 10 resolved by the brief itself; restated here only where this investigation adds a concrete implementation detail)

1. **CI trigger:** `on: push: branches: [main]` plus `workflow_dispatch` (the latter added solely to support Task 14's scratch-branch proof without touching the `push` trigger — see Global Constraints).
2. **Health split:** `/health/live` — zero dependencies, always `200`. `/health/ready` — backed by `IReadinessChecker`, `200` if DB reachable and no pending migrations, else `503` with a JSON `{status, reason, timestamp}` body. `railway.json`'s `healthcheckPath` moves from `/health` to `/health/ready`.
3. **Migration validation, both paths, proven by schema convergence, not just test outcomes:** two Postgres `services:` containers in one job (`postgres-fresh` on host port 5432, `postgres-upgrade` on host port 5433) so both final schemas can be diffed directly at the end via `pg_dump --schema-only`. Full test suite runs a second time against the upgrade-path DB (port 5433) in addition to the normal `build-and-test` job's run against a single fresh DB.
4. **Container build validates the real artifact:** plain `docker build -f Dockerfile .` from repo root, no build args, no CI-only Dockerfile — see Global Constraints.
5. **Security scanning:** `dotnet list package --vulnerable --include-transitive` (grepped for `High`/`Critical`), `aquasecurity/trivy-action` against the built image (`HIGH,CRITICAL`, `ignore-unfixed: true` — see Task 9's design note on why), a manual verification checklist item for secret scanning/push protection (Task 13), `.github/dependabot.yml` (Task 5).
6. **Branch protection:** required status checks = the 4 job IDs (`build-and-test`, `migration-validation`, `container-smoke-test`, `security-scan`), `required_linear_history: true`, `allow_force_pushes: false`, no required-PR-reviews setting (stays `null` — no PR gate). Applied via `gh api` in Task 13, gated for explicit user confirmation first (a live repo-settings change).
7. **Hosted-service readiness:** investigated and ruled out — see Investigation summary above. Documented as an explicit finding in `ADR-016`, no readiness-gating code added for it.
8. **Migration reversibility policy:** enforced going forward by a new static-analysis test (Task 4); no existing migration needs a code change (see Investigation summary).
9. **Artifact retention:** every CI job redirects its own diagnostic command output to files and uploads them via `actions/upload-artifact@v4` with `if: always()` (not just on failure — small, useful as a clean-run audit trail too, and simpler than branching upload logic by outcome).
10. **Fail-fast config:** new `RequiredConfigurationValidator` (Infrastructure layer, single choke point, called as the first line of `AddInfrastructure`) validates `ConnectionStrings:DefaultConnection` and `Anthropic:ApiKey` together, throwing one `InvalidOperationException` listing every missing key at once. Paired with the `Program.cs` exit-code fix (Task 1) so a validation failure actually surfaces as a failed container to Railway's restart policy, not a silent exit-0.

---

## Task 1: Fail-fast configuration validation + `Program.cs` exit-code and `HostAbortedException` fix

**Files:**
- Create: `src/OrchestAI.Infrastructure/Configuration/RequiredConfigurationValidator.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (replace the ad-hoc `Anthropic:ApiKey` throw with a call to the new validator)
- Modify: `src/OrchestAI.API/Program.cs` (exit-code + `HostAbortedException` fix)
- Test: `tests/OrchestAI.Tests/Infrastructure/RequiredConfigurationValidatorTests.cs`

**Interfaces:**
- Produces: `OrchestAI.Infrastructure.Configuration.RequiredConfigurationValidator.Validate(IConfiguration configuration)` — `static void`, throws `InvalidOperationException` listing every missing/blank required key, does nothing if all are present. Called by `AddInfrastructure` (Task 2 and later tasks rely on `AddInfrastructure` continuing to fail fast the same way it does today, just centralized).

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Configuration;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Infrastructure;

public sealed class RequiredConfigurationValidatorTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Validate_AllRequiredKeysPresent_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme",
            ["Anthropic:ApiKey"] = "sk-ant-real-key"
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MissingConnectionString_ThrowsWithClearMessage()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "sk-ant-real-key"
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:DefaultConnection*");
    }

    [Fact]
    public void Validate_MissingAnthropicApiKey_ThrowsWithClearMessage()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme"
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Anthropic:ApiKey*");
    }

    [Fact]
    public void Validate_BlankValues_AreTreatedAsMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "   ",
            ["Anthropic:ApiKey"] = ""
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:DefaultConnection*")
            .WithMessage("*Anthropic:ApiKey*");
    }

    [Fact]
    public void Validate_MultipleMissingKeys_ListsAllOfThemInOneException()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => RequiredConfigurationValidator.Validate(config);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("ConnectionStrings:DefaultConnection");
        exception.Message.Should().Contain("Anthropic:ApiKey");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~RequiredConfigurationValidatorTests"`
Expected: FAIL to compile — `RequiredConfigurationValidator` does not exist yet.

- [ ] **Step 3: Write `RequiredConfigurationValidator`**

```csharp
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
```

- [ ] **Step 4: Wire the validator into `AddInfrastructure` and remove the old ad-hoc check**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, add `using OrchestAI.Infrastructure.Configuration;` (already implicitly in scope since the file is under that root namespace's sibling — confirm the existing `using OrchestAI.Infrastructure.Configuration;` line is already present; it is, line 13) and change the top of `AddInfrastructure`:

```csharp
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        RequiredConfigurationValidator.Validate(configuration);

        services.AddSingleton<ICurrentTenantAccessor, AsyncLocalCurrentTenantAccessor>();
```

Then remove the old inline check further down and simplify the `apiKey` read (the value is now guaranteed non-blank by the validator above):

```csharp
        services.AddSingleton(new AnthropicClient(new APIAuthentication(configuration["Anthropic:ApiKey"]!)));
```

removing the old:
```csharp
        var apiKey = configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException(
                "Anthropic:ApiKey is not configured. Set the Anthropic__ApiKey environment variable.");

        services.AddSingleton(new AnthropicClient(new APIAuthentication(apiKey)));
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~RequiredConfigurationValidatorTests"`
Expected: PASS, 5/5.

- [ ] **Step 6: Fix `Program.cs`'s exit code and `HostAbortedException` handling**

In `src/OrchestAI.API/Program.cs`, change:

```csharp
catch (Exception ex)
{
    Log.Fatal(ex, "OrchestAI API terminated unexpectedly");
}
```

to:

```csharp
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is deliberately excluded: EF Core design-time tooling (dotnet ef
    // migrations add/list, database update) builds this host via reflection and throws this
    // specific exception as its documented mechanism for capturing the built host without
    // actually running it — the tool already got what it needed via a DiagnosticListener
    // side-channel by this point. Catching it here and setting ExitCode=1 would poison every
    // single `dotnet ef` invocation's exit code (they run in-process via reflection, not as a
    // subprocess, so Environment.ExitCode is one shared mutable value), silently breaking any
    // CI step that checks `dotnet ef`'s exit code — confirmed by actually running the command,
    // not assumed. See docs/superpowers/plans/2026-07-17-week12-delivery-operational-safety.md
    // Task 1 investigation note.
    Log.Fatal(ex, "OrchestAI API terminated unexpectedly");
    Environment.ExitCode = 1;
}
```

`HostAbortedException` resolves without a new `using` — `Microsoft.Extensions.Hosting` is already an implicit global using for `Microsoft.NET.Sdk.Web` projects (confirmed: the SDK's implicit-usings list includes it).

- [ ] **Step 7: Empirically verify the `HostAbortedException` fix does not break `dotnet ef` tooling**

Run: `dotnet ef migrations list --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API` (install the tool first if needed: `dotnet tool install --global dotnet-ef --version 8.0.28`, ensure `$HOME/.dotnet/tools` is on `PATH`)
Expected: prints all 12 migration names, **no** `"OrchestAI API terminated unexpectedly"` Fatal log line this time, and the shell's own exit code is `0` (`echo $?` immediately after).

- [ ] **Step 8: Build and run the full suite**

Run: `dotnet build OrchestAI.sln` — expect 0 warnings, 0 errors.
Run: `dotnet test tests/OrchestAI.Tests` — expect 409/409 passing (404 baseline + 5 new).

- [ ] **Step 9: Commit**

```bash
git add src/OrchestAI.Infrastructure/Configuration/RequiredConfigurationValidator.cs \
        src/OrchestAI.Infrastructure/DependencyInjection.cs \
        src/OrchestAI.API/Program.cs \
        tests/OrchestAI.Tests/Infrastructure/RequiredConfigurationValidatorTests.cs
git commit -m "feat: centralize fail-fast required-configuration validation; fix Program.cs exit code and HostAbortedException handling"
```

---

## Task 2: `IReadinessChecker` / `DatabaseReadinessChecker`

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IReadinessChecker.cs`
- Create: `src/OrchestAI.Domain/Models/ReadinessResult.cs`
- Create: `src/OrchestAI.Infrastructure/Data/DatabaseReadinessChecker.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register `IReadinessChecker`)
- Test: `tests/OrchestAI.Tests/Infrastructure/DatabaseReadinessCheckerTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (existing, directly constructor-injectable as Scoped — confirmed, see Investigation summary).
- Produces: `OrchestAI.Domain.Interfaces.IReadinessChecker.CheckAsync(CancellationToken) : Task<ReadinessResult>`; `OrchestAI.Domain.Models.ReadinessResult(bool IsReady, string? Reason)` — Task 3's `/health/ready` endpoint consumes both directly.

**Design note (why no `WebApplicationFactory`):** This codebase has never used `Microsoft.AspNetCore.Mvc.Testing`/`WebApplicationFactory` — every existing integration test constructs `AppDbContext`/repositories directly (see Investigation summary). Also, `Program.cs` unconditionally auto-migrates on startup *before* serving any request, so a fully-booted host's `/health/ready` can never actually observe "pending migrations" — by the time it could answer a request, the host already brought itself current. The only way to genuinely exercise the "pending migrations → not ready" path is to test `DatabaseReadinessChecker` directly against a database deliberately migrated to one migration behind, bypassing the app's own auto-migrate. This test therefore targets the checker component directly, not the HTTP endpoint; Task 8's `build-and-test` job and Task 10's `container-smoke-test` job independently prove the HTTP wiring itself works end-to-end for the healthy path.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Uses a dedicated, disposable database (orchestai_readiness_test) rather than the shared
// `orchestai` database every other integration test in this project relies on — this test
// deliberately migrates a database backward (to a known prior migration) and forward again,
// which would corrupt the shared dev database for every concurrently-running test if done
// in-place. The connecting role (orchestai) is the docker-compose Postgres image's bootstrap
// superuser, so CREATE DATABASE/DROP DATABASE both succeed without extra grants.
public sealed class DatabaseReadinessCheckerTests : IAsyncLifetime
{
    private const string AdminConnectionString =
        "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=orchestai_readiness_test;Username=orchestai;Password=changeme";

    // The migration immediately before the current latest — see
    // src/OrchestAI.Infrastructure/Migrations/. If a later week adds new migrations, this still
    // proves the same behavior (some known-older point reports NotReady; latest reports Ready);
    // it doesn't need updating unless this specific migration is ever removed.
    private const string PriorMigration = "20260715084549_AddIdempotencyRecords";

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using (var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS orchestai_readiness_test WITH (FORCE)", admin))
            await drop.ExecuteNonQueryAsync();
        await using (var create = new NpgsqlCommand("CREATE DATABASE orchestai_readiness_test", admin))
            await create.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS orchestai_readiness_test WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private static AppDbContext BuildContext(string connectionString)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        return new AppDbContext(options, accessor);
    }

    [Fact]
    public async Task CheckAsync_PendingMigrations_ReportsNotReady()
    {
        await using var dbContext = BuildContext(TestConnectionString);
        var migrator = dbContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(PriorMigration);

        var checker = new DatabaseReadinessChecker(dbContext, NullLogger<DatabaseReadinessChecker>.Instance);
        var result = await checker.CheckAsync(CancellationToken.None);

        result.IsReady.Should().BeFalse();
        result.Reason.Should().Contain("pending migrations");
    }

    [Fact]
    public async Task CheckAsync_AllMigrationsApplied_ReportsReady()
    {
        await using var dbContext = BuildContext(TestConnectionString);
        var migrator = dbContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync();

        var checker = new DatabaseReadinessChecker(dbContext, NullLogger<DatabaseReadinessChecker>.Instance);
        var result = await checker.CheckAsync(CancellationToken.None);

        result.IsReady.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_DatabaseUnreachable_ReportsNotReady()
    {
        var unreachable =
            "Host=localhost;Port=59999;Database=orchestai;Username=orchestai;Password=changeme;Timeout=2";
        await using var dbContext = BuildContext(unreachable);

        var checker = new DatabaseReadinessChecker(dbContext, NullLogger<DatabaseReadinessChecker>.Instance);
        var result = await checker.CheckAsync(CancellationToken.None);

        result.IsReady.Should().BeFalse();
        result.Reason.Should().Be("database unreachable");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~DatabaseReadinessCheckerTests"`
Expected: FAIL to compile — `ReadinessResult`/`DatabaseReadinessChecker` do not exist yet.

- [ ] **Step 3: Write `ReadinessResult` and `IReadinessChecker`**

```csharp
namespace OrchestAI.Domain.Models;

public sealed record ReadinessResult(bool IsReady, string? Reason);
```

```csharp
namespace OrchestAI.Domain.Interfaces;

using OrchestAI.Domain.Models;

public interface IReadinessChecker
{
    Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Write `DatabaseReadinessChecker`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Data;

// Backs /health/ready (see Program.cs). Checks DB connectivity first, then pending migrations —
// in that order, since GetPendingMigrationsAsync itself needs a working connection and would
// throw a less clear error if the DB is simply unreachable.
public sealed class DatabaseReadinessChecker : IReadinessChecker
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DatabaseReadinessChecker> _logger;

    public DatabaseReadinessChecker(AppDbContext dbContext, ILogger<DatabaseReadinessChecker> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        bool canConnect;
        try
        {
            canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness check: database connection attempt threw");
            return new ReadinessResult(false, "database unreachable");
        }

        if (!canConnect)
            return new ReadinessResult(false, "database unreachable");

        List<string> pending;
        try
        {
            pending = (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness check: failed to read pending migrations");
            return new ReadinessResult(false, "unable to determine migration state");
        }

        return pending.Count == 0
            ? new ReadinessResult(true, null)
            : new ReadinessResult(false, $"pending migrations: {string.Join(", ", pending)}");
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, add near the other `Scoped` repository registrations:

```csharp
        services.AddScoped<IReadinessChecker, DatabaseReadinessChecker>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~DatabaseReadinessCheckerTests"`
Expected: PASS, 3/3. (Requires local Postgres running — `docker compose up -d postgres`.)

- [ ] **Step 7: Run the full suite**

Run: `dotnet build OrchestAI.sln && dotnet test tests/OrchestAI.Tests`
Expected: 0 warnings, 412/412 passing (409 from Task 1 + 3 new).

- [ ] **Step 8: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IReadinessChecker.cs \
        src/OrchestAI.Domain/Models/ReadinessResult.cs \
        src/OrchestAI.Infrastructure/Data/DatabaseReadinessChecker.cs \
        src/OrchestAI.Infrastructure/DependencyInjection.cs \
        tests/OrchestAI.Tests/Infrastructure/DatabaseReadinessCheckerTests.cs
git commit -m "feat: add IReadinessChecker/DatabaseReadinessChecker backing the new readiness endpoint"
```

---

## Task 3: Split `/health/live` and `/health/ready`; update `railway.json`

**Files:**
- Modify: `src/OrchestAI.API/Program.cs`
- Modify: `railway.json`

**Interfaces:**
- Consumes: `OrchestAI.Domain.Interfaces.IReadinessChecker` (Task 2).

- [ ] **Step 1: Replace the single `/health` endpoint**

In `src/OrchestAI.API/Program.cs`, replace:

```csharp
    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));
```

with:

```csharp
    // Liveness: process is up, full stop — no dependency on the database or anything else.
    // Railway/any orchestrator should never restart a container based on this alone failing
    // for a reason unrelated to the process itself being wedged.
    app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTimeOffset.UtcNow }));

    // Readiness: only 200 if the DB is reachable AND there are no pending EF Core migrations
    // (the running code's schema expectations match what's actually in the database). This is
    // what railway.json's healthcheckPath points at — Railway must not route traffic to a
    // container that isn't actually able to serve a real request yet.
    app.MapGet("/health/ready", async (IReadinessChecker readinessChecker, CancellationToken cancellationToken) =>
    {
        var result = await readinessChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        return result.IsReady
            ? Results.Ok(new { status = "ready", timestamp = DateTimeOffset.UtcNow })
            : Results.Json(
                new { status = "not_ready", reason = result.Reason, timestamp = DateTimeOffset.UtcNow },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    });
```

Add `using OrchestAI.Domain.Interfaces;` to the top of `Program.cs` if not already present (it is not — confirm via the existing `using` block and add it alongside the other `OrchestAI.*` usings).

- [ ] **Step 2: Update `railway.json`**

In `railway.json`, change:

```json
    "healthcheckPath": "/health",
```

to:

```json
    "healthcheckPath": "/health/ready",
```

- [ ] **Step 3: Build and smoke-test locally**

Run: `dotnet build OrchestAI.sln` — expect 0 warnings.
Run (with local Postgres up, migrated): `dotnet run --project src/OrchestAI.API` in one terminal, then in another:
```bash
curl -i http://localhost:8080/health/live
curl -i http://localhost:8080/health/ready
```
Expected: both `200`, `/health/ready` body includes `"status":"ready"`. Stop the local Postgres container (`docker compose stop postgres`) and re-curl `/health/ready` — expect `503` with `"status":"not_ready","reason":"database unreachable"`; `/health/live` still `200`. Restart Postgres (`docker compose start postgres`) before continuing.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: 412/412 passing (no new tests this task — this task's correctness is proven by Step 3's live curl check and Task 10's automated container smoke test, per Task 2's design note).

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.API/Program.cs railway.json
git commit -m "feat: split /health into /health/live and /health/ready; point Railway's healthcheck at /health/ready"
```

---

## Task 4: Migration reversibility audit test

**Files:**
- Create: `tests/OrchestAI.Tests/Architecture/MigrationReversibilityTests.cs`

**Interfaces:**
- None new — this is a static-analysis test over existing migration source files.

- [ ] **Step 1: Write the test**

```csharp
using FluentAssertions;

namespace OrchestAI.Tests.Architecture;

// Enforces the migration-reversibility policy from ADR-016: every migration's Down() must either
// perform real migrationBuilder work (purely additive, structurally reversible) or throw
// NotSupportedException with a documented reason (irreversible — data transformation, destructive
// change). All 12 migrations that exist as of this test's introduction are purely additive and
// already have EF-generated, fully-reversible Down() bodies (confirmed by reading every one) — this
// test exists to catch the FIRST future migration that ships an empty or thoughtless Down(), not
// because any current migration violates the policy.
public sealed class MigrationReversibilityTests
{
    [Fact]
    public void EveryMigration_DeclaresExplicitDownBehavior()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).Equals("AppDbContextModelSnapshot.cs", StringComparison.Ordinal))
            .ToList();

        migrationFiles.Should().NotBeEmpty("the migrations source directory must resolve to real files");

        var violations = new List<string>();
        foreach (var file in migrationFiles)
        {
            var downBody = ExtractDownBody(File.ReadAllText(file));

            var hasRealWork = downBody.Contains("migrationBuilder.", StringComparison.Ordinal);
            var hasDocumentedThrow = downBody.Contains("throw new NotSupportedException(", StringComparison.Ordinal);

            if (!hasRealWork && !hasDocumentedThrow)
                violations.Add(Path.GetFileName(file));
        }

        violations.Should().BeEmpty(
            "every migration's Down() must either perform real migrationBuilder work (reversible) " +
            "or throw NotSupportedException with a documented reason (irreversible) — see ADR-016. " +
            $"Violating files: {string.Join(", ", violations)}");
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrchestAI.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate repository root (OrchestAI.sln) from " + AppContext.BaseDirectory);

        return Path.Combine(dir.FullName, "src", "OrchestAI.Infrastructure", "Migrations");
    }

    private static string ExtractDownBody(string source)
    {
        const string marker = "protected override void Down(MigrationBuilder migrationBuilder)";
        var startIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (startIndex < 0)
            return string.Empty;

        var braceStart = source.IndexOf('{', startIndex);
        var depth = 0;
        var i = braceStart;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        return source.Substring(braceStart, i - braceStart + 1);
    }
}
```

- [ ] **Step 2: Run the test to verify it passes against the current 12 migrations**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~MigrationReversibilityTests"`
Expected: PASS, 1/1 (proves the static analysis correctly reads all 12 existing, already-reversible migrations with zero violations).

- [ ] **Step 3: Prove the test actually catches a violation (masking proof)**

Temporarily create a throwaway file `src/OrchestAI.Infrastructure/Migrations/99999999999999_ScratchViolation.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

namespace OrchestAI.Infrastructure.Migrations;

public partial class ScratchViolation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // deliberately empty — should be caught
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~MigrationReversibilityTests"`
Expected: FAIL, listing `99999999999999_ScratchViolation.cs` in the violations message.

Delete the scratch file (`rm src/OrchestAI.Infrastructure/Migrations/99999999999999_ScratchViolation.cs`) and re-run — expect PASS again.

- [ ] **Step 4: Run the full suite**

Run: `dotnet build OrchestAI.sln && dotnet test tests/OrchestAI.Tests`
Expected: 0 warnings, 413/413 passing.

- [ ] **Step 5: Commit**

```bash
git add tests/OrchestAI.Tests/Architecture/MigrationReversibilityTests.cs
git commit -m "test: add migration-reversibility policy audit test"
```

---

## Task 5: `.github/dependabot.yml`

**Files:**
- Create: `.github/dependabot.yml`

- [ ] **Step 1: Write the config**

```yaml
version: 2
updates:
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
    open-pull-requests-limit: 10
    labels:
      - "dependencies"

  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    labels:
      - "dependencies"
      - "ci"
```

- [ ] **Step 2: Validate YAML syntax**

Run: `python3 -c "import yaml, sys; yaml.safe_load(open('.github/dependabot.yml'))" && echo "valid YAML"`
Expected: `valid YAML`.

- [ ] **Step 3: Commit**

```bash
git add .github/dependabot.yml
git commit -m "chore: enable Dependabot for NuGet and GitHub Actions dependencies"
```

(Dependabot itself only activates once this file is on the default branch — no further local verification possible; its PRs will appear in the GitHub UI over the following days. This is the one deliberate, expected exception to this project's otherwise PR-less workflow, per confirmation #5 — documented in `ADR-016`.)

---

## Task 6: `ci.yml` — `build-and-test` job

**Files:**
- Create: `.github/workflows/ci.yml` (this task creates the file with just this one job; Tasks 7-9 add the remaining jobs to the same file)

- [ ] **Step 1: Write the workflow file with the `build-and-test` job**

```yaml
name: CI

on:
  push:
    branches: [main]
  workflow_dispatch:

env:
  DOTNET_VERSION: "8.0.x"
  DOTNET_NOLOGO: "true"
  DOTNET_CLI_TELEMETRY_OPTOUT: "true"

jobs:
  build-and-test:
    name: build-and-test
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_DB: orchestai
          POSTGRES_USER: orchestai
          POSTGRES_PASSWORD: changeme
        ports:
          - 5432:5432
        options: >-
          --health-cmd "pg_isready -U orchestai"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 10

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Install dotnet-ef
        run: |
          dotnet tool install --global dotnet-ef --version 8.0.28
          echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"

      - name: Restore
        run: dotnet restore OrchestAI.sln

      - name: Build
        run: dotnet build OrchestAI.sln --no-restore --configuration Release -warnaserror

      - name: Apply migrations
        run: >
          dotnet ef database update
          --project src/OrchestAI.Infrastructure
          --startup-project src/OrchestAI.API
          --connection "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme"
        env:
          Anthropic__ApiKey: "sk-ant-ci-placeholder"

      - name: Test
        run: >
          dotnet test tests/OrchestAI.Tests
          --no-restore
          --configuration Release
          --logger "trx;LogFileName=test-results.trx"
          --results-directory ./TestResults
        env:
          Anthropic__ApiKey: "sk-ant-ci-placeholder"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: build-and-test-results
          path: TestResults/**/*.trx
          retention-days: 14
```

Notes baked into this step:
- `-warnaserror` on the build step is redundant with `TreatWarningsAsErrors` already set in every `.csproj` (confirmed in `OrchestAI.API.csproj`/`OrchestAI.Infrastructure.csproj`), kept anyway as an explicit, visible CI-level guarantee rather than relying solely on project-file state.
- `dotnet ef database update` needs `Anthropic__ApiKey` set even though it never calls Anthropic — `AddInfrastructure` (Task 1's `RequiredConfigurationValidator`) runs as part of building the design-time host, so it must see a non-blank placeholder value or the command fails at the validator, not at the DB.
- No `Environment.ExitCode`/`HostAbortedException` concern here (Task 1 already resolved it) — `dotnet ef database update`'s own exit code is trustworthy.

- [ ] **Step 2: Validate YAML syntax locally**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))" && echo "valid YAML"`
Expected: `valid YAML`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add build-and-test job (real Postgres service, migrations, full suite)"
```

(This job's actual pass/fail behavior is only provable by pushing to `main` or triggering `workflow_dispatch` — deferred to Task 14's scratch-branch proof, which exercises the complete workflow file after Tasks 7-9 add the remaining jobs.)

---

## Task 7: `ci.yml` — `migration-validation` job

**Files:**
- Modify: `.github/workflows/ci.yml` (add a second job)

- [ ] **Step 1: Add the `migration-validation` job**

Append to the `jobs:` section of `.github/workflows/ci.yml`:

```yaml
  migration-validation:
    name: migration-validation
    runs-on: ubuntu-latest

    services:
      postgres-fresh:
        image: postgres:16-alpine
        env:
          POSTGRES_DB: orchestai
          POSTGRES_USER: orchestai
          POSTGRES_PASSWORD: changeme
        ports:
          - 5432:5432
        options: >-
          --health-cmd "pg_isready -U orchestai"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 10
      postgres-upgrade:
        image: postgres:16-alpine
        env:
          POSTGRES_DB: orchestai
          POSTGRES_USER: orchestai
          POSTGRES_PASSWORD: changeme
        ports:
          - 5433:5432
        options: >-
          --health-cmd "pg_isready -U orchestai"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 10

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Install dotnet-ef and postgresql-client
        run: |
          dotnet tool install --global dotnet-ef --version 8.0.28
          echo "$HOME/.dotnet/tools" >> "$GITHUB_PATH"
          sudo apt-get update && sudo apt-get install -y postgresql-client

      - name: Restore and build
        run: |
          dotnet restore OrchestAI.sln
          dotnet build OrchestAI.sln --no-restore --configuration Release

      - name: Determine prior migration
        id: migrations
        run: |
          export PATH="$PATH:$HOME/.dotnet/tools"
          dotnet ef migrations list \
            --project src/OrchestAI.Infrastructure \
            --startup-project src/OrchestAI.API \
            --no-build > migrations-list-raw.txt 2>&1 || true
          grep -E '^[0-9]{14}_' migrations-list-raw.txt > migrations-list.txt
          LATEST=$(tail -1 migrations-list.txt)
          PRIOR=$(tail -2 migrations-list.txt | head -1)
          echo "latest=$LATEST" >> "$GITHUB_OUTPUT"
          echo "prior=$PRIOR" >> "$GITHUB_OUTPUT"
          echo "Latest migration: $LATEST"
          echo "Prior migration: $PRIOR"
        env:
          Anthropic__ApiKey: "sk-ant-ci-placeholder"
          ConnectionStrings__DefaultConnection: "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme"

      - name: Scenario (a) — fresh install, apply all migrations to an empty database
        run: >
          dotnet ef database update
          --project src/OrchestAI.Infrastructure
          --startup-project src/OrchestAI.API
          --connection "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme"
          > fresh-migration-output.txt 2>&1
        env:
          Anthropic__ApiKey: "sk-ant-ci-placeholder"

      - name: Scenario (b) — upgrade path, migrate to prior version then apply latest
        run: |
          export PATH="$PATH:$HOME/.dotnet/tools"
          dotnet ef database update "${{ steps.migrations.outputs.prior }}" \
            --project src/OrchestAI.Infrastructure \
            --startup-project src/OrchestAI.API \
            --connection "Host=localhost;Port=5433;Database=orchestai;Username=orchestai;Password=changeme" \
            > upgrade-migration-step1-output.txt 2>&1
          dotnet ef database update \
            --project src/OrchestAI.Infrastructure \
            --startup-project src/OrchestAI.API \
            --connection "Host=localhost;Port=5433;Database=orchestai;Username=orchestai;Password=changeme" \
            > upgrade-migration-step2-output.txt 2>&1
        env:
          Anthropic__ApiKey: "sk-ant-ci-placeholder"

      - name: Run full test suite against the upgrade-path database
        run: >
          dotnet test tests/OrchestAI.Tests
          --no-build
          --configuration Release
          --logger "trx;LogFileName=upgrade-path-test-results.trx"
          --results-directory ./TestResults
        env:
          Anthropic__ApiKey: "sk-ant-ci-placeholder"
          ConnectionStrings__DefaultConnection: "Host=localhost;Port=5433;Database=orchestai;Username=orchestai;Password=changeme"

      - name: Verify fresh-install and upgrade-path schemas converge
        run: |
          PGPASSWORD=changeme pg_dump --schema-only --no-owner --no-privileges \
            -h localhost -p 5432 -U orchestai orchestai > fresh-schema.sql
          PGPASSWORD=changeme pg_dump --schema-only --no-owner --no-privileges \
            -h localhost -p 5433 -U orchestai orchestai > upgrade-schema.sql
          if ! diff -u fresh-schema.sql upgrade-schema.sql > schema-diff.txt; then
            echo "Fresh-install and upgrade-path schemas DO NOT converge:"
            cat schema-diff.txt
            exit 1
          fi
          echo "Schemas converge — fresh-install and upgrade-path are structurally identical."

      - name: Upload migration validation artifacts
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: migration-validation-artifacts
          path: |
            migrations-list-raw.txt
            fresh-migration-output.txt
            upgrade-migration-step1-output.txt
            upgrade-migration-step2-output.txt
            fresh-schema.sql
            upgrade-schema.sql
            schema-diff.txt
            TestResults/**/*.trx
          retention-days: 14
```

Design notes baked into this job:
- Two separate `services:` Postgres containers on different host ports (`5432`, `5433`) run for the whole job, so both final schemas can be `pg_dump`'d and diffed directly at the end without tearing anything down mid-job.
- `dotnet ef migrations list`'s own stdout is intentionally captured with `|| true` and then filtered with `grep -E '^[0-9]{14}_'` — this is robust against the Serilog bootstrap-logger noise (JSON lines starting with `{`) that the design-time host prints before/around the actual migration list, confirmed locally in this plan's investigation phase (see Task 1's investigation note); genuine migration IDs are the only lines matching a 14-digit-prefix anchor.
- `diff -u` producing any output means the schemas diverge — a stray extra index or subtly different constraint would show up here even if every test on both paths passed, directly satisfying confirmation #3's "prove schema convergence, not just test success."

- [ ] **Step 2: Validate YAML syntax locally**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))" && echo "valid YAML"`
Expected: `valid YAML`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add migration-validation job (fresh-install + upgrade path, schema-convergence proof)"
```

---

## Task 8: `ci.yml` — `container-smoke-test` job

**Files:**
- Modify: `.github/workflows/ci.yml` (add a third job)

- [ ] **Step 1: Add the `container-smoke-test` job**

Append to the `jobs:` section:

```yaml
  container-smoke-test:
    name: container-smoke-test
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:16-alpine
        env:
          POSTGRES_DB: orchestai
          POSTGRES_USER: orchestai
          POSTGRES_PASSWORD: changeme
        ports:
          - 5432:5432
        options: >-
          --health-cmd "pg_isready -U orchestai"
          --health-interval 5s
          --health-timeout 5s
          --health-retries 10

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      # Builds the exact root Dockerfile with no build args, no CI-only Dockerfile — the same
      # artifact Railway builds from railway.json's "dockerfilePath": "Dockerfile". See
      # confirmation #4 and this plan's Global Constraints.
      - name: Build production image
        run: docker build -f Dockerfile -t orchestai-api:ci .

      - name: Run container
        run: |
          docker run -d --name orchestai-smoke \
            --network host \
            -e PORT=8080 \
            -e ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme" \
            -e Anthropic__ApiKey="sk-ant-ci-placeholder" \
            orchestai-api:ci

      - name: Poll /health/ready until healthy or timeout
        run: |
          for i in $(seq 1 30); do
            if curl -sf http://localhost:8080/health/ready -o health-ready-response.json; then
              echo "container reported ready after $((i * 2))s"
              cat health-ready-response.json
              exit 0
            fi
            echo "not ready yet (attempt $i/30)"
            sleep 2
          done
          echo "TIMED OUT waiting for /health/ready"
          exit 1

      - name: Verify /health/live independently
        run: curl -sf http://localhost:8080/health/live | tee health-live-response.json

      - name: Capture container logs
        if: always()
        run: docker logs orchestai-smoke > container-smoke-logs.txt 2>&1 || true

      - name: Tear down container
        if: always()
        run: |
          docker stop orchestai-smoke || true
          docker rm orchestai-smoke || true

      - name: Upload smoke-test artifacts
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: container-smoke-test-artifacts
          path: |
            container-smoke-logs.txt
            health-ready-response.json
            health-live-response.json
          retention-days: 14
```

Design notes baked into this job:
- `--network host` on the `docker run` step is the standard, documented way for a manually-`docker run` container on a GitHub-hosted Linux runner to reach a `services:` container via `localhost` — GH-hosted runners execute steps directly on the VM (not nested), so service containers are bound to the runner's own network namespace, which `--network host` shares directly. No `host.docker.internal` trick needed (that's a Docker Desktop/macOS mechanism, not applicable to GH-hosted Linux runners).
- The container's own startup path (`Program.cs`'s `await dbContext.Database.MigrateAsync()`) is what actually migrates this database — this job does **not** pre-migrate, since proving the container migrates itself and becomes ready unattended is the entire point of the smoke test, mirroring a real fresh Railway deploy.
- 30 attempts × 2s = 60s timeout budget, comfortably above `railway.json`'s own `healthcheckTimeout: 30` per attempt — generous enough to absorb a slow migration on a loaded CI runner without being effectively unbounded.

- [ ] **Step 2: Validate YAML syntax locally**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))" && echo "valid YAML"`
Expected: `valid YAML`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add container-smoke-test job (real Dockerfile, polls /health/ready)"
```

---

## Task 9: `ci.yml` — `security-scan` job

**Files:**
- Modify: `.github/workflows/ci.yml` (add a fourth job)

- [ ] **Step 1: Add the `security-scan` job**

Append to the `jobs:` section:

```yaml
  security-scan:
    name: security-scan
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Restore
        run: dotnet restore OrchestAI.sln

      # `dotnet list package --vulnerable` always exits 0 regardless of findings — it's a
      # reporting command, not a gate. This step greps its own output for High/Critical severity
      # markers and fails explicitly, since there is no built-in --fail-on-severity flag.
      - name: Scan NuGet packages for known vulnerabilities
        run: |
          dotnet list OrchestAI.sln package --vulnerable --include-transitive 2>&1 | tee vulnerable-packages.txt
          if grep -qE '\s(High|Critical)\s' vulnerable-packages.txt; then
            echo "High or Critical severity vulnerable package(s) found — see vulnerable-packages.txt"
            exit 1
          fi
          echo "No High/Critical severity vulnerable packages found."

      - name: Build production image for container scan
        run: docker build -f Dockerfile -t orchestai-api:ci .

      # ignore-unfixed:true is deliberate — scanning any real-world base image (including
      # Microsoft's own aspnet:8.0) routinely turns up OS-level CVEs with no fix published yet;
      # failing CI on those would make this gate permanently red for reasons outside this
      # project's control. HIGH/CRITICAL with a known fix is the actionable signal this scan
      # exists to catch.
      - name: Scan container image with Trivy
        uses: aquasecurity/trivy-action@0.28.0
        with:
          image-ref: orchestai-api:ci
          format: table
          output: trivy-results.txt
          severity: HIGH,CRITICAL
          ignore-unfixed: true
          exit-code: "1"

      - name: Upload security-scan artifacts
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: security-scan-artifacts
          path: |
            vulnerable-packages.txt
            trivy-results.txt
          retention-days: 14
```

- [ ] **Step 2: Validate YAML syntax locally**

Run: `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))" && echo "valid YAML"`
Expected: `valid YAML`.

- [ ] **Step 3: Confirm the vulnerable-package scan runs cleanly against this repo today**

Run: `dotnet list OrchestAI.sln package --vulnerable --include-transitive`
Expected: `has no vulnerable packages` for every project (already confirmed during this plan's investigation phase) — this step is a sanity check that the command itself still behaves the same way immediately before wiring it into CI, not a new finding.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add security-scan job (dotnet list package --vulnerable, Trivy container scan)"
```

---

## Task 10: README CI badge

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add the CI badge**

In `README.md`, change:

```markdown
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Tests](https://img.shields.io/badge/tests-182%20passing-16a34a)](tests/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
```

to:

```markdown
[![CI](https://github.com/jigargajjarcad/orchestai/actions/workflows/ci.yml/badge.svg)](https://github.com/jigargajjarcad/orchestai/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Tests](https://img.shields.io/badge/tests-413%20passing-16a34a)](tests/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
```

(The `Tests` badge count is updated to `413` — the actual count after Tasks 1-4's new tests, confirmed by the last `dotnet test` run in Task 4 Step 4. The stale `182` predates this plan entirely and was already wrong before this week started.)

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add CI status badge, fix stale test-count badge"
```

(The CI badge will render "no status"/unknown until the workflow has run at least once on `main` — expected, resolves itself after Task 14's proof and the next real push.)

---

## Task 11: ADR-016 — Delivery and Operational Safety

**Files:**
- Modify: `DECISIONS.md` (append ADR-016, following the exact structure of ADR-011 through ADR-015: Status, Investigation, numbered Confirmations, Implementation notes, Trigger for revisiting)

- [ ] **Step 1: Append ADR-016 to `DECISIONS.md`**

Append after ADR-015's final line (`... to close Bug #2 (bucket immutability after a limits change), the implementation note directly above.`):

```markdown

## ADR-016: Delivery and Operational Safety

**Status:** Accepted

### Investigation — what already existed vs. what's net-new
Before this week, there was no `.github/` directory at all — no CI, no Dependabot. Health checking
was a single unconditional-200 `/health` endpoint with no DB dependency of any kind. Startup
configuration validation existed for exactly one value (`Anthropic:ApiKey`, thrown from deep inside
`AddInfrastructure`); `ConnectionStrings:DefaultConnection` had none. Every one of the 12 existing
EF Core migrations was purely additive with an already-correct, EF-generated `Down()` — no migration
reversibility policy existed because nothing had yet violated one. `IReadinessChecker`,
`DatabaseReadinessChecker`, `RequiredConfigurationValidator`, and the entire `.github/` tree are
net-new this week.

### Confirmation #1 — CI triggers on push to `main`, not `pull_request`
This project has merged locally and pushed directly to `main` for 11 prior weeks — no PR-based
workflow exists or is being introduced now. A workflow gated only on `pull_request` events would
simply never run in practice. `ci.yml` triggers on `push: branches: [main]`, functioning as a
post-hoc safety net and portfolio signal (a green check on GitHub) rather than a pre-merge gate —
the actual merge gate remains what it has always been: a clean local `dotnet build`/`dotnet test`
before pushing. `workflow_dispatch` is also enabled, solely so a scratch branch can trigger a manual
run (used to prove the gate actually catches failures — see the Tests section of this week's plan)
without altering the `push` trigger itself.

### Confirmation #2 — Liveness/readiness split, and the hosted-service readiness finding
`/health/live` has zero dependencies and always returns `200` — a process that's up but can't reach
its database should still be reported "alive" (it isn't crash-looping) while `/health/ready` alone
governs whether Railway routes traffic to it. `/health/ready` is backed by `IReadinessChecker`
(`DatabaseReadinessChecker` in Infrastructure): `200` only if `Database.CanConnectAsync()` succeeds
**and** `Database.GetPendingMigrationsAsync()` returns empty, else `503` with a `reason`.
`railway.json`'s `healthcheckPath` was moved from `/health` to `/health/ready` so Railway's
redeploy health-gating actually reflects real DB/schema state, not a static always-200 stub.

**Hosted-service readiness — investigated, explicitly ruled out.** Both existing `BackgroundService`s
(`CostRollupBackgroundService`, `EvalRunBackgroundWorker`) were read in full. Neither has any
meaningful asynchronous initialization phase: `CostRollupBackgroundService.ExecuteAsync`'s very
first action inside its loop *is* the actual rollup work (no setup step precedes it);
`EvalRunBackgroundWorker.ExecuteAsync`'s very first statement is `await _queue.DequeueAsync(...)` —
it's immediately ready to process the moment `ExecuteAsync` starts. Neither service has a genuine
"not ready yet" state for `/health/ready` to gate on. No readiness-gating mechanism was built for
this — inventing one for a problem that doesn't exist would be exactly the kind of premature
abstraction `DESIGN_PRINCIPLES.md` argues against. If a future hosted service *does* need real
async init (e.g. warming a large in-memory index before it can safely process anything), this is the
trigger to revisit — see below.

**Why `/health/ready`'s migration check is not tautological despite `Program.cs` always
auto-migrating at startup.** Since `Program.cs` unconditionally calls `dbContext.Database
.MigrateAsync()` before `app.Run()`, a container that's actually serving traffic has, by
definition, already migrated itself — so immediately after startup, the pending-migrations check
will always report zero pending. Its real value is a live drift detector, not a startup gate: if an
operator manually reverts the database schema while the app container keeps running (exactly the
scenario `RUNBOOK.md`'s migration-rollback guidance can require), `/health/ready` correctly flips to
`503` on the very next poll, surfacing the code/schema mismatch instead of silently continuing to
serve requests against a schema the running binary no longer matches.

### Confirmation #3 — Migration validation proves schema convergence, not just test outcomes
The `migration-validation` CI job runs two Postgres services in parallel (`postgres-fresh`,
`postgres-upgrade`, different host ports) so both final schemas can be diffed directly. Scenario
(a): `dotnet ef database update` applied to a genuinely empty database. Scenario (b): migrate to the
migration immediately prior to latest, then apply latest on top — simulating a real production
upgrade — followed by the **full test suite run a second time** against that upgraded database.
Finally, `pg_dump --schema-only` both resulting databases and `diff` them: a non-empty diff fails
the job even if every test on both paths passed, since fresh-install test success alone cannot prove
an upgrade is safe (a constraint that's fine on an empty table but conflicts with existing data is
exactly the class of bug this catches that test-outcome checking alone would miss).

### Confirmation #4 — Container build validates the exact production artifact
The `container-smoke-test` job runs `docker build -f Dockerfile .` from the repo root with no build
args and no CI-only Dockerfile — the identical artifact `railway.json` (`"builder": "DOCKERFILE",
"dockerfilePath": "Dockerfile"`, no `startCommand` override) has Railway build. The built image is
then actually run (`docker run --network host`, real Postgres service, no pre-migration) and polled
against `/health/ready` until healthy or a 60-second timeout — proving the container migrates and
becomes ready unattended, mirroring a real fresh Railway deploy rather than a parallel, potentially
divergent build path.

### Confirmation #5 — Security scanning scope
`dotnet list package --vulnerable --include-transitive` (its own exit code is always `0` regardless
of findings — CI greps the text output for `High`/`Critical` markers and fails explicitly), a Trivy
container-image scan (`HIGH,CRITICAL`, `ignore-unfixed: true` — scanning any real base image
routinely surfaces OS-level CVEs with no published fix yet; failing on those would make the gate
permanently red for reasons outside this project's control), a manual verification checklist item
for GitHub secret scanning/push protection (a repo Settings toggle, not expressible in workflow
YAML — public repos have secret scanning on by default, verified via the repo's Settings →
Code security page, not a custom check), and `.github/dependabot.yml`. Dependabot is the one
deliberate, expected exception to this project's otherwise PR-less workflow — its update PRs are
reviewed and merged locally like everything else, not auto-merged.

### Confirmation #6 — Branch protection scoped to not conflict with the existing workflow
No "PR required before merge" setting — that would break the established local-merge-and-push
habit this project has used for 11 prior weeks. What **is** required on `main`'s HEAD: all four CI
job status checks (`build-and-test`, `migration-validation`, `container-smoke-test`,
`security-scan`), `required_linear_history: true`, and `allow_force_pushes: false`. CI here is a
second, independent, automated confirmation that runs *after* the existing local engineering gate
(a clean worktree build/test before merge) — not a replacement for that discipline.

### Confirmation #7 — Migration reversibility policy
Every migration's `Down()` must either perform real `migrationBuilder` work (purely additive,
structurally reversible — every one of the 12 existing migrations already qualifies) or throw
`NotSupportedException` with a documented reason (irreversible — a data transformation or
destructive change). Enforced going forward by `MigrationReversibilityTests`
(`tests/OrchestAI.Tests/Architecture/`), a static-analysis test over migration source files that
fails the build the moment a future migration ships an empty or thoughtless `Down()` — the same
"enforced by a test, not just by review" pattern `LayeringTests` already established for
architectural layering. **Production rollback does not mean executing migration `Down()`s against a
live database** — see `RUNBOOK.md`: rollback means redeploying the previous application version
against a schema that remains backward-compatible with it, following the same
nullable-→-backfill→-non-null multi-step pattern Week 10 already established for any non-purely-
additive change.

### Confirmation #8 — Fail-fast configuration validation
`RequiredConfigurationValidator.Validate` (Infrastructure, called as the first line of
`AddInfrastructure`) checks `ConnectionStrings:DefaultConnection` and `Anthropic:ApiKey` together,
throwing one `InvalidOperationException` listing every missing/blank key at once — replacing the old
ad-hoc single-key check (`Anthropic:ApiKey` only; `ConnectionStrings:DefaultConnection` had zero
validation and would only ever fail lazily, inside the auto-migrate call, with whatever exception
Npgsql happened to throw). **`Admin:BootstrapSecret` is deliberately excluded from required
validation** — `RequireAdminSecretFilter` already fails gracefully per-request (`503`) when it's
unset, a correct, already-shipped posture for an operator-only admin-bootstrap surface; a fresh
Railway deploy that hasn't configured admin bootstrap yet should still serve normal tenant traffic,
not refuse to start entirely.

A real bug was found and fixed alongside this: `Program.cs`'s top-level `catch (Exception ex)` never
set a non-zero exit code, so a genuine startup failure (bad config, unreachable DB) logged Fatal and
then exited `0` — which would defeat Railway's `restartPolicyType: ON_FAILURE` (a `0` exit reads as
an intentional, successful shutdown, not something to restart from). Fixed by explicitly setting
`Environment.ExitCode = 1` in that catch block. This interacts with a second, independently
discovered bug: EF Core design-time tooling (`dotnet ef migrations list/add`, `database update`)
invokes `Program.Main` via reflection *in the same process* and throws
`Microsoft.Extensions.Hosting.HostAbortedException` as its documented mechanism for capturing the
built host without running it — confirmed by actually running `dotnet ef migrations list` locally
during this week's investigation, not assumed. Adding the exit-code fix to the *unconditional* catch
would have poisoned `Environment.ExitCode` to `1` for every subsequent `dotnet ef` invocation in the
same process (the tool's own successful-completion path never resets that shared mutable value),
silently breaking any CI step that checks `dotnet ef`'s exit code — exactly the failure mode
`DESIGN_PRINCIPLES.md`'s "Empirical verification over plausible-sounding review" section exists to
catch. Fixed with the standard, documented pattern: `catch (Exception ex) when (ex is not
HostAbortedException)`.

### Confirmation #9 — Artifact retention on CI failure
Every job redirects its own diagnostic command output to files (test `.trx`, migration command
output, container logs, health-check polling responses, vulnerability-scan output) and uploads them
via `actions/upload-artifact@v4` with `if: always()` — not conditioned on failure, since a clean
run's artifacts are still useful as an audit trail and this avoids branching upload logic by
job outcome. A bare "container smoke test failed" with nothing to inspect would force a re-run just
to get diagnostic information; this optimizes for investigation speed over minimal storage use (14-
day retention, GitHub Actions' artifact defaults otherwise apply).

### Implementation notes
**A real, empirically-confirmed bug was found purely by running a command, not by reading code.**
See Confirmation #8 above — the `HostAbortedException`/exit-code interaction would not have been
caught by review alone; it was only found by actually running `dotnet ef migrations list` during
this week's investigation phase and observing the unexpected Fatal log line, then reasoning through
why the naive exit-code fix would have made it worse, not better.

### Trigger for revisiting
- The first time a hosted service is added that genuinely needs asynchronous initialization before
  it can safely process work (unlike `CostRollupBackgroundService`/`EvalRunBackgroundWorker` today)
  — build a real readiness-gating mechanism for it at that point, not before.
- The first time CI wall-clock time becomes a bottleneck on this project's own iteration speed —
  revisit whether `migration-validation`'s duplicate full-test-suite run (once in `build-and-test`,
  once against the upgrade-path DB) is worth splitting or caching further.
- The first time a second application (not just Dependabot) needs to open a PR against this
  otherwise PR-less workflow — revisit whether branch protection's `required_linear_history`
  assumption still holds.
```

- [ ] **Step 2: Validate the appended Markdown doesn't break existing structure**

Run: `grep -c "^## ADR-" DECISIONS.md`
Expected: `16` (was `15` before this task).

- [ ] **Step 3: Commit**

```bash
git add DECISIONS.md
git commit -m "docs: add ADR-016 for delivery and operational safety"
```

---

## Task 12: `RUNBOOK.md`

**Files:**
- Create: `RUNBOOK.md`

- [ ] **Step 1: Write `RUNBOOK.md`**

```markdown
# RUNBOOK

Operational recovery guidance for OrchestAI's production deployment (Railway, single API instance +
managed PostgreSQL). Read `ADR-016` (`DECISIONS.md`) for the full reasoning behind every policy
referenced here.

## Incident classification

Start here. Pick the branch that matches what you're actually observing.

| Symptom | Classification | Action |
|---|---|---|
| App was working, a recent deploy broke it, `/health/ready` failing or requests erroring | **Operational failure** | Redeploy the previous successful build/image (see "Rollback: redeploy the previous build" below). |
| `/health/ready` returns `503` with `reason: "database unreachable"`, `/health/live` still `200` | **Infrastructure failure** | Restart/verify the Postgres instance in Railway's dashboard; once reachable, confirm `/health/ready` returns to `200` before assuming recovery. |
| A migration was just deployed and something looks wrong (data-shape errors, constraint violations) | **Migration failure** | Follow "Rollback: migration-aware" below — do **not** just redeploy the previous image without checking schema compatibility first. |
| App crashes immediately, or logs show `Required configuration is missing or blank` at startup | **Configuration failure** | Restore the previous Railway environment variables (compare against `.env.example`); this is exactly what `RequiredConfigurationValidator` (ADR-016 confirmation #8) is designed to make loud and immediate rather than a mysterious runtime crash. |

## Rollback: redeploy the previous build

Railway's redeploy-previous-build feature is the rollback mechanism for this project — there is no
blue-green/canary deployment strategy (deliberately out of scope, see ADR-016). In the Railway
dashboard: **Deployments → find the last known-good deployment → Redeploy**. This only works safely
because of the migration-compatibility rule below — if it doesn't hold for the deploy you're rolling
back past, do the migration-aware rollback instead.

## The migration-compatibility rule that makes rollback safe

**Schema changes must remain backward-compatible with the immediately-prior application version.**
A rollback redeploys old *code* against whatever schema is currently live — it does not touch the
database. If a migration in the deploy being rolled back is not backward-compatible with the
previous code version, redeploying that previous code against the now-current schema will break it.

For anything that isn't purely additive (a new nullable column, a new table, a new index), follow
the same multi-step pattern Week 10 already established for the tenant-isolation rollout:
1. Add the new column/constraint as **nullable**, deploy.
2. Backfill data in a separate step/deploy.
3. Only once backfilled, deploy the migration that makes it **non-null**/enforced.

Never ship a single breaking migration that would strand a rolled-back deployment against a schema
its code doesn't understand.

## Migration reversibility policy (ADR-016 confirmation #7)

Every migration's `Down()` either performs real, working rollback work (purely additive changes) or
throws `NotSupportedException` with a documented reason (irreversible changes — data transformations,
destructive operations). Enforced by `MigrationReversibilityTests`
(`tests/OrchestAI.Tests/Architecture/`).

**Production rollback does not mean running `dotnet ef database update <previous-migration>`
against the live database.** `Down()` existing and working is a local-development/testing
convenience (letting a developer cleanly undo a migration on their own machine), not a production
recovery mechanism. Production recovery is *always* "redeploy the previous application version
against a schema that remains compatible with it" (see above) — never an automatic downgrade
executed against live data.

## Known Limitations

Consolidated from ADR-011 through ADR-016 — check this list before treating one of these as a new
incident:

- **Rate-limiter bucket immutability after a live limit change** (ADR-015 confirmation #1 /
  implementation note). An admin `PUT .../limits` call that changes a tenant's `RequestsPerMinute`
  has zero effect on that tenant's *already-created* in-memory rate-limiter bucket until the process
  restarts (which resets every tenant's buckets, not just the changed one). Not a bug if observed —
  it's a named, accepted limitation of `System.Threading.RateLimiting`'s partition-caching model.
  Planned fix (not yet built): partition-key versioning (`{tenantId}:{limitsVersion}`).
- **Single-instance architecture, no distributed rate limiter.** The token-bucket rate limiter, the
  per-task tool-call budget counter, and the per-tenant queue-depth counter all live in single-
  process memory (ADR-015 confirmation #2). A second concurrently-running API instance would let a
  tenant's requests/tool calls/queue depth spread across instances that don't share state, silently
  defeating each limit. Deliberately out of scope until a second instance is actually deployed.
- **In-memory reservations, TTL-based crash recovery.** `TaskAdmissionReservation` rows are pure
  operational state (ADR-015 confirmation #5, `DESIGN_PRINCIPLES.md`'s "Operational state vs. audit
  state"). A reservation whose owning task crashes mid-execution is never explicitly released — it
  physically remains in the table until it ages out of admission math past
  `AbuseProtectionOptions.ReservationStalenessMinutes` (default 30 minutes). No reconciliation sweep
  deletes these orphaned rows; if they become numerous enough to matter, that's the trigger to build
  one (not yet needed).
- **`/health/ready`'s migration check is a live drift detector, not a startup gate** (ADR-16
  confirmation #2). Since `Program.cs` always auto-migrates at startup, immediately-post-startup this
  check is always trivially "no pending migrations." It only becomes meaningful if the schema drifts
  out from under a running container after the fact (e.g. a manual production DB change) — expected
  behavior, not a bug, if you ever see it fire on a container that's been running for a while.
```

- [ ] **Step 2: Validate structure**

Run: `grep -c "^## " RUNBOOK.md`
Expected: `5` (Incident classification, Rollback: redeploy, migration-compatibility rule, migration reversibility policy, Known Limitations).

- [ ] **Step 3: Commit**

```bash
git add RUNBOOK.md
git commit -m "docs: add RUNBOOK.md — rollback procedure, incident classification, known limitations"
```

---

## Task 13: GitHub repo settings — secret scanning verification + branch protection

**⚠ Requires explicit user confirmation before executing — this changes live settings on a public GitHub repository, not local files.**

**Files:** none (repo settings only).

- [ ] **Step 1: Verify GitHub secret scanning / push protection is enabled**

Ask the user to open `https://github.com/jigargajjarcad/orchestai/settings/security_analysis` and
confirm **Secret scanning** and **Push protection** both show as **Enabled**. Public repositories
get secret scanning on by default; push protection may need an explicit toggle. If either is off,
the user enables it via that page directly (a one-click toggle, not something to script). Record the
outcome in this plan's checklist — no code or workflow change results from this step either way.

- [ ] **Step 2: Confirm with the user before applying branch protection**

Present the exact change before running anything:
- Required status checks on `main`: `build-and-test`, `migration-validation`, `container-smoke-test`,
  `security-scan` (must match the `name:` fields used in `ci.yml`'s `jobs:` section exactly).
- `required_linear_history: true`, `allow_force_pushes: false`, `allow_deletions: false`.
- **No** required-PR-reviews setting — this repo continues to merge locally and push directly to
  `main`.

- [ ] **Step 3: Apply (only after explicit confirmation)**

If the user has `gh` installed and authenticated:

```bash
gh api -X PUT repos/jigargajjarcad/orchestai/branches/main/protection --input - <<'EOF'
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["build-and-test", "migration-validation", "container-smoke-test", "security-scan"]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": null,
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_linear_history": true
}
EOF
```

If `gh` is not installed/authenticated (confirmed not installed in this shell during this plan's
investigation phase), the user applies the same settings manually via
`https://github.com/jigargajjarcad/orchestai/settings/branches` → **Add branch protection rule** →
branch name pattern `main` → check "Require status checks to pass" and select the 4 job names once
they've appeared at least once (they only become selectable after Task 14's proof run) → check
"Require linear history" → leave "Require a pull request before merging" **unchecked**.

- [ ] **Step 4: Verify**

Run: `gh api repos/jigargajjarcad/orchestai/branches/main/protection` (if `gh` available) and confirm
the returned JSON matches the 4 required contexts and the linear-history/force-push settings above.
Otherwise, the user confirms visually via the Settings → Branches page.

---

## Task 14: Scratch-branch CI-failure proof

**⚠ Requires the user's live participation — pushes a real branch to the public GitHub remote and triggers real GitHub Actions runs. Confirm before pushing anything.**

**Files:** none in the final state (all changes in this task are pushed, observed, then reverted — nothing lands on `main`).

- [ ] **Step 1: Confirm Tasks 6-9 are already on `main`**

Run: `git log main --oneline -1` and `git branch --contains $(git rev-parse main)` to confirm the
commits containing the full `ci.yml` (all 4 jobs) are already merged and pushed to `main` — the
workflow file must exist on `main` (or at least on the pushed branch) for `workflow_dispatch` to be
able to target it.

- [ ] **Step 2: Create the scratch branch with two deliberate failures**

```bash
git checkout -b ci-proof-scratch
```

Deliberately break one test — in any existing passing test file (e.g.
`tests/OrchestAI.Tests/Infrastructure/RequiredConfigurationValidatorTests.cs`, added in Task 1),
change one assertion to something false, e.g. change:
```csharp
act.Should().NotThrow();
```
to:
```csharp
act.Should().Throw<InvalidOperationException>(); // deliberately wrong — proof only
```

Deliberately add a known-vulnerable package reference — add to
`src/OrchestAI.API/OrchestAI.API.csproj`:
```xml
    <PackageReference Include="System.Text.Encodings.Web" Version="4.5.0" />
```
(a version with a published, well-known high-severity vulnerability advisory — confirm current
advisory status via `dotnet list package --vulnerable` locally before relying on this specific
version/package if significant time has passed since this plan was written; substitute any package
version currently flagged High/Critical if `4.5.0` no longer registers).

```bash
git add -A
git commit -m "test: deliberate CI-failure proof (test break + vulnerable package) — DO NOT MERGE"
git push -u origin ci-proof-scratch
```

- [ ] **Step 3: Trigger the workflow on the scratch branch**

If `gh` is available:
```bash
gh workflow run ci.yml --ref ci-proof-scratch
gh run watch
```

If not, the user triggers it manually: GitHub → repo → Actions tab → `CI` workflow → "Run workflow"
dropdown → select branch `ci-proof-scratch` → Run workflow, then watches the run in the UI.

- [ ] **Step 4: Confirm both deliberate failures actually fail the workflow**

Confirm `build-and-test` fails on the broken test assertion. Confirm `security-scan` fails on the
vulnerable package. Confirm the other two jobs (`migration-validation`, `container-smoke-test`) pass
independently (they don't depend on the broken test or the added package) — this also proves the
"parallel jobs, independent feedback" design actually behaves that way, not just on paper.

- [ ] **Step 5: Confirm artifacts were actually attached to the failed run**

On the failed `build-and-test` run, confirm `build-and-test-results` (containing the `.trx` showing
the specific failing test) is downloadable from the run's Summary page. On the failed `security-scan`
run, confirm `security-scan-artifacts` (containing `vulnerable-packages.txt` showing the flagged
package) is downloadable. This directly proves confirmation #9 rather than assuming the `upload-
artifact` steps work because the YAML looks right.

- [ ] **Step 6: Revert the scratch branch and confirm a clean run**

```bash
git checkout ci-proof-scratch
git revert --no-edit HEAD
git push origin ci-proof-scratch
```

Re-trigger (`gh workflow run ci.yml --ref ci-proof-scratch` or the manual UI step) and confirm all
4 jobs now pass.

- [ ] **Step 7: Clean up the scratch branch**

```bash
git checkout main
git push origin --delete ci-proof-scratch
git branch -D ci-proof-scratch
```

(Confirm with the user before this remote branch deletion — it's a real, if low-risk, remote-state
change on the shared GitHub repo.)

- [ ] **Step 8: Record the proof outcome**

No file changes result from this task (everything happened on a now-deleted scratch branch) — the
proof itself is the deliverable. Note in the final plan-completion summary to the user that: the CI
gate was proven to actually fail on a broken test, proven to actually fail on a known-vulnerable
package, proven to upload inspectable artifacts on failure, and confirmed clean after revert.

---

## Self-review notes (already applied above, kept here for the record)

- **Spec coverage:** all 10 blocking confirmations map to a task (1↔13/6, 2↔3/11, 3↔7, 4↔8, 5↔9/13,
  6↔13, 7↔2/11, 8↔4/11/12, 9↔6-9's artifact-upload steps, 10↔1). All 4 `.github` deliverables (ci.yml,
  dependabot.yml, RUNBOOK.md via Task 12, ADR-016 via Task 11) have a task. All 6 "Tests" section
  items map to a task: readiness 503/200 (Task 2), liveness DB-independence (Task 2's third test +
  Task 3 Step 3's live curl), CI-gate proof (Task 14), migration-reversibility audit (Task 4),
  fail-fast config test (Task 1), artifact-upload proof (Task 14 Step 5), 0-warning/all-green bar
  (every task's build/test step).
- **Placeholder scan:** no TBD/"add appropriate handling"-style steps remain; every code/YAML step
  above has complete, literal content. Task 14's package-version note is the one place with an
  explicit "if this has gone stale, substitute" caveat — that's a deliberate acknowledgment that a
  specific CVE-affected package version is a moving target over time, not a placeholder.
- **Type/name consistency:** `IReadinessChecker`/`ReadinessResult`/`DatabaseReadinessChecker` names
  match exactly between Task 2 (definition) and Task 3 (consumption). CI job `name:` fields
  (`build-and-test`, `migration-validation`, `container-smoke-test`, `security-scan`) match exactly
  between Tasks 6-9 (definition) and Task 13 (branch-protection `contexts` list) and ADR-016's
  Confirmation #6.
