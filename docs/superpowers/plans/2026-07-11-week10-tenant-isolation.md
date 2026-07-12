# Week 10: Tenant Identity and Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **This is a security boundary, not a feature.** A partially-correct implementation (isolation that holds for HTTP reads but not background workers or writes) is a false sense of security — worse than not having the feature. Do not compress, skip, or "simplify for now" any task below. Every task's tests must actually run and pass against real (in-memory or real Postgres) data, not be reasoned about abstractly.

**Goal:** Introduce `Tenant`/`ApiKey` identity, API-key authentication, and enforce tenant isolation as the *default* mechanism (EF Core global query filters for reads, a `SaveChanges` interceptor for writes) across every table built in Weeks 1-9 — with explicit propagation into background workers, and explicit relationship-level ownership checks for every command that accepts a foreign ID.

**Architecture:** A new `ICurrentTenantAccessor` (AsyncLocal-backed ambient context) is read by `AppDbContext`'s reflection-driven global query filters and a new `TenantScopingInterceptor`. HTTP requests get their ambient tenant set by a new authentication middleware (API key → `Tenant`, fail-closed on anything invalid/missing/suspended). Background workers (`EvalRunBackgroundWorker`) explicitly restore the ambient tenant from the job's persisted `TenantId` before touching any data. The cost rollup service is deliberately *not* tenant-scoped — it gets its own narrow, explicit, auditable system-data-access path, since it is inherently cross-tenant by nature. All Week 1-9 data backfills to one well-known default/system tenant that has no valid API key (unreachable by any real caller).

**Tech Stack:** C# .NET 8, EF Core 8 (PostgreSQL, global query filters + `SaveChangesInterceptor`), MediatR, ASP.NET Core custom middleware, `System.Security.Cryptography` (SHA-256 + constant-time compare), xUnit + FluentAssertions + Moq, NetArchTest (existing layering guardrail), React (temporary in-memory key auth).

## Global Constraints

- Fail closed, always: absence of a resolved tenant context denies reads (empty results) and rejects writes (throw) — never falls back to a default/system tenant, never disables the filter.
- The default/system tenant (used only for backfill) must be structurally incapable of authenticating — no valid `ApiKey` row exists for it, ever.
- EF Core global query filters + the `SaveChanges` interceptor are the *default* enforcement mechanism, not the *only* one — any command accepting a foreign ID (baseline run, checkpoint resume, trace-to-eval linking) must still explicitly verify tenant ownership at the domain/service layer.
- Client-supplied `TenantId` values are never trusted — the interceptor stamps `TenantId` from the resolved ambient context only, and rejects (does not silently overwrite) any mismatched value that somehow reaches an entity.
- `TenantId` is set exactly once per entity (via the `SaveChanges` interceptor, using the same `entry.Property(...).CurrentValue` technique this codebase already uses for `UpdatedAt` — see `UpdatedAtInterceptor`) — no domain factory method for an `ITenantScoped` entity created by request-driven application code ever takes a `TenantId` parameter. This closes the "client-supplied TenantId" attack surface at the design level, in addition to the interceptor's runtime check. There are exactly two named exceptions, both non-`ITenantScoped` entities created only by a trusted, non-tenant-authenticated writer with no ambient tenant scope to bypass: `ApiKey.Create(tenantId, ...)` (Task 1 — the operator explicitly designates the tenant via the admin-secret-gated `CreateApiKeyHandler`, Task 8; there is no ambient tenant during that call for an interceptor to stamp from) and `CostRollup.Create(tenantId, ...)` (Task 12 — `CostRollupBackgroundService` derives it from an authoritative SQL join, never from a caller). Any other factory method accepting `TenantId` is a defect, not a third instance of this pattern.
- Background workers and queued commands must carry `TenantId` explicitly in their persisted payload, captured at enqueue time — never inferred later, never defaulted.
- The cost rollup job is cross-tenant by nature and uses a separate, explicit, narrowly-scoped system-data-access path — it must never run inside a tenant's ambient scope, and must never be reachable from tenant-authenticated request or worker code paths.
- No self-service tenant signup, no SSO/OAuth, no per-tenant billing, no RBAC-within-tenant, no rate limiting/cost caps (Week 11), no self-service key rotation UI. Tenant/API-key creation is operator-only this week, gated by a separate admin secret, never a tenant API key.
- Keep the 0-warning, all-tests-green bar. Baseline before Task 1: **220/220** tests passing (confirmed).

---

## Investigation summary (already done — do not re-derive)

Verified directly against the current codebase (not assumed):

- **Every entity and its current ownership chain**, from `src/OrchestAI.Domain/Entities/*.cs` (15 files, all read in full):
  - Directly owns a `UserId` column today: `OrchestrationTask`, `AgentMemory`, `CostRollup` (unconstrained — no FK to `User`).
  - Owned transitively via a chain back to `OrchestrationTask.UserId`: `AgentExecution` (via `OrchestrationTaskId`), `AgentMessage`/`AgentRetryAttempt`/`McpToolCall` (via `AgentExecutionId`), `CostLedger`/`TaskCheckpoint` (via `OrchestrationTaskId`).
  - **Currently global/unowned** (no user linkage at all today): `EvalSuite`, `EvalCase`, `EvalRun`, `EvalResult` — Week 10 is what makes these tenant-private for the first time; today every "user" implicitly shares one global eval-suite space. `ModelPricing` stays genuinely global/shared (admin-updatable pricing, not tenant data).
  - `User.cs` in full: `Id`, `Email`, `DisplayName`, `CreatedAt`, `UpdatedAt` only — no auth field, no tenant/org concept, nothing multi-tenancy-adjacent. `User` stays as-is this week (an internal actor label used for `OrchestrationTask.UserId` attribution) — it is **not** part of the new auth/isolation model; `TenantId` is added as a parallel, independent column on each tenant-scoped entity, not derived by joining through `User`.
- **13 entities need `TenantId` (implement a new `ITenantScoped` interface)**: `OrchestrationTask`, `AgentExecution`, `AgentMemory`, `AgentMessage`, `AgentRetryAttempt`, `CostLedger`, `CostRollup`, `McpToolCall`, `TaskCheckpoint`, `EvalSuite`, `EvalCase`, `EvalRun`, `EvalResult`. Every one of their current `Create(...)` factory signatures and full source was read directly (see Task 2 for exact per-entity diffs).
- **Zero `HasQueryFilter` calls exist anywhere in `src/`** (confirmed via grep) — no global filters of any kind exist today.
- **`AppDbContext` does not override `SaveChanges`/`SaveChangesAsync`** — cross-cutting write behavior (`UpdatedAt` stamping) is done via a `SaveChangesInterceptor` (`UpdatedAtInterceptor`, in `src/OrchestAI.Infrastructure/Data/Interceptors/`), registered `AddSingleton` and wired via `options.AddInterceptors(...)` in `DependencyInjection.cs`. This is the exact pattern `TenantScopingInterceptor` (Task 5) will follow.
- **`AppDbContext`'s current constructor** (`src/OrchestAI.Infrastructure/Data/AppDbContext.cs:9`): `public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }` — no other dependencies today. Registered via `services.AddDbContextFactory<AppDbContext>((sp, options) => {...})`; all 12 repository classes take `IDbContextFactory<AppDbContext>` and create a fresh context per call (confirmed via grep) — meaning any new cross-cutting behavior (the query filter, the interceptor) must be wired at the *model*/*interceptor* level (evaluated per-DbContext-type or per-injected-service), not per-repository-call, since repositories never share a context instance.
- **Zero authentication of any kind exists today** — confirmed via grep for `Authorize`/`ClaimsPrincipal`/`ApiKey`/`Bearer`/`HttpContext.User` across `src/OrchestAI.API/`: no hits (the only `ApiKey` hits are unrelated third-party provider keys in `appsettings.json`). `Program.cs` has no `UseAuthentication()`/`UseAuthorization()` call. Every endpoint is fully anonymous today; `UserId` travels as a plain, unvalidated `Guid` in the route or body.
- **`Program.cs` full middleware order** (confirmed via full read, 89 lines): Serilog → `AddControllers` (+ `JsonStringEnumConverter`) → Swagger → `AddApplication`/`AddInfrastructure` → `AddCors("Frontend")` → `AddProblemDetails` → `build()` → migrate+seed → `UseSerilogRequestLogging` → (dev) Swagger UI → `UseExceptionHandler` → `UseStatusCodePages` → `UseCors("Frontend")` → `MapControllers` → `/health`. New auth middleware goes between `UseCors("Frontend")` and `MapControllers`, matching ASP.NET Core convention.
- **CORS**: origins from `ALLOWED_ORIGINS` config (comma-split, defaults to localhost), `.AllowAnyHeader().AllowAnyMethod()`, **no `.AllowCredentials()`** — relevant since API-key auth (a custom header) doesn't need credentialed CORS the way cookies would.
- **`DatabaseSeeder.cs` in full**: seeds exactly two `User` rows via raw `ExecuteSqlRawAsync` (`ON CONFLICT DO NOTHING`) — `DevUserId` (`3fa85f64-...`) and `EvalSystemUserId` (`0000ee7a1000`) — plus 4 `ModelPricing` rows. **Only raw-SQL writer in the codebase**; confirmed via grep this is the only `ExecuteSqlRawAsync`/`FromSqlRaw`/`IgnoreQueryFilters` usage anywhere in `src/` (zero `IgnoreQueryFilters` hits at all today).
- **`EvalRunBackgroundWorker`** (both live-suite and post-hoc paths) and **`CostRollupBackgroundService`** are the only two `BackgroundService` classes (confirmed via grep). `IEvalRunQueue`/`InMemoryEvalRunQueue` enqueues a **bare `Guid evalRunId`** — no identity travels with it today. Live-suite work is **hardcoded to `DatabaseSeeder.EvalSystemUserId`** (`EvalRunBackgroundWorker.cs:139-140`) regardless of who triggered the suite run. `RunEvalSuiteCommand`, `RequestPostHocScoringCommand`, and `ResumeOrchestrationTaskCommand` all carry **zero caller-identity field today** — confirmed via fresh full reads of all three.
- **`CostRollupBackgroundService`/`GetDailyAggregatesAsync`** is already grouped by `(Date, UserId, AgentType, Model)` and `CostRollup` already has a (currently FK-unconstrained) `UserId` column — this path is already "multi-tenant-shaped" by construction, just not access-controlled. It needs a `TenantId` column too (Task 12), added to its existing grouping key, not replacing `UserId`.
- **Migration convention** confirmed from the most recent migration (`20260710082238_AddPostHocScoring.cs`, full `Up()`/`Down()` read): `AlterColumn` for nullability changes, `AddColumn` with explicit Postgres `type:` strings, `CreateIndex` named `IX_{Table}_{Column(s)}`, partial-unique filters as raw double-quoted-column SQL strings, `Down()` fully mirrored in reverse.
- **Frontend**: `API_BASE` construction identical across `EvalsPage.jsx`/`ObservabilityPage.jsx`/`App.jsx`; `DEV_USER_ID` (matching `DatabaseSeeder.DevUserId`) hardcoded in `App.jsx`/`ObservabilityPage.jsx`. **Zero** `Authorization`/`localStorage`/`sessionStorage` usage anywhere in the frontend today — every `fetch` is unauthenticated. `vercel.json` is a pure SPA rewrite, no secrets/env exposure.
- **Test conventions**: zero existing hits for `Tenant`/`ClaimsPrincipal`/auth-adjacent tests anywhere. Infrastructure-level tests live flat under `tests/OrchestAI.Tests/Infrastructure/*.cs`, namespace `OrchestAI.Tests.Infrastructure`. `tests/OrchestAI.Tests/Architecture/LayeringTests.cs` (from the Week 9 cleanup pass) enforces Domain/Application/Infrastructure/API layering as a build-breaking guardrail — any new tenancy code placed in the wrong layer will fail this existing test immediately.
- **`Security/` folder** (`src/OrchestAI.Infrastructure/Security/`) currently holds only `RegexPiiRedactor.cs` — crypto/key-hashing code fits this folder's existing purpose; a new `Tenancy/` folder holds the ambient accessor, middleware, and interceptor-adjacent tenancy-specific infrastructure, matching the existing per-concern folder convention (`Eval/`, `Observability/`, `Security/`).

---

## Blocking-confirmation answers (resolved before any task below)

1. **Tenant definition:** one `Tenant` = one external org/customer; `ApiKey` is a separate entity, many-to-one to `Tenant` (revoking one key never orphans the tenant's data). `User` is untouched — it remains an internal actor label, orthogonal to `Tenant`, not part of the auth chain this week.
2. **Tenant-scoped tables:** the 13 entities listed above, via a new `ITenantScoped` marker interface. `ModelPricing` and `User` stay global/untouched.
3. **Centralization:** EF Core global query filters (reads) applied *generically* via reflection over every `ITenantScoped` entity type in `OnModelCreating` (so a future entity implementing the interface is automatically protected — no per-entity `HasQueryFilter` call to remember) + a new `TenantScopingInterceptor` (writes) mirroring the existing `UpdatedAtInterceptor` pattern exactly. Foreign-ID relationship checks (baseline run, resume-by-task-id, post-hoc explicit trace IDs) are handled explicitly per-command in Task 10 — the filter alone does not replace them.
4. **Fail closed:** the query filter compares `e.TenantId == accessor.TenantId` where `accessor.TenantId` is `Guid?` — when unset (`null`), SQL's `col = NULL` is never true, so reads naturally return zero rows with no special-casing. The interceptor explicitly throws `TenantContextViolationException` on any write attempt with no resolved tenant. The default/system tenant gets zero `ApiKey` rows, ever — structurally unauthenticatable.
5. **Background propagation:** `TenantId` is captured into each command's payload at enqueue time (`RunEvalSuiteCommand`, `RequestPostHocScoringCommand`) and persisted onto the resulting `EvalRun`. `EvalRunBackgroundWorker` explicitly calls `ICurrentTenantAccessor.SetTenant(run.TenantId)` before processing each dequeued job (both live-suite and post-hoc paths), and checks the tenant's current status before executing (suspended → reject, don't silently complete).
6. **Ambient tenant mechanism:** `ICurrentTenantAccessor`, backed by `AsyncLocal<Guid?>` — the same mechanism serves both HTTP-request scope (set once by auth middleware) and background-job scope (set explicitly per dequeued job), since `AsyncLocal` flows correctly across async continuations regardless of DI-scope boundaries, and doesn't depend on `IHttpContextAccessor` (which doesn't exist in a background worker).
7. **API key format:** `orch_live_<publicKeyId>.<secret>`. Stored: `PublicKeyId` (indexed, unique, O(1) lookup), `HashedSecret` (SHA-256 over a long, cryptographically-random secret — a slow KDF is unnecessary and would actively hurt a machine-credential auth path, since it exists to resist brute-forcing a low-entropy human-chosen password, which doesn't apply here), `TenantId`, timestamps, `RevokedAt`/`ExpiresAt` (nullable). Verify via constant-time byte comparison, never raw string `==`.
8. **Backfill + bootstrap:** one well-known default/system `Tenant` (`Guid` `00000000-0000-0000-0000-000000000001`, exposed as `Tenant.DefaultTenantId`), zero `ApiKey` rows for it — unauthenticatable by design, enforced at **two** independent layers: `CreateApiKeyHandler` (Task 8) rejects the ID explicitly, and a Postgres `CHECK` constraint on `ApiKeys.TenantId` (Task 6) refuses the row even from a raw SQL insert or a future code path that bypasses the handler. `CreateTenantCommand`/`CreateApiKeyCommand`/`RevokeApiKeyCommand` are reachable only through a separate admin-secret-gated controller, never the tenant-facing API.
9. **Suspension:** invalid/missing/revoked key → 401. Valid key, suspended tenant → 403. Queued work for a tenant suspended after enqueue → explicit rejection when the worker checks status, not silent completion.
10. **Frontend:** temporary in-memory (session-only, not persisted) API-key prompt — explicitly documented in ADR-014 as non-production and still exposed to XSS during an active session (in-memory only avoids *persistence*-based exposure, not runtime exposure).

---

### Task 1: Domain model foundation — `Tenant`, `ApiKey`, `ITenantScoped`, exception type

**Files:**
- Create: `src/OrchestAI.Domain/Enums/TenantStatus.cs`
- Create: `src/OrchestAI.Domain/Entities/Tenant.cs`
- Create: `src/OrchestAI.Domain/Entities/ApiKey.cs`
- Create: `src/OrchestAI.Domain/Interfaces/ITenantScoped.cs`
- Create: `src/OrchestAI.Application/Exceptions/TenantContextViolationException.cs`
- Test: Create `tests/OrchestAI.Tests/Domain/TenantTests.cs`
- Test: Create `tests/OrchestAI.Tests/Domain/ApiKeyTests.cs`

**Interfaces:**
- Produces: `TenantStatus { Active, Suspended }`; `Tenant.Create(name, slug)`, `Tenant.Suspend()`, `Tenant.Reactivate()`; `ApiKey.Create(tenantId, publicKeyId, hashedSecret, displayName?)`, `ApiKey.IsUsable()`, `ApiKey.Revoke()`, `ApiKey.RecordUsage()`; `ITenantScoped { Guid TenantId { get; } }` (read-only — no setter, enforced only by the interceptor in Task 5); `TenantContextViolationException(string message)`.

- [ ] **Step 1: Write the failing domain tests**

Create `tests/OrchestAI.Tests/Domain/TenantTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class TenantTests
{
    [Fact]
    public void Create_StartsActive()
    {
        var tenant = Tenant.Create("Acme Corp", "acme-corp");

        tenant.Name.Should().Be("Acme Corp");
        tenant.Slug.Should().Be("acme-corp");
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.SuspendedAt.Should().BeNull();
    }

    [Fact]
    public void Suspend_SetsStatusAndTimestamp()
    {
        var tenant = Tenant.Create("Acme Corp", "acme-corp");

        tenant.Suspend();

        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.SuspendedAt.Should().NotBeNull();
    }

    [Fact]
    public void Reactivate_ClearsSuspension()
    {
        var tenant = Tenant.Create("Acme Corp", "acme-corp");
        tenant.Suspend();

        tenant.Reactivate();

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.SuspendedAt.Should().BeNull();
    }
}
```

Create `tests/OrchestAI.Tests/Domain/ApiKeyTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Tests.Domain;

public sealed class ApiKeyTests
{
    [Fact]
    public void Create_IsUsableByDefault()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value", "prod");

        key.IsUsable().Should().BeTrue();
        key.RevokedAt.Should().BeNull();
        key.DisplayName.Should().Be("prod");
    }

    [Fact]
    public void Revoke_MakesKeyUnusable()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value");

        key.Revoke();

        key.IsUsable().Should().BeFalse();
        key.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void IsUsable_ExpiredKey_ReturnsFalse()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value");
        typeof(ApiKey).GetProperty(nameof(ApiKey.ExpiresAt))!
            .SetValue(key, DateTimeOffset.UtcNow.AddDays(-1));

        key.IsUsable().Should().BeFalse();
    }

    [Fact]
    public void RecordUsage_SetsLastUsedAt()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value");

        key.RecordUsage();

        key.LastUsedAt.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantTests|FullyQualifiedName~ApiKeyTests"`
Expected: FAIL — `Tenant`/`ApiKey`/`TenantStatus` don't exist yet (compile error).

- [ ] **Step 3: Create `TenantStatus`**

```csharp
namespace OrchestAI.Domain.Enums;

public enum TenantStatus
{
    Active,
    Suspended
}
```

- [ ] **Step 4: Create `Tenant` entity**

```csharp
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class Tenant
{
    // The well-known backfill/system tenant (Task 6). Structurally unauthenticatable at two
    // independent layers: CreateApiKeyHandler (Task 8) rejects it explicitly, and a Postgres
    // CHECK constraint on ApiKeys.TenantId (Task 6, CK_ApiKeys_TenantId_NotDefault) refuses the
    // row even from a raw SQL insert. Defined here, in Domain, so both Infrastructure (the
    // migration/seeder) and Application (the CreateApiKeyHandler guard) reference one source of
    // truth without Application depending on Infrastructure (see Global Constraints and
    // LayeringTests).
    public static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private Tenant() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }

    public static Tenant Create(string name, string slug)
    {
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            Status = TenantStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Suspend()
    {
        Status = TenantStatus.Suspended;
        SuspendedAt = DateTimeOffset.UtcNow;
    }

    public void Reactivate()
    {
        Status = TenantStatus.Active;
        SuspendedAt = null;
    }
}
```

- [ ] **Step 5: Create `ApiKey` entity**

```csharp
namespace OrchestAI.Domain.Entities;

// The raw secret is never persisted or logged — only HashedSecret (see ADR-014 confirmation
// #7). PublicKeyId is the indexed lookup key; HashedSecret is verified via constant-time
// comparison against the caller-supplied secret (see IApiKeyHasher, Task 7).
public sealed class ApiKey
{
    private ApiKey() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string PublicKeyId { get; private set; } = string.Empty;
    public string HashedSecret { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public Tenant Tenant { get; private set; } = null!;

    // ApiKey is deliberately NOT ITenantScoped (see Task 13's ExpectedGloballySharedTypes) and
    // Create() deliberately DOES take tenantId — this is one of exactly two named exceptions to
    // the Global Constraints' "no factory ever takes TenantId" rule (the other is
    // CostRollup.Create, Task 12). This is not a design regression: Create() is only ever
    // reachable via the admin-secret-gated CreateApiKeyHandler (Task 8), never a tenant-
    // authenticated request, and there is no ambient tenant scope during that call for an
    // interceptor to stamp from in the first place — the operator explicitly designating which
    // tenant a new key belongs to IS the operation, not a value that should be inferred from
    // request context.
    public static ApiKey Create(Guid tenantId, string publicKeyId, string hashedSecret, string? displayName = null)
    {
        return new ApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PublicKeyId = publicKeyId,
            HashedSecret = hashedSecret,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool IsUsable() =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);

    public void Revoke() => RevokedAt = DateTimeOffset.UtcNow;

    public void RecordUsage() => LastUsedAt = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 6: Create `ITenantScoped`**

```csharp
namespace OrchestAI.Domain.Interfaces;

// Marker + accessor for every entity that must be isolated per tenant. TenantId has NO public
// setter and is never a parameter on any Create(...) factory — the only writer is
// TenantScopingInterceptor (Task 5), which sets it via entry.Property(...).CurrentValue,
// exactly like UpdatedAtInterceptor stamps UpdatedAt. This closes the "client-supplied
// TenantId" attack surface at the design level, not just at runtime-check level.
public interface ITenantScoped
{
    Guid TenantId { get; }
}
```

- [ ] **Step 7: Create `TenantContextViolationException`**

```csharp
namespace OrchestAI.Application.Exceptions;

// Thrown by TenantScopingInterceptor (Task 5) when a write would cross a tenant boundary —
// either no tenant context is resolved, or an entity already carries a TenantId that doesn't
// match the current ambient tenant. Controllers map this to 403 Forbidden (Task 9) — this is a
// security-boundary violation, not an ordinary validation error.
public sealed class TenantContextViolationException : Exception
{
    public TenantContextViolationException(string message) : base(message) { }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantTests|FullyQualifiedName~ApiKeyTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/OrchestAI.Domain/Enums/TenantStatus.cs src/OrchestAI.Domain/Entities/Tenant.cs \
  src/OrchestAI.Domain/Entities/ApiKey.cs src/OrchestAI.Domain/Interfaces/ITenantScoped.cs \
  src/OrchestAI.Application/Exceptions/TenantContextViolationException.cs \
  tests/OrchestAI.Tests/Domain/TenantTests.cs tests/OrchestAI.Tests/Domain/ApiKeyTests.cs
git commit -m "feat: add Tenant/ApiKey domain model and ITenantScoped marker interface"
```

---

### Task 2: Add `TenantId`/`ITenantScoped` to all 13 existing tenant-scoped entities + EF configurations

**Files (all Modify):**
- `src/OrchestAI.Domain/Entities/OrchestrationTask.cs`, `AgentExecution.cs`, `AgentMemory.cs`, `AgentMessage.cs`, `AgentRetryAttempt.cs`, `CostLedger.cs`, `CostRollup.cs`, `McpToolCall.cs`, `TaskCheckpoint.cs`, `EvalSuite.cs`, `EvalCase.cs`, `EvalRun.cs`, `EvalResult.cs`
- `src/OrchestAI.Infrastructure/Data/Configurations/OrchestrationTaskConfiguration.cs`, `AgentExecutionConfiguration.cs`, `AgentMemoryConfiguration.cs`, `AgentMessageConfiguration.cs`, `AgentRetryAttemptConfiguration.cs`, `CostLedgerConfiguration.cs`, `CostRollupConfiguration.cs`, `McpToolCallConfiguration.cs`, `TaskCheckpointConfiguration.cs`, `EvalSuiteConfiguration.cs`, `EvalCaseConfiguration.cs`, `EvalRunConfiguration.cs`, `EvalResultConfiguration.cs`
- Test: Create `tests/OrchestAI.Tests/Domain/TenantScopedEntitiesTests.cs`

**Interfaces:**
- Consumes: `ITenantScoped` (Task 1).
- Produces: every one of the 13 entities now has `public Guid TenantId { get; private set; }` and implements `ITenantScoped`. **No `Create(...)` factory method gains a new parameter** — `TenantId` is set only by `TenantScopingInterceptor` (Task 5). This is a mechanically identical change repeated 13 times; the exact current content of every file below was read in full before writing this task, so apply each diff exactly as shown.

- [ ] **Step 1: Write a failing test proving every entity's `Create(...)` still compiles without a TenantId parameter, and that TenantId defaults to `Guid.Empty` until the interceptor sets it**

Create `tests/OrchestAI.Tests/Domain/TenantScopedEntitiesTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Domain;

// Proves TenantId is never settable via any public factory — only TenantScopingInterceptor
// (Task 5) writes it, via reflection exactly like UpdatedAtInterceptor stamps UpdatedAt. Every
// entity here must implement ITenantScoped and default to Guid.Empty until stamped.
public sealed class TenantScopedEntitiesTests
{
    [Fact]
    public void OrchestrationTask_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "title", "prompt");
        (task as ITenantScoped).Should().NotBeNull();
        task.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentExecution_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var execution = AgentExecution.Create(Guid.NewGuid(), AgentType.Research, "prompt");
        (execution as ITenantScoped).Should().NotBeNull();
        execution.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentMemory_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var memory = AgentMemory.Create(Guid.NewGuid(), AgentType.Research, "key", "value");
        (memory as ITenantScoped).Should().NotBeNull();
        memory.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentMessage_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var message = AgentMessage.Create(Guid.NewGuid(), MessageRole.Assistant, "content", 0);
        (message as ITenantScoped).Should().NotBeNull();
        message.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentRetryAttempt_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var retry = AgentRetryAttempt.Create(Guid.NewGuid(), 1, 500, "timeout");
        (retry as ITenantScoped).Should().NotBeNull();
        retry.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void CostLedger_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var ledger = CostLedger.Create(Guid.NewGuid(), "model", 10, 5, 0.01m);
        (ledger as ITenantScoped).Should().NotBeNull();
        ledger.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void CostRollup_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var rollup = CostRollup.Create(DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), AgentType.Research, "model", 10, 5, 0.01m, 1);
        (rollup as ITenantScoped).Should().NotBeNull();
        rollup.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void McpToolCall_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var call = McpToolCall.Create(Guid.NewGuid(), "tool", "{}", "parent-span");
        (call as ITenantScoped).Should().NotBeNull();
        call.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TaskCheckpoint_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var checkpoint = TaskCheckpoint.Create(Guid.NewGuid(), AgentType.Research, Guid.NewGuid(), "output", 10, 5, 0.01m);
        (checkpoint as ITenantScoped).Should().NotBeNull();
        checkpoint.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalSuite_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var suite = EvalSuite.Create("suite", "desc", AgentType.Research);
        (suite as ITenantScoped).Should().NotBeNull();
        suite.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalCase_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var evalCase = EvalCase.Create(Guid.NewGuid(), "{}", "{}", EvalScorerType.RuleBased, 0.1m);
        (evalCase as ITenantScoped).Should().NotBeNull();
        evalCase.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalRun_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "v1", null);
        (run as ITenantScoped).Should().NotBeNull();
        run.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalResult_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var result = EvalResult.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvalScorerType.RuleBased, "v1", 1.0m, true, "{}");
        (result as ITenantScoped).Should().NotBeNull();
        result.TenantId.Should().Be(Guid.Empty);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantScopedEntitiesTests"`
Expected: FAIL — none of the 13 entities implement `ITenantScoped` yet (compile error: no `TenantId` property).

- [ ] **Step 3: Add `TenantId`/`ITenantScoped` to each entity**

For every entity below, add `, ITenantScoped` to the class declaration, add `using OrchestAI.Domain.Interfaces;` if not already present, and add the property `public Guid TenantId { get; private set; }` immediately after `Id`. **Do not add a `TenantId` parameter to any `Create(...)` method** — leave every factory method's signature and body otherwise unchanged.

**`OrchestrationTask.cs`** — change line 7 and add property after line 11:
```csharp
public sealed class OrchestrationTask : IHasUpdatedAt, ITenantScoped
{
    private OrchestrationTask() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
```

**`AgentExecution.cs`** — change line 6 and add property after line 10:
```csharp
public sealed class AgentExecution : ITenantScoped
{
    private AgentExecution() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrchestrationTaskId { get; private set; }
```

**`AgentMemory.cs`** — change line 6 and add property after line 10:
```csharp
public sealed class AgentMemory : IHasUpdatedAt, ITenantScoped
{
    private AgentMemory() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
```

**`AgentMessage.cs`** — change line 5, add `using OrchestAI.Domain.Interfaces;`, and add property after line 9:
```csharp
public sealed class AgentMessage : ITenantScoped
{
    private AgentMessage() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentExecutionId { get; private set; }
```

**`AgentRetryAttempt.cs`** — change line 3, add `using OrchestAI.Domain.Interfaces;`, and add property after line 7:
```csharp
public sealed class AgentRetryAttempt : ITenantScoped
{
    private AgentRetryAttempt() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentExecutionId { get; private set; }
```

**`CostLedger.cs`** — change line 5, add `using OrchestAI.Domain.Interfaces;`, and add property after line 9:
```csharp
public sealed class CostLedger : ITenantScoped
{
    private CostLedger() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrchestrationTaskId { get; private set; }
```

**`CostRollup.cs`** — change line 8, add `using OrchestAI.Domain.Interfaces;`, and add property after line 12:
```csharp
public sealed class CostRollup : ITenantScoped
{
    private CostRollup() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public DateOnly Date { get; private set; }
```

**`McpToolCall.cs`** — change line 6, add `using OrchestAI.Domain.Interfaces;`, and add property after line 10:
```csharp
public sealed class McpToolCall : ITenantScoped
{
    private McpToolCall() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentExecutionId { get; private set; }
```

**`TaskCheckpoint.cs`** — change line 5, add `using OrchestAI.Domain.Interfaces;`, and add property after line 9:
```csharp
public sealed class TaskCheckpoint : ITenantScoped
{
    private TaskCheckpoint() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrchestrationTaskId { get; private set; }
```

**`EvalSuite.cs`** — change line 5, add `using OrchestAI.Domain.Interfaces;`, and add property after line 9:
```csharp
public sealed class EvalSuite : ITenantScoped
{
    private EvalSuite() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
```

**`EvalCase.cs`** — change line 6, add `using OrchestAI.Domain.Interfaces;`, and add property after line 10:
```csharp
public sealed class EvalCase : ITenantScoped
{
    private EvalCase() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SuiteId { get; private set; }
```

**`EvalRun.cs`** — change line 5, add `using OrchestAI.Domain.Interfaces;`, and add property after line 9:
```csharp
public sealed class EvalRun : ITenantScoped
{
    private EvalRun() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? SuiteId { get; private set; }
```

**`EvalResult.cs`** — change line 7, add `using OrchestAI.Domain.Interfaces;`, and add property after line 11:
```csharp
public sealed class EvalResult : ITenantScoped
{
    private EvalResult() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EvalRunId { get; private set; }
```

- [ ] **Step 4: Add `TenantId` column configuration to each EF configuration file**

For every configuration file below, add this block immediately after the existing `Id` property configuration (matching each file's exact existing style — `HasColumnType("uuid")`, `IsRequired()` where the file already does this for `Id`), and add a `HasIndex(r => r.TenantId)` (or `e =>`/`t =>` matching each file's existing lambda parameter name) alongside the file's other `HasIndex` calls:

```csharp
        builder.Property(r => r.TenantId)
            .IsRequired()
            .HasColumnType("uuid");
```
```csharp
        builder.HasIndex(r => r.TenantId);
```

Apply this to: `OrchestrationTaskConfiguration.cs`, `AgentExecutionConfiguration.cs`, `AgentMemoryConfiguration.cs`, `AgentMessageConfiguration.cs`, `AgentRetryAttemptConfiguration.cs`, `CostLedgerConfiguration.cs`, `CostRollupConfiguration.cs`, `McpToolCallConfiguration.cs`, `TaskCheckpointConfiguration.cs`, `EvalSuiteConfiguration.cs`, `EvalCaseConfiguration.cs`, `EvalRunConfiguration.cs`, `EvalResultConfiguration.cs`. Read each file first to match its exact lambda parameter naming (`r =>`, `e =>`, `t =>`, etc. — they are not all the same) and lambda style before editing — do not assume a single parameter name across all 13 files.

**Do not mark `TenantId` as non-nullable in the migration yet** — Task 6 handles the safe nullable→backfill→non-nullable migration sequence. For now this EF configuration change and the C# property are enough to make `dotnet build` succeed (the actual `IsRequired()` here describes the intended *final* state; Task 6's migration will add the column as nullable first regardless of what the fluent config says, since EF migrations are generated by diffing the model against the database, and the first migration in Task 6 explicitly overrides nullability for the transition period — see Task 6's exact steps).

- [ ] **Step 5: Run tests to verify they pass, and full build succeeds**

Run: `dotnet build OrchestAI.sln`
Expected: 0 errors (confirms all 13 entities + 13 configs compile).

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantScopedEntitiesTests"`
Expected: PASS, all 13 tests green.

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS — the full suite, including all existing Week 1-9 tests, must still be green (adding a property with a default value and an interface implementation is purely additive; no existing test constructs these entities in a way that would break). Baseline: 220 + 4 (Task 1) + 13 (this task) = 237.

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Domain/Entities/ src/OrchestAI.Infrastructure/Data/Configurations/ \
  tests/OrchestAI.Tests/Domain/TenantScopedEntitiesTests.cs
git commit -m "feat: add TenantId/ITenantScoped to all 13 tenant-scoped entities and their EF configs"
```

---

### Task 3: `ICurrentTenantAccessor` — ambient tenant context (AsyncLocal-backed)

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/ICurrentTenantAccessor.cs`
- Create: `src/OrchestAI.Infrastructure/Tenancy/AsyncLocalCurrentTenantAccessor.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register the accessor)
- Test: Create `tests/OrchestAI.Tests/Infrastructure/AsyncLocalCurrentTenantAccessorTests.cs`

**Interfaces:**
- Produces: `ICurrentTenantAccessor { Guid? TenantId { get; } IDisposable SetTenant(Guid tenantId); }`; `AsyncLocalCurrentTenantAccessor : ICurrentTenantAccessor`.

**Why AsyncLocal, not `IHttpContextAccessor`/DI scope:** this must work identically for an HTTP request (auth middleware sets it once) *and* a background-worker job (the worker explicitly sets it per dequeued item) — `IHttpContextAccessor` doesn't exist in the latter. `AppDbContext` instances are created via `IDbContextFactory<AppDbContext>`, which resolves each new instance's constructor dependencies from an internal per-call scope (not the ambient HTTP request scope) — so a scoped/per-request service wouldn't reliably flow into a freshly-factory-created `AppDbContext`. `AsyncLocal<T>` sidesteps this entirely: it flows correctly across async continuations within whatever logical call chain is currently executing, independent of DI scope boundaries, and the same registered instance (Singleton is correct here — its state lives in a `static AsyncLocal` field, not instance state) is referenced identically from HTTP middleware, worker code, `AppDbContext`'s query filter, and `TenantScopingInterceptor`.

- [ ] **Step 1: Write the failing tests**

Create `tests/OrchestAI.Tests/Infrastructure/AsyncLocalCurrentTenantAccessorTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AsyncLocalCurrentTenantAccessorTests
{
    [Fact]
    public void TenantId_NoScopeSet_IsNull()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();

        accessor.TenantId.Should().BeNull();
    }

    [Fact]
    public void SetTenant_WithinScope_ExposesTenantId()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var tenantId = Guid.NewGuid();

        using (accessor.SetTenant(tenantId))
        {
            accessor.TenantId.Should().Be(tenantId);
        }
    }

    [Fact]
    public void SetTenant_DisposingScope_RestoresPreviousValue()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        using (accessor.SetTenant(outer))
        {
            using (accessor.SetTenant(inner))
            {
                accessor.TenantId.Should().Be(inner);
            }
            accessor.TenantId.Should().Be(outer);
        }
        accessor.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task SetTenant_FlowsAcrossAsyncContinuations()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var tenantId = Guid.NewGuid();

        using (accessor.SetTenant(tenantId))
        {
            await Task.Delay(1);
            await Task.Yield();
            accessor.TenantId.Should().Be(tenantId, "AsyncLocal must survive await continuations within the same logical call chain");
        }
    }

    [Fact]
    public async Task SetTenant_DoesNotLeakAcrossConcurrentAsyncFlows()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var taskA = Task.Run(async () =>
        {
            using (accessor.SetTenant(tenantA))
            {
                await Task.Delay(20);
                return accessor.TenantId;
            }
        });
        var taskB = Task.Run(async () =>
        {
            using (accessor.SetTenant(tenantB))
            {
                await Task.Delay(10);
                return accessor.TenantId;
            }
        });

        var results = await Task.WhenAll(taskA, taskB);

        results[0].Should().Be(tenantA, "each Task.Run body has its own async flow and must not see the other's tenant");
        results[1].Should().Be(tenantB);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~AsyncLocalCurrentTenantAccessorTests"`
Expected: FAIL — `ICurrentTenantAccessor`/`AsyncLocalCurrentTenantAccessor` don't exist yet (compile error).

- [ ] **Step 3: Create `ICurrentTenantAccessor`**

```csharp
namespace OrchestAI.Domain.Interfaces;

// Ambient current-tenant context, read by AppDbContext's global query filters and
// TenantScopingInterceptor, set once per HTTP request (auth middleware) or once per
// background-worker job (explicitly, from the job's persisted TenantId). See ADR-014.
public interface ICurrentTenantAccessor
{
    Guid? TenantId { get; }

    // Sets the ambient tenant for the duration of the returned scope; disposing restores
    // whatever value was ambient before (supports nesting, though nesting isn't expected).
    IDisposable SetTenant(Guid tenantId);
}
```

- [ ] **Step 4: Implement `AsyncLocalCurrentTenantAccessor`**

```csharp
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Tenancy;

public sealed class AsyncLocalCurrentTenantAccessor : ICurrentTenantAccessor
{
    private static readonly AsyncLocal<Guid?> Ambient = new();

    public Guid? TenantId => Ambient.Value;

    public IDisposable SetTenant(Guid tenantId)
    {
        var previous = Ambient.Value;
        Ambient.Value = tenantId;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;

        public RestoreScope(Guid? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, add `using OrchestAI.Infrastructure.Tenancy;` to the usings, and add this line near the top of `AddInfrastructure` (before the `AddDbContextFactory` call, since `AppDbContext`'s constructor will depend on it starting in Task 4):

```csharp
        services.AddSingleton<ICurrentTenantAccessor, AsyncLocalCurrentTenantAccessor>();
```

(Add `using OrchestAI.Domain.Interfaces;` — already present in this file for other interfaces.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~AsyncLocalCurrentTenantAccessorTests"`
Expected: PASS, all 5 tests green — including the concurrent-flows test, which is the one that actually proves `AsyncLocal` isolation rather than just "it returns what I set."

- [ ] **Step 7: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/ICurrentTenantAccessor.cs \
  src/OrchestAI.Infrastructure/Tenancy/AsyncLocalCurrentTenantAccessor.cs \
  src/OrchestAI.Infrastructure/DependencyInjection.cs \
  tests/OrchestAI.Tests/Infrastructure/AsyncLocalCurrentTenantAccessorTests.cs
git commit -m "feat: add AsyncLocal-backed ICurrentTenantAccessor for ambient tenant context"
```

---

### Task 4: `AppDbContext` global query filters (generic, reflection-driven) + fail-closed read tests

**Files:**
- Modify: `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (no functional change needed here — `IDbContextFactory<AppDbContext>` resolves the new constructor parameter automatically; verify only)
- Test: Create `tests/OrchestAI.Tests/Infrastructure/TenantQueryFilterTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenantAccessor` (Task 3), `ITenantScoped` (Task 1).
- Produces: `AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantAccessor tenantAccessor)` — every `ITenantScoped` entity type gets a generic query filter `e => e.TenantId == _tenantAccessor.TenantId` applied automatically via reflection over `modelBuilder.Model.GetEntityTypes()`, so a *future* entity implementing `ITenantScoped` is protected without anyone remembering to add a new `HasQueryFilter` call.

**Why this fails closed with no special-casing:** the filter is `e.TenantId == _tenantAccessor.TenantId`, comparing a non-nullable `Guid` against a `Guid?`. When no tenant is set, `_tenantAccessor.TenantId` is `null`, and EF translates this to SQL `"TenantId" = @p` with `@p = NULL` — under SQL's three-valued logic, `x = NULL` is never `TRUE` for any real (non-null) `TenantId` value, so the query returns zero rows. This is why the filter must be written as a direct equality, **never** as `_tenantAccessor.TenantId == null || e.TenantId == _tenantAccessor.TenantId` — that version would fail *open* (return everything when no tenant is resolved) and must never be written this way.

- [ ] **Step 1: Write the failing tests**

Create `tests/OrchestAI.Tests/Infrastructure/TenantQueryFilterTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class TenantQueryFilterTests
{
    private static (PooledDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        // AppDbContext's constructor now takes ICurrentTenantAccessor directly (no DI container
        // involved in this test) — PooledDbContextFactory resolves it via the options builder's
        // captured constructor args, matching how every other repository test in this codebase
        // already constructs a factory without a full DI container.
        return (new PooledDbContextFactory<AppDbContext>(options, accessor), accessor);
    }

    private static async Task<(Guid TenantAId, Guid TenantBId, Guid TaskAId, Guid TaskBId)> SeedTwoTenants(
        PooledDbContextFactory<AppDbContext> factory, AsyncLocalCurrentTenantAccessor accessor)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("tenant-filter@test.local");

        Guid taskAId, taskBId;
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
        }

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskAId = task.Id;
        }

        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant B task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskBId = task.Id;
        }

        return (tenantAId, tenantBId, taskAId, taskBId);
    }

    [Fact]
    public async Task Query_WithTenantAScope_OnlySeesTenantARows()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var (tenantAId, _, taskAId, taskBId) = await SeedTwoTenants(factory, accessor);

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var tasks = await ctx.OrchestrationTasks.ToListAsync();

            tasks.Should().ContainSingle(t => t.Id == taskAId);
            tasks.Should().NotContain(t => t.Id == taskBId);
        }
    }

    [Fact]
    public async Task Query_WithNoTenantScope_ReturnsNoRows()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        await SeedTwoTenants(factory, accessor);

        // Deliberately no accessor.SetTenant(...) call — simulates the fail-closed case.
        await using var ctx = await factory.CreateDbContextAsync();
        var tasks = await ctx.OrchestrationTasks.ToListAsync();

        tasks.Should().BeEmpty("no tenant context resolved must mean zero rows, never all rows");
    }

    [Fact]
    public async Task Query_ByIdForForeignTenantRow_ReturnsNull()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var (tenantAId, _, _, taskBId) = await SeedTwoTenants(factory, accessor);

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var found = await ctx.OrchestrationTasks.FirstOrDefaultAsync(t => t.Id == taskBId);

            found.Should().BeNull("looking up tenant B's row by ID while scoped to tenant A must not leak it");
        }
    }
}
```

Also create `tests/OrchestAI.Tests/Infrastructure/TestUserFactory.cs` **only if** `TestUserFactory` isn't already accessible from this new test — it already exists as `internal static class TestUserFactory` in `CostLedgerRepositoryEvalFilterTests.cs` within the same `OrchestAI.Tests.Infrastructure` namespace (confirmed in Week 9), so **do not redefine it** — this new test file can use it directly since `internal` is assembly-scoped.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantQueryFilterTests"`
Expected: FAIL — `AppDbContext`'s constructor doesn't accept `ICurrentTenantAccessor` yet (compile error), and no query filter exists yet even once it compiles.

- [ ] **Step 3: Update `AppDbContext`**

Replace the full contents of `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`:

```csharp
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data.Configurations;

namespace OrchestAI.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    private readonly ICurrentTenantAccessor _tenantAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantAccessor tenantAccessor)
        : base(options)
    {
        _tenantAccessor = tenantAccessor;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<OrchestrationTask> OrchestrationTasks => Set<OrchestrationTask>();
    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
    public DbSet<AgentMessage> AgentMessages => Set<AgentMessage>();
    public DbSet<McpToolCall> McpToolCalls => Set<McpToolCall>();
    public DbSet<CostLedger> CostLedger => Set<CostLedger>();
    public DbSet<TaskCheckpoint> TaskCheckpoints => Set<TaskCheckpoint>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
    public DbSet<AgentRetryAttempt> AgentRetryAttempts => Set<AgentRetryAttempt>();
    public DbSet<CostRollup> CostRollups => Set<CostRollup>();
    public DbSet<ModelPricing> ModelPricing => Set<ModelPricing>();
    public DbSet<EvalSuite> EvalSuites => Set<EvalSuite>();
    public DbSet<EvalCase> EvalCases => Set<EvalCase>();
    public DbSet<EvalRun> EvalRuns => Set<EvalRun>();
    public DbSet<EvalResult> EvalResults => Set<EvalResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new OrchestrationTaskConfiguration());
        modelBuilder.ApplyConfiguration(new AgentExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new AgentMessageConfiguration());
        modelBuilder.ApplyConfiguration(new McpToolCallConfiguration());
        modelBuilder.ApplyConfiguration(new CostLedgerConfiguration());
        modelBuilder.ApplyConfiguration(new TaskCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new AgentMemoryConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRetryAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new CostRollupConfiguration());
        modelBuilder.ApplyConfiguration(new ModelPricingConfiguration());
        modelBuilder.ApplyConfiguration(new EvalSuiteConfiguration());
        modelBuilder.ApplyConfiguration(new EvalCaseConfiguration());
        modelBuilder.ApplyConfiguration(new EvalRunConfiguration());
        modelBuilder.ApplyConfiguration(new EvalResultConfiguration());

        ApplyTenantQueryFilters(modelBuilder);
    }

    // Applies the SAME filter shape to every entity implementing ITenantScoped, generically —
    // so a future entity that implements the interface is protected automatically, with no new
    // HasQueryFilter call to remember. See ADR-014 and TenantQueryFilterTests for the fail-closed
    // proof (comparing against a null ambient TenantId returns zero rows, never all rows).
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var buildFilterMethod = typeof(AppDbContext).GetMethod(
            nameof(BuildTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType)) continue;

            var typedMethod = buildFilterMethod.MakeGenericMethod(entityType.ClrType);
            var filter = (LambdaExpression)typedMethod.Invoke(this, null)!;
            entityType.SetQueryFilter(filter);
        }
    }

    private LambdaExpression BuildTenantFilter<TEntity>() where TEntity : class, ITenantScoped
    {
        Expression<Func<TEntity, bool>> filter = e => e.TenantId == _tenantAccessor.TenantId;
        return filter;
    }
}
```

- [ ] **Step 4: Verify `AddDbContextFactory` resolves the new constructor parameter automatically**

Read `src/OrchestAI.Infrastructure/DependencyInjection.cs`'s current `AddDbContextFactory<AppDbContext>((sp, options) => {...})` call — no change is needed to this lambda itself. `IDbContextFactory<AppDbContext>.CreateDbContext()` resolves `AppDbContext`'s full constructor (including `ICurrentTenantAccessor`, now registered in Task 3) from an internal DI scope automatically; only the `DbContextOptions` configuration is customized by the lambda. Confirm this by building — if `ICurrentTenantAccessor` were not registered, `dotnet build` would still succeed (DI resolution failures are a runtime error, not compile-time), so the real proof is Step 5's tests actually resolving a working `AppDbContext` through the factory in Task 6's later end-to-end test; for now, just confirm the solution compiles.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build OrchestAI.sln`
Expected: 0 errors. (Note: this will currently fail if `TenantConfiguration`/`ApiKeyConfiguration` don't exist yet — they're created in Task 6 alongside the migration. If Task 6 hasn't run yet in your execution order, stub minimal `TenantConfiguration`/`ApiKeyConfiguration` classes now — matching the exact `UserConfiguration.cs` style — so this task's build succeeds standalone; Task 6 will then flesh out their full column/index configuration. Read `UserConfiguration.cs` first to match its style before writing the stubs.)

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantQueryFilterTests"`
Expected: PASS, all 3 tests green — this is the direct proof of confirmation #4 (fail-closed reads).

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS — full suite still green. Any existing repository test that seeds/queries `ITenantScoped` entities without ever calling `accessor.SetTenant(...)` will now see empty results from those specific queries; check the full run for any newly-failing pre-existing test and fix it by wrapping its seed/query calls in `using (accessor.SetTenant(someTenantId))` — this is expected, necessary fallout of turning on tenant isolation, not a bug in this task. Do not weaken the filter to make old tests pass without a scope; fix the tests to set a scope instead.

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Infrastructure/Data/AppDbContext.cs \
  tests/OrchestAI.Tests/Infrastructure/TenantQueryFilterTests.cs
git commit -m "feat: wire generic EF Core global query filters for every ITenantScoped entity"
```

---

### Task 5: `TenantScopingInterceptor` — auto-stamp writes, reject mismatched `TenantId`

**Files:**
- Create: `src/OrchestAI.Infrastructure/Data/Interceptors/TenantScopingInterceptor.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register interceptor, add to `AddInterceptors(...)`)
- Test: Create `tests/OrchestAI.Tests/Infrastructure/TenantScopingInterceptorTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenantAccessor` (Task 3), `ITenantScoped` (Task 1), `TenantContextViolationException` (Task 1).
- Produces: `TenantScopingInterceptor : SaveChangesInterceptor` — on every `SavingChanges`/`SavingChangesAsync`, for each `ITenantScoped` entry: `Added` with no `TenantId` yet set → stamp from the ambient accessor (throw if no tenant resolved); `Added` with a `TenantId` already present that doesn't match the ambient tenant → throw (defense in depth — no legitimate code path sets it before save, since no `Create(...)` factory takes it, but this catches any future path that tries); `Modified` with `TenantId` flagged as changed → throw (a tenant assignment must never change after creation).

- [ ] **Step 1: Write the failing tests**

Create `tests/OrchestAI.Tests/Infrastructure/TenantScopingInterceptorTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class TenantScopingInterceptorTests
{
    private static (PooledDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return (new PooledDbContextFactory<AppDbContext>(options, accessor), accessor);
    }

    [Fact]
    public async Task SaveChanges_WithTenantScope_StampsTenantIdOnNewEntity()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var user = TestUserFactory.Create("interceptor-stamp@test.local");

        Guid taskId;
        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "title", "prompt");
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskId = task.Id;
        }

        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var persisted = await ctx.OrchestrationTasks.SingleAsync(t => t.Id == taskId);
            persisted.TenantId.Should().Be(tenantId);
        }
    }

    [Fact]
    public async Task SaveChanges_NoTenantScope_ThrowsAndDoesNotPersist()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var user = TestUserFactory.Create("interceptor-noscope@test.local");

        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync(); // User is not ITenantScoped — this must still succeed.

        var task = OrchestrationTask.Create(user.Id, "title", "prompt");
        ctx.OrchestrationTasks.Add(task);

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<TenantContextViolationException>(
            "no ambient tenant is set, so persisting a new tenant-scoped entity must be rejected, never silently defaulted");
    }

    [Fact]
    public async Task SaveChanges_ExistingEntity_TenantIdCannotBeChanged()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var user = TestUserFactory.Create("interceptor-immutable@test.local");

        Guid taskId;
        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "title", "prompt");
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
            taskId = task.Id;
        }

        using (accessor.SetTenant(tenantId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = await ctx.OrchestrationTasks.SingleAsync(t => t.Id == taskId);
            var otherTenantId = Guid.NewGuid();
            ctx.Entry(task).Property("TenantId").CurrentValue = otherTenantId;

            var act = async () => await ctx.SaveChangesAsync();

            await act.Should().ThrowAsync<TenantContextViolationException>(
                "TenantId must never change once an entity is created");
        }
    }

    [Fact]
    public async Task SaveChanges_NonTenantScopedEntity_IsUnaffected()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var user = TestUserFactory.Create("interceptor-nonscoped@test.local");

        // Deliberately no accessor.SetTenant(...) — User isn't ITenantScoped, so this must
        // succeed even with zero ambient tenant context.
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.Users.Add(user);
        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantScopingInterceptorTests"`
Expected: FAIL — `TenantScopingInterceptor` doesn't exist yet (compile error).

- [ ] **Step 3: Implement `TenantScopingInterceptor`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Data.Interceptors;

// Mirrors UpdatedAtInterceptor's shape exactly, but enforces a security boundary instead of a
// convenience timestamp: TenantId is stamped on every new ITenantScoped entity from the ambient
// ICurrentTenantAccessor, and any attempt to persist a mismatched or later-changed TenantId is
// rejected outright rather than silently corrected. See ADR-014 confirmation #3.
public sealed class TenantScopingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentTenantAccessor _tenantAccessor;

    public TenantScopingInterceptor(ICurrentTenantAccessor tenantAccessor)
    {
        _tenantAccessor = tenantAccessor;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnforceTenantScoping(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        EnforceTenantScoping(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void EnforceTenantScoping(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>())
        {
            var tenantProperty = entry.Property(nameof(ITenantScoped.TenantId));

            if (entry.State is EntityState.Added)
            {
                var suppliedTenantId = (Guid)(tenantProperty.CurrentValue ?? Guid.Empty);

                if (suppliedTenantId != Guid.Empty)
                {
                    if (_tenantAccessor.TenantId is not { } activeTenantId || suppliedTenantId != activeTenantId)
                        throw new TenantContextViolationException(
                            $"Attempted to persist a new {entry.Entity.GetType().Name} with TenantId " +
                            $"{suppliedTenantId}, which does not match the current tenant context.");

                    continue;
                }

                if (_tenantAccessor.TenantId is not { } tenantId)
                    throw new TenantContextViolationException(
                        $"Cannot persist a new {entry.Entity.GetType().Name} — no tenant context is resolved.");

                tenantProperty.CurrentValue = tenantId;
            }
            else if (entry.State is EntityState.Modified && tenantProperty.IsModified)
            {
                throw new TenantContextViolationException(
                    $"TenantId on an existing {entry.Entity.GetType().Name} must never change after creation.");
            }
        }
    }
}
```

- [ ] **Step 4: Register in DI**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, register the interceptor as Singleton (mirroring `UpdatedAtInterceptor` exactly) and add it to the `AddInterceptors(...)` call:

```csharp
        services.AddSingleton<UpdatedAtInterceptor>();
        services.AddSingleton<TenantScopingInterceptor>();

        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var updatedAtInterceptor = sp.GetRequiredService<UpdatedAtInterceptor>();
            var tenantScopingInterceptor = sp.GetRequiredService<TenantScopingInterceptor>();

            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

            options.AddInterceptors(updatedAtInterceptor, tenantScopingInterceptor);
        });
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantScopingInterceptorTests"`
Expected: PASS, all 4 tests green.

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS — full suite green. As in Task 4, any existing test that writes an `ITenantScoped` entity without an ambient tenant scope will now throw `TenantContextViolationException`; fix those tests by wrapping the relevant seed/act calls in `using (accessor.SetTenant(...))` rather than weakening the interceptor.

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Infrastructure/Data/Interceptors/TenantScopingInterceptor.cs \
  src/OrchestAI.Infrastructure/DependencyInjection.cs \
  tests/OrchestAI.Tests/Infrastructure/TenantScopingInterceptorTests.cs
git commit -m "feat: add TenantScopingInterceptor enforcing fail-closed tenant writes"
```

---

### Task 6: Migration — `Tenants`/`ApiKeys` tables, default tenant seed, safe `TenantId` retrofit on all 13 tables

**Files:**
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/TenantConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/ApiKeyConfiguration.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/DatabaseSeeder.cs` (reference `Tenant.DefaultTenantId`, Task 1 — the row itself is created by this migration, not by `SeedAsync()`)
- Create (generated + hand-edited): `src/OrchestAI.Infrastructure/Migrations/<timestamp>_AddTenantIsolation.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/TenantBackfillIntegrationTests.cs`

**Interfaces:**
- Consumes: `Tenant`/`ApiKey` (Task 1), `ITenantScoped` on all 13 entities (Task 2).
- Produces: `Tenants`/`ApiKeys` tables; every one of the 13 tables gains a non-nullable, indexed, FK-constrained `TenantId` column; a well-known default/system tenant row (`00000000-0000-0000-0000-000000000001`) with **zero** `ApiKeys` rows.

**Why this migration hand-replaces the typed `AddColumn`/`AlterColumn` calls with raw SQL instead of relying on `dotnet ef migrations add`'s automatic diff:** `TenantId` is declared as a non-nullable `Guid` in every entity (correct final domain shape, matching `ITenantScoped`). A model-diff-generated migration would therefore try to `AddColumn(..., nullable: false)` directly against tables that already have rows — Postgres rejects this outright (or requires a default, which would silently backfill with a meaningless value, not the real relational-consistency backfill this task requires). The fix used here: let `dotnet ef migrations add` generate the migration NORMALLY (so `AppDbContextModelSnapshot.cs` and the migration's `.Designer.cs` correctly reflect the true final model state — those files are generated from the C# model itself, not from whatever ends up in `Up()`), then **replace only the body of `Up()`** with hand-written `migrationBuilder.Sql(...)` calls implementing the safe nullable→backfill→verify→non-nullable sequence for the 13 existing tables. The brand-new `Tenants`/`ApiKeys` tables have no populated-table risk, so their auto-generated `CreateTable` calls can be kept as EF generated them.

- [ ] **Step 1: Add `TenantConfiguration` and `ApiKeyConfiguration`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(TenantStatus.Active)
            .HasConversion<string>();

        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.SuspendedAt)
            .HasColumnType("timestamptz");

        builder.HasIndex(t => t.Slug).IsUnique();
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        // Schema-level backstop for the "default tenant can never authenticate" invariant
        // (Global Constraints, ADR-014 confirmation #8): CreateApiKeyHandler (Task 8) rejects
        // this at the application layer, but a CHECK constraint means the guarantee holds even
        // against a raw SQL insert, a future code path that bypasses the handler, or a bug in
        // the handler itself — the DB refuses the row unconditionally, not "as long as every
        // caller remembers to check." See Tenant.DefaultTenantId (Task 1).
        builder.ToTable("ApiKeys", t => t.HasCheckConstraint(
            "CK_ApiKeys_TenantId_NotDefault",
            "\"TenantId\" <> '00000000-0000-0000-0000-000000000001'"));

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(k => k.TenantId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(k => k.PublicKeyId)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(k => k.HashedSecret)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(k => k.DisplayName)
            .HasMaxLength(200);

        builder.Property(k => k.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(k => k.LastUsedAt)
            .HasColumnType("timestamptz");

        builder.Property(k => k.RevokedAt)
            .HasColumnType("timestamptz");

        builder.Property(k => k.ExpiresAt)
            .HasColumnType("timestamptz");

        builder.HasOne(k => k.Tenant)
            .WithMany()
            .HasForeignKey(k => k.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(k => k.PublicKeyId).IsUnique();
        builder.HasIndex(k => k.TenantId);
    }
}
```

Also add `modelBuilder.ApplyConfiguration(new TenantConfiguration());` and `modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());` to `AppDbContext.OnModelCreating` if Task 4's `AppDbContext.cs` replacement (which already includes these two lines) hasn't landed yet in your execution order — check the file first, don't duplicate the `ApplyConfiguration` calls.

- [ ] **Step 2: Reference `Tenant.DefaultTenantId` from `DatabaseSeeder`**

`Tenant.DefaultTenantId` (Task 1) is the single source of truth for this GUID — do **not**
declare a second constant with the same value here; two independently-declared constants that
happen to match today is exactly the kind of drift `DESIGN_PRINCIPLES.md`'s "single-choke-point"
principle (Task 17) warns about. In `src/OrchestAI.Infrastructure/Data/DatabaseSeeder.cs`, add a
`using OrchestAI.Domain.Entities;` and reference `Tenant.DefaultTenantId` directly wherever this
plan's later steps say `DatabaseSeeder.DefaultTenantId` (including this task's own Step 4/5 SQL
comments below and Task 18's smoke test) — read as `Tenant.DefaultTenantId` throughout.

- [ ] **Step 3: Generate the migration normally**

Run: `dotnet ef migrations add AddTenantIsolation --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API`

This produces `<timestamp>_AddTenantIsolation.cs`, `.Designer.cs`, and updates `AppDbContextModelSnapshot.cs`. **Do not touch `.Designer.cs` or `AppDbContextModelSnapshot.cs`** — leave them exactly as generated; they correctly describe the final model (non-nullable `TenantId` everywhere), which is what will actually be true once this migration's hand-edited `Up()` finishes running.

- [ ] **Step 4: Replace the body of `Up()`**

Open the generated migration file. Keep any `CreateTable` calls for `"Tenants"` and `"ApiKeys"` exactly as generated (these are brand-new tables — safe as-is), **including** the generated `constraints: table => { table.HasCheckConstraint("CK_ApiKeys_TenantId_NotDefault", ...); }` line inside the `"ApiKeys"` `CreateTable` call — this comes from `ApiKeyConfiguration`'s `HasCheckConstraint` (Step 1 above) and is exactly as load-bearing as the table itself; do not simplify it away as boilerplate. **Replace every `AddColumn`/`CreateIndex`/`AddForeignKey` call touching the 13 existing tables' `TenantId`** with the following, placed in this exact order inside `Up()` (after the `Tenants`/`ApiKeys` `CreateTable` calls, before the method ends):

```csharp
            // Step 1 (of the safe retrofit sequence): seed the default/system tenant. Zero
            // ApiKeys rows are ever created for it — see Tenant.DefaultTenantId and the guard
            // in CreateApiKeyHandler (Task 8).
            migrationBuilder.Sql(
                """
                INSERT INTO "Tenants" ("Id", "Name", "Slug", "Status", "CreatedAt")
                VALUES ('00000000-0000-0000-0000-000000000001', 'Default (Pre-Adoption) Tenant', 'default', 'Active', NOW())
                ON CONFLICT ("Id") DO NOTHING;
                """);

            // Step 2: add TenantId as NULLABLE to all 13 existing tables — safe against
            // already-populated tables, unlike a direct NOT NULL add.
            migrationBuilder.Sql(
                """
                ALTER TABLE "OrchestrationTasks" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentExecutions" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentMemories" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentMessages" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "AgentRetryAttempts" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "CostLedger" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "CostRollups" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "McpToolCalls" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "TaskCheckpoints" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalSuites" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalCases" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalRuns" ADD COLUMN "TenantId" uuid NULL;
                ALTER TABLE "EvalResults" ADD COLUMN "TenantId" uuid NULL;
                """);

            // Step 3: backfill, respecting existing ownership chains — not one independent
            // per-table default assignment. Order matters: parents before children.
            migrationBuilder.Sql(
                """
                UPDATE "OrchestrationTasks" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "AgentExecutions" ae SET "TenantId" = ot."TenantId" FROM "OrchestrationTasks" ot WHERE ae."OrchestrationTaskId" = ot."Id" AND ae."TenantId" IS NULL;
                UPDATE "AgentMemories" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "AgentMessages" am SET "TenantId" = ae."TenantId" FROM "AgentExecutions" ae WHERE am."AgentExecutionId" = ae."Id" AND am."TenantId" IS NULL;
                UPDATE "AgentRetryAttempts" ara SET "TenantId" = ae."TenantId" FROM "AgentExecutions" ae WHERE ara."AgentExecutionId" = ae."Id" AND ara."TenantId" IS NULL;
                UPDATE "CostLedger" cl SET "TenantId" = ot."TenantId" FROM "OrchestrationTasks" ot WHERE cl."OrchestrationTaskId" = ot."Id" AND cl."TenantId" IS NULL;
                UPDATE "CostRollups" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "McpToolCalls" mtc SET "TenantId" = ae."TenantId" FROM "AgentExecutions" ae WHERE mtc."AgentExecutionId" = ae."Id" AND mtc."TenantId" IS NULL;
                UPDATE "TaskCheckpoints" tc SET "TenantId" = ot."TenantId" FROM "OrchestrationTasks" ot WHERE tc."OrchestrationTaskId" = ot."Id" AND tc."TenantId" IS NULL;
                UPDATE "EvalSuites" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "TenantId" IS NULL;
                UPDATE "EvalCases" ec SET "TenantId" = es."TenantId" FROM "EvalSuites" es WHERE ec."SuiteId" = es."Id" AND ec."TenantId" IS NULL;
                UPDATE "EvalRuns" er SET "TenantId" = es."TenantId" FROM "EvalSuites" es WHERE er."SuiteId" = es."Id" AND er."TenantId" IS NULL;
                UPDATE "EvalRuns" SET "TenantId" = '00000000-0000-0000-0000-000000000001' WHERE "SuiteId" IS NULL AND "TenantId" IS NULL;
                UPDATE "EvalResults" evr SET "TenantId" = er."TenantId" FROM "EvalRuns" er WHERE evr."EvalRunId" = er."Id" AND evr."TenantId" IS NULL;
                """);

            // Step 4 (explicit post-backfill check, not an assumption): fail the migration
            // loudly if any row in any of the 13 tables still has a NULL TenantId — this must
            // never silently proceed to the NOT NULL tightening below with unbackfilled rows.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    remaining_nulls integer;
                BEGIN
                    SELECT
                        (SELECT count(*) FROM "OrchestrationTasks" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentExecutions" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentMemories" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentMessages" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "AgentRetryAttempts" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "CostLedger" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "CostRollups" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "McpToolCalls" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "TaskCheckpoints" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalSuites" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalCases" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalRuns" WHERE "TenantId" IS NULL) +
                        (SELECT count(*) FROM "EvalResults" WHERE "TenantId" IS NULL)
                    INTO remaining_nulls;

                    IF remaining_nulls > 0 THEN
                        RAISE EXCEPTION 'AddTenantIsolation backfill incomplete: % rows still have a NULL TenantId', remaining_nulls;
                    END IF;
                END $$;
                """);

            // Step 5: tighten to NOT NULL now that every row is guaranteed backfilled.
            migrationBuilder.Sql(
                """
                ALTER TABLE "OrchestrationTasks" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentExecutions" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentMemories" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentMessages" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "AgentRetryAttempts" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "CostLedger" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "CostRollups" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "McpToolCalls" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "TaskCheckpoints" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalSuites" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalCases" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalRuns" ALTER COLUMN "TenantId" SET NOT NULL;
                ALTER TABLE "EvalResults" ALTER COLUMN "TenantId" SET NOT NULL;
                """);

            // Step 6: indexes + FK constraints (safe now that every column is NOT NULL and
            // fully populated) — keep whatever exact CreateIndex/AddForeignKey calls EF
            // auto-generated for these 13 tables' TenantId columns here, in this position
            // (after the column is populated and non-null), rather than wherever EF originally
            // placed them in the generated file. Verify each references "Tenants" ("Id") with
            // ON DELETE RESTRICT (tenants aren't hard-deleted this week — see ADR-014).
```

Leave the rest of `Up()` (any remaining EF-generated `CreateIndex`/`AddForeignKey` calls for `Tenants`/`ApiKeys` themselves) untouched.

- [ ] **Step 5: Update `Down()` to match**

`Down()` can mostly stay as EF generated it (`DropTable "ApiKeys"`, `DropTable "Tenants"`, `DropColumn "TenantId"` from each of the 13 tables) — `DropColumn` doesn't care whether the column was added via typed API or raw SQL. Verify it drops columns/tables in the correct reverse-dependency order (FKs/indexes before tables, tables in reverse creation order) and that it doesn't attempt to drop indexes/constraints that Step 6 above didn't actually create under a different name than EF's auto-generated `Down()` expects — read the generated `Down()` carefully against what Step 4 actually created and reconcile any naming mismatch.

- [ ] **Step 6: Write and run the integration test**

Create `tests/OrchestAI.Tests/Infrastructure/TenantBackfillIntegrationTests.cs` — this test **must run against real PostgreSQL** (via the local dev Postgres from `docker-compose.yml`, same as the manual smoke-testing convention already used in this codebase), not the EF in-memory provider, since the backfill logic is raw SQL (`DO $$` blocks, `ON CONFLICT`) that the in-memory provider doesn't execute:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Tests.Infrastructure;

// Runs against the real local dev Postgres (docker-compose.yml) — the backfill logic is raw
// SQL that the in-memory provider never executes, so this is the only way to actually prove the
// migration ordering (nullable -> backfill -> verify -> non-nullable) works end to end.
public sealed class TenantBackfillIntegrationTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    [Fact]
    public async Task Migration_DefaultTenantExists_WithNoApiKeys()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            "SELECT \"Status\" FROM \"Tenants\" WHERE \"Id\" = '00000000-0000-0000-0000-000000000001'", connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            (await reader.ReadAsync()).Should().BeTrue("the default tenant must exist after migration");
            reader.GetString(0).Should().Be("Active");
        }

        await using var keyCmd = new NpgsqlCommand(
            "SELECT count(*) FROM \"ApiKeys\" WHERE \"TenantId\" = '00000000-0000-0000-0000-000000000001'", connection);
        var keyCount = (long)(await keyCmd.ExecuteScalarAsync())!;
        keyCount.Should().Be(0, "the default/system tenant must never have a valid API key");
    }

    [Theory]
    [InlineData("OrchestrationTasks")]
    [InlineData("AgentExecutions")]
    [InlineData("AgentMemories")]
    [InlineData("AgentMessages")]
    [InlineData("AgentRetryAttempts")]
    [InlineData("CostLedger")]
    [InlineData("CostRollups")]
    [InlineData("McpToolCalls")]
    [InlineData("TaskCheckpoints")]
    [InlineData("EvalSuites")]
    [InlineData("EvalCases")]
    [InlineData("EvalRuns")]
    [InlineData("EvalResults")]
    public async Task Table_HasNoNullTenantIds(string tableName)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM \"{tableName}\" WHERE \"TenantId\" IS NULL", connection);
        var nullCount = (long)(await cmd.ExecuteScalarAsync())!;

        nullCount.Should().Be(0, $"{tableName}.TenantId must be fully backfilled with no nulls remaining");
    }

    [Fact]
    public async Task EvalCase_InheritsTenantFromItsSuite_ForExistingBackfilledData()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*) FROM "EvalCases" ec
            JOIN "EvalSuites" es ON ec."SuiteId" = es."Id"
            WHERE ec."TenantId" <> es."TenantId"
            """, connection);
        var mismatches = (long)(await cmd.ExecuteScalarAsync())!;

        mismatches.Should().Be(0, "every EvalCase's TenantId must match its owning EvalSuite's TenantId — no independent per-table assignment");
    }

    [Fact]
    public async Task ApiKeys_CheckConstraint_RejectsDefaultTenantId_EvenViaRawSql()
    {
        // Proves the "default tenant can never authenticate" invariant holds at the schema
        // level, not only inside CreateApiKeyHandler (Task 8) — a raw INSERT that bypasses the
        // application layer entirely must still be refused by the database itself.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "ApiKeys" ("Id", "TenantId", "PublicKeyId", "HashedSecret", "CreatedAt")
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'pk_should_never_exist', 'hash', now())
            """, connection);

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be("23514", "23514 is Postgres's check_violation error code");
        exception.Which.ConstraintName.Should().Be("CK_ApiKeys_TenantId_NotDefault");
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantBackfillIntegrationTests"` (requires local Postgres running via `docker-compose up -d` and the API's `dotnet run` or a direct `dotnet ef database update` to have applied the migration first — confirm Postgres is up with `docker ps`, apply migrations with `dotnet ef database update --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API`, then run this test).
Expected: PASS, all 16 test cases (1 + 13 theories + 1 + 1) green against the real database.

- [ ] **Step 7: Run the full suite**

Run: `dotnet build OrchestAI.sln` — expect 0 errors.
Run: `dotnet test tests/OrchestAI.Tests` — expect the full in-memory suite still green (this migration doesn't change any C# entity shape beyond what Task 2 already did).

- [ ] **Step 8: Commit**

```bash
git add src/OrchestAI.Infrastructure/Data/Configurations/TenantConfiguration.cs \
  src/OrchestAI.Infrastructure/Data/Configurations/ApiKeyConfiguration.cs \
  src/OrchestAI.Infrastructure/Data/DatabaseSeeder.cs \
  src/OrchestAI.Infrastructure/Migrations/ \
  tests/OrchestAI.Tests/Infrastructure/TenantBackfillIntegrationTests.cs
git commit -m "feat: add Tenants/ApiKeys tables and safely retrofit TenantId onto all 13 existing tables"
```

---

### Task 7: API key format + hashing (`IApiKeyHasher`)

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IApiKeyHasher.cs`
- Create: `src/OrchestAI.Domain/Models/GeneratedApiKey.cs`
- Create: `src/OrchestAI.Domain/Models/ParsedApiKey.cs`
- Create: `src/OrchestAI.Infrastructure/Security/ApiKeyHasher.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register `IApiKeyHasher`)
- Test: Create `tests/OrchestAI.Tests/Infrastructure/ApiKeyHasherTests.cs`

**Interfaces:**
- Produces: `IApiKeyHasher { GeneratedApiKey GenerateNew(); ParsedApiKey? Parse(string rawKey); string Hash(string rawSecret); bool Verify(string rawSecret, string hashedSecret); }`; `GeneratedApiKey(string RawKey, string PublicKeyId, string HashedSecret)`; `ParsedApiKey(string PublicKeyId, string RawSecret)`.

**Format:** `orch_live_<publicKeyId>.<secret>` — `publicKeyId` is a 12-character random base62 string (indexed lookup key, not secret), `secret` is a 32-character random base62 string (≈190 bits of entropy — a machine credential, not a human password, so SHA-256 is the right hash, not a slow KDF like bcrypt/argon2, which exists specifically to resist brute-forcing a *low-entropy* human-chosen password). Verification uses `System.Security.Cryptography.CryptographicOperations.FixedTimeEquals` — the BCL's constant-time byte comparison primitive — never a raw `==`/`string.Equals`, which can leak timing information proportional to how many leading bytes match.

- [ ] **Step 1: Write the failing tests**

Create `tests/OrchestAI.Tests/Infrastructure/ApiKeyHasherTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Infrastructure.Security;

namespace OrchestAI.Tests.Infrastructure;

public sealed class ApiKeyHasherTests
{
    [Fact]
    public void GenerateNew_ProducesCorrectlyFormattedKey()
    {
        var hasher = new ApiKeyHasher();

        var generated = hasher.GenerateNew();

        generated.RawKey.Should().StartWith("orch_live_");
        generated.RawKey.Should().Contain(".");
        generated.PublicKeyId.Should().HaveLength(12);
        generated.HashedSecret.Should().NotBeNullOrEmpty();
        generated.RawKey.Should().NotContain(generated.HashedSecret, "the raw key must never embed the hash — only the plaintext secret");
    }

    [Fact]
    public void GenerateNew_TwoCalls_ProduceDifferentKeys()
    {
        var hasher = new ApiKeyHasher();

        var first = hasher.GenerateNew();
        var second = hasher.GenerateNew();

        first.RawKey.Should().NotBe(second.RawKey);
        first.PublicKeyId.Should().NotBe(second.PublicKeyId);
    }

    [Fact]
    public void Parse_RoundTripsAGeneratedKey()
    {
        var hasher = new ApiKeyHasher();
        var generated = hasher.GenerateNew();

        var parsed = hasher.Parse(generated.RawKey);

        parsed.Should().NotBeNull();
        parsed!.PublicKeyId.Should().Be(generated.PublicKeyId);
        hasher.Verify(parsed.RawSecret, generated.HashedSecret).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key-at-all")]
    [InlineData("orch_live_missingdot")]
    [InlineData("orch_live_.emptypublickeyid")]
    [InlineData("orch_live_abc123.")]
    [InlineData("wrong_prefix_abc123.secret")]
    public void Parse_MalformedInput_ReturnsNull(string input)
    {
        var hasher = new ApiKeyHasher();

        var parsed = hasher.Parse(input);

        parsed.Should().BeNull();
    }

    [Fact]
    public void Verify_CorrectSecret_ReturnsTrue()
    {
        var hasher = new ApiKeyHasher();
        var hashed = hasher.Hash("correct-secret-value");

        hasher.Verify("correct-secret-value", hashed).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var hasher = new ApiKeyHasher();
        var hashed = hasher.Hash("correct-secret-value");

        hasher.Verify("wrong-secret-value", hashed).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedStoredHash_ReturnsFalseInsteadOfThrowing()
    {
        var hasher = new ApiKeyHasher();

        var act = () => hasher.Verify("some-secret", "not-valid-hex!!");

        act.Should().NotThrow();
        hasher.Verify("some-secret", "not-valid-hex!!").Should().BeFalse();
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var hasher = new ApiKeyHasher();

        hasher.Hash("same-input").Should().Be(hasher.Hash("same-input"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~ApiKeyHasherTests"`
Expected: FAIL — `ApiKeyHasher` doesn't exist yet (compile error).

- [ ] **Step 3: Create the models and interface**

```csharp
namespace OrchestAI.Domain.Models;

// RawKey is shown to the caller exactly once (at creation) and never persisted or logged
// anywhere — only HashedSecret is stored. See ADR-014 confirmation #7.
public sealed record GeneratedApiKey(string RawKey, string PublicKeyId, string HashedSecret);
```

```csharp
namespace OrchestAI.Domain.Models;

public sealed record ParsedApiKey(string PublicKeyId, string RawSecret);
```

```csharp
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IApiKeyHasher
{
    GeneratedApiKey GenerateNew();

    // Returns null for anything that doesn't match "orch_live_<publicKeyId>.<secret>" with a
    // non-empty publicKeyId and secret — malformed input, not an exception.
    ParsedApiKey? Parse(string rawKey);

    string Hash(string rawSecret);

    // Constant-time comparison — never a raw string == / Equals. A malformed stored hash
    // returns false, it never throws (an auth check must fail closed on bad data, not crash).
    bool Verify(string rawSecret, string hashedSecret);
}
```

- [ ] **Step 4: Implement `ApiKeyHasher`**

```csharp
using System.Security.Cryptography;
using System.Text;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Security;

public sealed class ApiKeyHasher : IApiKeyHasher
{
    private const string Prefix = "orch_live_";
    private const int PublicKeyIdLength = 12;
    private const int SecretLength = 32;
    private const string Base62Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public GeneratedApiKey GenerateNew()
    {
        var publicKeyId = GenerateRandomBase62(PublicKeyIdLength);
        var secret = GenerateRandomBase62(SecretLength);
        var rawKey = $"{Prefix}{publicKeyId}.{secret}";
        var hashedSecret = Hash(secret);

        return new GeneratedApiKey(rawKey, publicKeyId, hashedSecret);
    }

    public ParsedApiKey? Parse(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey) || !rawKey.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var withoutPrefix = rawKey[Prefix.Length..];
        var dotIndex = withoutPrefix.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == withoutPrefix.Length - 1)
            return null;

        var publicKeyId = withoutPrefix[..dotIndex];
        var secret = withoutPrefix[(dotIndex + 1)..];
        return new ParsedApiKey(publicKeyId, secret);
    }

    public string Hash(string rawSecret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawSecret));
        return Convert.ToHexString(hash);
    }

    public bool Verify(string rawSecret, string hashedSecret)
    {
        byte[] storedBytes;
        try
        {
            storedBytes = Convert.FromHexString(hashedSecret);
        }
        catch (FormatException)
        {
            return false;
        }

        var computedBytes = Convert.FromHexString(Hash(rawSecret));
        return CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
    }

    private static string GenerateRandomBase62(int length)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Base62Alphabet[randomBytes[i] % Base62Alphabet.Length];
        return new string(chars);
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, add `using OrchestAI.Infrastructure.Security;` (already present for `RegexPiiRedactor`) and:

```csharp
        services.AddSingleton<IApiKeyHasher, ApiKeyHasher>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~ApiKeyHasherTests"`
Expected: PASS, all 12 tests green (8 fact-style + 6 theory cases counted individually — actually 6 theory inline data rows + 7 fact methods = 13 total; confirm the exact count in the runner output rather than assuming).

- [ ] **Step 7: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IApiKeyHasher.cs src/OrchestAI.Domain/Models/GeneratedApiKey.cs \
  src/OrchestAI.Domain/Models/ParsedApiKey.cs src/OrchestAI.Infrastructure/Security/ApiKeyHasher.cs \
  src/OrchestAI.Infrastructure/DependencyInjection.cs \
  tests/OrchestAI.Tests/Infrastructure/ApiKeyHasherTests.cs
git commit -m "feat: add IApiKeyHasher with constant-time secret verification"
```

---

### Task 8: `CreateTenantCommand`/`CreateApiKeyCommand`/`RevokeApiKeyCommand` + admin-secret-gated controller

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/ITenantRepository.cs`
- Create: `src/OrchestAI.Domain/Interfaces/IApiKeyRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/TenantRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/ApiKeyRepository.cs`
- Create: `src/OrchestAI.Application/Commands/CreateTenant/{CreateTenantCommand,CreateTenantHandler,CreateTenantResponse}.cs`
- Create: `src/OrchestAI.Application/Commands/CreateApiKey/{CreateApiKeyCommand,CreateApiKeyHandler,CreateApiKeyResponse}.cs`
- Create: `src/OrchestAI.Application/Commands/RevokeApiKey/{RevokeApiKeyCommand,RevokeApiKeyHandler,RevokeApiKeyResponse}.cs`
- Create: `src/OrchestAI.Infrastructure/Tenancy/RequireAdminSecretFilter.cs`
- Create: `src/OrchestAI.API/Controllers/AdminController.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register repositories + filter)
- Modify: `src/OrchestAI.API/appsettings.json` / `appsettings.Development.json` (add empty `Admin:BootstrapSecret` placeholder key so the setting is discoverable, never a real value committed)
- Test: Create `tests/OrchestAI.Tests/Application/CreateTenantHandlerTests.cs`, `CreateApiKeyHandlerTests.cs`, `RevokeApiKeyHandlerTests.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/RequireAdminSecretFilterTests.cs`

**Interfaces:**
- Produces: `ITenantRepository { GetByIdAsync, GetBySlugAsync, AddAsync, UpdateAsync }`; `IApiKeyRepository { GetByIdAsync, GetByPublicKeyIdAsync, AddAsync, UpdateAsync }`; `CreateTenantCommand(string Name, string Slug)`; `CreateApiKeyCommand(Guid TenantId, string? DisplayName)` → returns the raw key **exactly once**, rejects `Tenant.DefaultTenantId`; `RevokeApiKeyCommand(Guid ApiKeyId)`; `SuspendTenantCommand(Guid TenantId)` (Step 13); `RequireAdminSecretFilter : IAsyncActionFilter` — checks `X-Admin-Secret` header against `Admin:BootstrapSecret` config via constant-time comparison, 503 if unconfigured, 401 if missing/wrong.

**Why a separate admin-secret filter, not the tenant API-key auth from Task 9:** `Tenant`/`ApiKey` are deliberately **not** `ITenantScoped` — creating a tenant or minting its first key is an operation that precedes any tenant existing to authenticate as. Gating this behind a static, separately-configured operator secret (never a tenant API key) is what confirmation #8 requires: an ordinary tenant must never be able to create another tenant or mint itself unlimited keys.

- [ ] **Step 1: Write the failing handler tests**

Create `tests/OrchestAI.Tests/Application/CreateTenantHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.CreateTenant;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class CreateTenantHandlerTests
{
    [Fact]
    public async Task Handle_ValidNameAndSlug_CreatesTenant()
    {
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetBySlugAsync("acme-corp", It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);
        Tenant? captured = null;
        repoMock.Setup(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Callback<Tenant, CancellationToken>((t, _) => captured = t)
            .Returns(Task.CompletedTask);

        var handler = new CreateTenantHandler(repoMock.Object);
        var response = await handler.Handle(new CreateTenantCommand("Acme Corp", "acme-corp"), CancellationToken.None);

        captured.Should().NotBeNull();
        response.Name.Should().Be("Acme Corp");
        response.Slug.Should().Be("acme-corp");
    }

    [Fact]
    public async Task Handle_DuplicateSlug_ThrowsValidation()
    {
        var existing = Tenant.Create("Existing", "acme-corp");
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetBySlugAsync("acme-corp", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var handler = new CreateTenantHandler(repoMock.Object);
        var act = async () => await handler.Handle(new CreateTenantCommand("Acme Corp", "acme-corp"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsValidation()
    {
        var handler = new CreateTenantHandler(Mock.Of<ITenantRepository>());
        var act = async () => await handler.Handle(new CreateTenantCommand("", "slug"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

Create `tests/OrchestAI.Tests/Application/CreateApiKeyHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.CreateApiKey;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class CreateApiKeyHandlerTests
{
    [Fact]
    public async Task Handle_ExistingTenant_ReturnsRawKeyOnceAndPersistsOnlyTheHash()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var hasherMock = new Mock<IApiKeyHasher>();
        hasherMock.Setup(h => h.GenerateNew())
            .Returns(new GeneratedApiKey("orch_live_pk123.secretvalue", "pk123", "hashed-secretvalue"));

        ApiKey? captured = null;
        var apiKeyRepoMock = new Mock<IApiKeyRepository>();
        apiKeyRepoMock.Setup(r => r.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()))
            .Callback<ApiKey, CancellationToken>((k, _) => captured = k)
            .Returns(Task.CompletedTask);

        var handler = new CreateApiKeyHandler(tenantRepoMock.Object, apiKeyRepoMock.Object, hasherMock.Object);
        var response = await handler.Handle(new CreateApiKeyCommand(tenant.Id, "prod"), CancellationToken.None);

        response.RawKey.Should().Be("orch_live_pk123.secretvalue");
        captured.Should().NotBeNull();
        captured!.HashedSecret.Should().Be("hashed-secretvalue");
        captured.PublicKeyId.Should().Be("pk123");
        captured.DisplayName.Should().Be("prod");
    }

    [Fact]
    public async Task Handle_UnknownTenant_ThrowsNotFound()
    {
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var handler = new CreateApiKeyHandler(tenantRepoMock.Object, Mock.Of<IApiKeyRepository>(), Mock.Of<IApiKeyHasher>());
        var act = async () => await handler.Handle(new CreateApiKeyCommand(Guid.NewGuid(), null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_DefaultTenant_ThrowsValidation_NeverCreatesKey()
    {
        var apiKeyRepoMock = new Mock<IApiKeyRepository>();
        var handler = new CreateApiKeyHandler(
            Mock.Of<ITenantRepository>(), apiKeyRepoMock.Object, Mock.Of<IApiKeyHasher>());

        var act = async () => await handler.Handle(
            new CreateApiKeyCommand(Tenant.DefaultTenantId, "should-never-exist"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        apiKeyRepoMock.Verify(r => r.AddAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

Create `tests/OrchestAI.Tests/Application/RevokeApiKeyHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.RevokeApiKey;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class RevokeApiKeyHandlerTests
{
    [Fact]
    public async Task Handle_ExistingKey_RevokesAndPersists()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk123", "hashed");
        var repoMock = new Mock<IApiKeyRepository>();
        repoMock.Setup(r => r.GetByIdAsync(key.Id, It.IsAny<CancellationToken>())).ReturnsAsync(key);
        repoMock.Setup(r => r.UpdateAsync(key, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RevokeApiKeyHandler(repoMock.Object);
        var response = await handler.Handle(new RevokeApiKeyCommand(key.Id), CancellationToken.None);

        response.Revoked.Should().BeTrue();
        key.IsUsable().Should().BeFalse();
        repoMock.Verify(r => r.UpdateAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownKey_ThrowsNotFound()
    {
        var repoMock = new Mock<IApiKeyRepository>();
        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((ApiKey?)null);

        var handler = new RevokeApiKeyHandler(repoMock.Object);
        var act = async () => await handler.Handle(new RevokeApiKeyCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

Create `tests/OrchestAI.Tests/Infrastructure/RequireAdminSecretFilterTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class RequireAdminSecretFilterTests
{
    private static ActionExecutingContext BuildContext(string? headerValue)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue is not null)
            httpContext.Request.Headers["X-Admin-Secret"] = headerValue;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
        return new ActionExecutingContext(
            actionContext, [], new Dictionary<string, object?>(), controller: new object());
    }

    private static IConfiguration BuildConfig(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(secret is null ? [] : new Dictionary<string, string?> { ["Admin:BootstrapSecret"] = secret })
            .Build();

    [Fact]
    public async Task OnActionExecutionAsync_CorrectSecret_CallsNext()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var context = BuildContext("correct-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WrongSecret_Returns401AndDoesNotCallNext()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var context = BuildContext("wrong-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        nextCalled.Should().BeFalse();
        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_MissingHeader_Returns401()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var context = BuildContext(headerValue: null);

        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], context.Controller)));

        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_SecretNotConfigured_Returns503()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig(secret: null));
        var context = BuildContext("anything");

        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], context.Controller)));

        context.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    // A legitimate tenant credential must never satisfy the admin gate — this filter checks
    // ONLY X-Admin-Secret, never Authorization, so a request carrying a real (or fake) tenant
    // API key but no X-Admin-Secret header is indistinguishable from any other unauthenticated
    // request as far as this filter is concerned. Named explicitly (rather than left as an
    // implication of "checks one specific header") because it's the exact invariant confirmation
    // #8 depends on: an ordinary tenant must never be able to reach tenant/API-key provisioning.
    [Fact]
    public async Task OnActionExecutionAsync_ValidTenantAuthorizationHeaderPresent_StillReturns401()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer orch_live_pk123.a-real-tenant-secret";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext, [], new Dictionary<string, object?>(), controller: new object());
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        nextCalled.Should().BeFalse("a tenant API key must never satisfy the admin-secret gate, regardless of how valid it is elsewhere");
        context.Result.Should().BeOfType<UnauthorizedResult>();
    }
}
```

- [ ] **Step 2: Run to verify all of the above fail**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~CreateTenantHandlerTests|FullyQualifiedName~CreateApiKeyHandlerTests|FullyQualifiedName~RevokeApiKeyHandlerTests|FullyQualifiedName~RequireAdminSecretFilterTests"`
Expected: FAIL — none of the handlers/filter/repositories exist yet (compile errors).

- [ ] **Step 3: Create the repository interfaces**

```csharp
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

// Tenant/ApiKey are NOT ITenantScoped — they are the identity/management layer, globally
// visible to admin-bootstrap and auth-middleware code only, never filtered by the tenant query
// filter (there's no "current tenant" to scope a tenant lookup to before one is resolved).
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
}
```

```csharp
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IApiKeyRepository
{
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiKey?> GetByPublicKeyIdAsync(string publicKeyId, CancellationToken cancellationToken = default);
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
    Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement the repositories**

```csharp
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class TenantRepository : ITenantRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TenantRepository(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.Tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ctx.Tenants.Update(tenant);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class ApiKeyRepository : IApiKeyRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ApiKeyRepository(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiKey?> GetByPublicKeyIdAsync(string publicKeyId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.ApiKeys.FirstOrDefaultAsync(k => k.PublicKeyId == publicKeyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.ApiKeys.AddAsync(apiKey, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ctx.ApiKeys.Update(apiKey);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: `CreateTenantCommand`/`Handler`/`Response`**

```csharp
using MediatR;

namespace OrchestAI.Application.Commands.CreateTenant;

public sealed record CreateTenantCommand(string Name, string Slug) : IRequest<CreateTenantResponse>;
```

```csharp
namespace OrchestAI.Application.Commands.CreateTenant;

public sealed record CreateTenantResponse(Guid TenantId, string Name, string Slug, DateTimeOffset CreatedAt);
```

```csharp
using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateTenant;

public sealed class CreateTenantHandler : IRequestHandler<CreateTenantCommand, CreateTenantResponse>
{
    private readonly ITenantRepository _tenantRepository;

    public CreateTenantHandler(ITenantRepository tenantRepository) => _tenantRepository = tenantRepository;

    public async Task<CreateTenantResponse> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException(nameof(request.Name), "Name is required.");
        if (string.IsNullOrWhiteSpace(request.Slug))
            throw new ValidationException(nameof(request.Slug), "Slug is required.");

        var existing = await _tenantRepository.GetBySlugAsync(request.Slug, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            throw new ValidationException(nameof(request.Slug), $"Slug '{request.Slug}' is already in use.");

        var tenant = Tenant.Create(request.Name, request.Slug);
        await _tenantRepository.AddAsync(tenant, cancellationToken).ConfigureAwait(false);

        return new CreateTenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.CreatedAt);
    }
}
```

- [ ] **Step 6: `CreateApiKeyCommand`/`Handler`/`Response`**

```csharp
using MediatR;

namespace OrchestAI.Application.Commands.CreateApiKey;

public sealed record CreateApiKeyCommand(Guid TenantId, string? DisplayName) : IRequest<CreateApiKeyResponse>;
```

```csharp
namespace OrchestAI.Application.Commands.CreateApiKey;

// RawKey is returned exactly once — the caller must store it now; it can never be retrieved
// again (only HashedSecret is persisted). See ADR-014 confirmation #7.
public sealed record CreateApiKeyResponse(Guid ApiKeyId, string RawKey, string PublicKeyId, DateTimeOffset CreatedAt);
```

```csharp
using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateApiKey;

public sealed class CreateApiKeyHandler : IRequestHandler<CreateApiKeyCommand, CreateApiKeyResponse>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApiKeyHasher _hasher;

    public CreateApiKeyHandler(
        ITenantRepository tenantRepository, IApiKeyRepository apiKeyRepository, IApiKeyHasher hasher)
    {
        _tenantRepository = tenantRepository;
        _apiKeyRepository = apiKeyRepository;
        _hasher = hasher;
    }

    public async Task<CreateApiKeyResponse> Handle(CreateApiKeyCommand request, CancellationToken cancellationToken)
    {
        // Global Constraint: the default/backfill tenant must be structurally incapable of
        // authenticating — no ApiKey row is ever created for it, under any circumstances,
        // including operator error. See Tenant.DefaultTenantId (Task 1), ADR-014 confirmation #8.
        if (request.TenantId == Domain.Entities.Tenant.DefaultTenantId)
            throw new ValidationException(nameof(request.TenantId), "Cannot create an API key for the default/system tenant.");

        _ = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Domain.Entities.Tenant), request.TenantId);

        var generated = _hasher.GenerateNew();
        var apiKey = ApiKey.Create(request.TenantId, generated.PublicKeyId, generated.HashedSecret, request.DisplayName);
        await _apiKeyRepository.AddAsync(apiKey, cancellationToken).ConfigureAwait(false);

        return new CreateApiKeyResponse(apiKey.Id, generated.RawKey, generated.PublicKeyId, apiKey.CreatedAt);
    }
}
```

- [ ] **Step 7: `RevokeApiKeyCommand`/`Handler`/`Response`**

```csharp
using MediatR;

namespace OrchestAI.Application.Commands.RevokeApiKey;

public sealed record RevokeApiKeyCommand(Guid ApiKeyId) : IRequest<RevokeApiKeyResponse>;
```

```csharp
namespace OrchestAI.Application.Commands.RevokeApiKey;

public sealed record RevokeApiKeyResponse(Guid ApiKeyId, bool Revoked);
```

```csharp
using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.RevokeApiKey;

public sealed class RevokeApiKeyHandler : IRequestHandler<RevokeApiKeyCommand, RevokeApiKeyResponse>
{
    private readonly IApiKeyRepository _apiKeyRepository;

    public RevokeApiKeyHandler(IApiKeyRepository apiKeyRepository) => _apiKeyRepository = apiKeyRepository;

    public async Task<RevokeApiKeyResponse> Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(request.ApiKeyId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(ApiKey), request.ApiKeyId);

        apiKey.Revoke();
        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken).ConfigureAwait(false);

        return new RevokeApiKeyResponse(apiKey.Id, Revoked: true);
    }
}
```

- [ ] **Step 8: `RequireAdminSecretFilter`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace OrchestAI.Infrastructure.Tenancy;

// Gates the admin-bootstrap controller (Tenant/ApiKey creation) — deliberately separate from
// the tenant API-key auth middleware (Task 9). An ordinary tenant must never be able to create
// another tenant or mint itself unlimited keys; this is operator-only, gated by a single static
// secret configured out-of-band (env var), never a tenant API key. See ADR-014 confirmation #8.
public sealed class RequireAdminSecretFilter : IAsyncActionFilter
{
    private readonly IConfiguration _configuration;

    public RequireAdminSecretFilter(IConfiguration configuration) => _configuration = configuration;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expectedSecret = _configuration["Admin:BootstrapSecret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Admin-Secret", out var provided) ||
            !ConstantTimeEquals(provided.ToString(), expectedSecret))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        await next().ConfigureAwait(false);
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
```

- [ ] **Step 9: `AdminController`**

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Commands.CreateApiKey;
using OrchestAI.Application.Commands.CreateTenant;
using OrchestAI.Application.Commands.RevokeApiKey;
using OrchestAI.Application.Exceptions;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.API.Controllers;

// Operator-only bootstrap surface — never reachable by a tenant-authenticated API key (Task 9's
// middleware only applies to /api/v1 routes OTHER than this admin prefix; see Task 9's exact
// middleware scoping). Gated by RequireAdminSecretFilter, not tenant auth.
[ApiController]
[Route("api/v1/admin")]
[ServiceFilter(typeof(RequireAdminSecretFilter))]
public sealed class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IMediator mediator, ILogger<AdminController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("tenants")]
    [ProducesResponseType(typeof(CreateTenantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTenantAsync([FromBody] CreateTenantCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction(nameof(CreateTenantAsync), response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for CreateTenant: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    [HttpPost("api-keys")]
    [ProducesResponseType(typeof(CreateApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateApiKeyAsync([FromBody] CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction(nameof(CreateApiKeyAsync), response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
    }

    [HttpPost("api-keys/{apiKeyId:guid}/revoke")]
    [ProducesResponseType(typeof(RevokeApiKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeApiKeyAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new RevokeApiKeyCommand(apiKeyId), cancellationToken);
            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
    }
}
```

- [ ] **Step 10: Register in DI and configuration**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, add:

```csharp
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<RequireAdminSecretFilter>();
```

In `src/OrchestAI.API/appsettings.json` and `appsettings.Development.json`, add an empty placeholder (never a real secret committed) alongside the existing top-level sections:

```json
  "Admin": {
    "BootstrapSecret": ""
  },
```

- [ ] **Step 11: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~CreateTenantHandlerTests|FullyQualifiedName~CreateApiKeyHandlerTests|FullyQualifiedName~RevokeApiKeyHandlerTests|FullyQualifiedName~RequireAdminSecretFilterTests"`
Expected: PASS, all tests green.

Run: `dotnet build OrchestAI.sln`
Expected: 0 errors.

- [ ] **Step 12: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/ITenantRepository.cs src/OrchestAI.Domain/Interfaces/IApiKeyRepository.cs \
  src/OrchestAI.Infrastructure/Repositories/TenantRepository.cs src/OrchestAI.Infrastructure/Repositories/ApiKeyRepository.cs \
  src/OrchestAI.Application/Commands/CreateTenant/ src/OrchestAI.Application/Commands/CreateApiKey/ \
  src/OrchestAI.Application/Commands/RevokeApiKey/ src/OrchestAI.Infrastructure/Tenancy/RequireAdminSecretFilter.cs \
  src/OrchestAI.API/Controllers/AdminController.cs src/OrchestAI.Infrastructure/DependencyInjection.cs \
  src/OrchestAI.API/appsettings.json src/OrchestAI.API/appsettings.Development.json \
  tests/OrchestAI.Tests/Application/CreateTenantHandlerTests.cs tests/OrchestAI.Tests/Application/CreateApiKeyHandlerTests.cs \
  tests/OrchestAI.Tests/Application/RevokeApiKeyHandlerTests.cs tests/OrchestAI.Tests/Infrastructure/RequireAdminSecretFilterTests.cs
git commit -m "feat: add operator-only Tenant/ApiKey bootstrap commands and admin-secret-gated controller"
```

- [ ] **Step 13: Add `SuspendTenantCommand`/`ReactivateTenantCommand`**

The domain model (`Tenant.Suspend()`/`Reactivate()`, Task 1) has no admin surface exposing it yet — add one now, following the exact pattern of `RevokeApiKeyCommand`/`Handler`/`Response` above.

```csharp
using MediatR;

namespace OrchestAI.Application.Commands.SuspendTenant;

public sealed record SuspendTenantCommand(Guid TenantId) : IRequest<SuspendTenantResponse>;
```

```csharp
namespace OrchestAI.Application.Commands.SuspendTenant;

public sealed record SuspendTenantResponse(Guid TenantId, string Status);
```

```csharp
using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.SuspendTenant;

public sealed class SuspendTenantHandler : IRequestHandler<SuspendTenantCommand, SuspendTenantResponse>
{
    private readonly ITenantRepository _tenantRepository;

    public SuspendTenantHandler(ITenantRepository tenantRepository) => _tenantRepository = tenantRepository;

    public async Task<SuspendTenantResponse> Handle(SuspendTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

        tenant.Suspend();
        await _tenantRepository.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);

        return new SuspendTenantResponse(tenant.Id, tenant.Status.ToString());
    }
}
```

`ITenantRepository.UpdateAsync`/`TenantRepository.UpdateAsync` are already defined above in this same task (Step 3/4) — this step only adds the command, handler, and controller endpoint that call it.

Add the corresponding endpoint to `AdminController` (Task 8, Step 9):

```csharp
    [HttpPost("tenants/{tenantId:guid}/suspend")]
    [ProducesResponseType(typeof(SuspendTenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new SuspendTenantCommand(tenantId), cancellationToken);
            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
    }
```

(Add `using OrchestAI.Application.Commands.SuspendTenant;` to `AdminController.cs`'s usings.)

Add a test mirroring `RevokeApiKeyHandlerTests.cs`'s exact shape, in a new `tests/OrchestAI.Tests/Application/SuspendTenantHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.SuspendTenant;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class SuspendTenantHandlerTests
{
    [Fact]
    public async Task Handle_ExistingTenant_SuspendsAndPersists()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        repoMock.Setup(r => r.UpdateAsync(tenant, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new SuspendTenantHandler(repoMock.Object);
        var response = await handler.Handle(new SuspendTenantCommand(tenant.Id), CancellationToken.None);

        response.Status.Should().Be(nameof(TenantStatus.Suspended));
        tenant.Status.Should().Be(TenantStatus.Suspended);
        repoMock.Verify(r => r.UpdateAsync(tenant, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnknownTenant_ThrowsNotFound()
    {
        var repoMock = new Mock<ITenantRepository>();
        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var handler = new SuspendTenantHandler(repoMock.Object);
        var act = async () => await handler.Handle(new SuspendTenantCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~SuspendTenantHandlerTests"` — expect PASS. Run `dotnet build OrchestAI.sln` — expect 0 errors. Commit alongside Task 8's other files (or as a small follow-up commit: `git add src/OrchestAI.Application/Commands/SuspendTenant/ src/OrchestAI.API/Controllers/AdminController.cs src/OrchestAI.Domain/Interfaces/ITenantRepository.cs src/OrchestAI.Infrastructure/Repositories/TenantRepository.cs tests/OrchestAI.Tests/Application/SuspendTenantHandlerTests.cs && git commit -m "feat: add SuspendTenantCommand and admin endpoint"`).

---

### Task 9: Tenant API-key authentication middleware

**Files:**
- Create: `src/OrchestAI.Infrastructure/Tenancy/TenantAuthenticationMiddleware.cs`
- Modify: `src/OrchestAI.API/Program.cs` (wire the middleware in)
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register middleware)
- Test: Create `tests/OrchestAI.Tests/Infrastructure/TenantAuthenticationMiddlewareTests.cs`

**Interfaces:**
- Consumes: `IApiKeyHasher` (Task 7), `IApiKeyRepository`/`ITenantRepository` (Task 8), `ICurrentTenantAccessor` (Task 3).
- Produces: `TenantAuthenticationMiddleware : IMiddleware` — parses `Authorization: Bearer <rawKey>`, resolves + validates the key/tenant, sets the ambient tenant scope for the duration of the request, debounces `LastUsedAt` updates, exempts `/health`, `/swagger`, and `/api/v1/admin/*`.

**Status code contract (confirmation #9):** missing/malformed/unknown/revoked/expired key → **401**. Valid key, but its tenant is `Suspended` → **403**. This distinction matters — 401 means "prove who you are again," 403 means "we know who you are, and the answer is no."

- [ ] **Step 1: Write the failing tests**

Create `tests/OrchestAI.Tests/Infrastructure/TenantAuthenticationMiddlewareTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class TenantAuthenticationMiddlewareTests
{
    private static (TenantAuthenticationMiddleware Middleware, Mock<IApiKeyHasher> Hasher,
        Mock<IApiKeyRepository> ApiKeyRepo, Mock<ITenantRepository> TenantRepo, AsyncLocalCurrentTenantAccessor Accessor)
        Build()
    {
        var hasher = new Mock<IApiKeyHasher>();
        var apiKeyRepo = new Mock<IApiKeyRepository>();
        var tenantRepo = new Mock<ITenantRepository>();
        var accessor = new AsyncLocalCurrentTenantAccessor();

        var middleware = new TenantAuthenticationMiddleware(
            hasher.Object, apiKeyRepo.Object, tenantRepo.Object, accessor,
            NullLogger<TenantAuthenticationMiddleware>.Instance);

        return (middleware, hasher, apiKeyRepo, tenantRepo, accessor);
    }

    private static DefaultHttpContext BuildContext(string path, string? authHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (authHeader is not null)
            context.Request.Headers.Authorization = authHeader;
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ValidKey_SetsTenantScopeDuringNextAndCallsIt()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, accessor) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");

        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        apiKeyRepo.Setup(r => r.UpdateAsync(apiKey, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");
        Guid? observedTenantId = null;
        var nextCalled = false;

        await middleware.InvokeAsync(context, ctx =>
        {
            nextCalled = true;
            observedTenantId = accessor.TenantId;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        observedTenantId.Should().Be(tenant.Id);
        context.Response.StatusCode.Should().Be(200); // untouched default
        accessor.TenantId.Should().BeNull("the ambient scope must be cleared once the request finishes");
    }

    [Fact]
    public async Task InvokeAsync_MissingAuthorizationHeader_Returns401()
    {
        var (middleware, _, _, _, _) = Build();
        var context = BuildContext("/api/v1/eval-suites");
        var nextCalled = false;

        await middleware.InvokeAsync(context, ctx => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_MalformedKey_Returns401()
    {
        var (middleware, hasher, _, _, _) = Build();
        hasher.Setup(h => h.Parse(It.IsAny<string>())).Returns((ParsedApiKey?)null);
        var context = BuildContext("/api/v1/eval-suites", "Bearer garbage");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_UnknownPublicKeyId_Returns401()
    {
        var (middleware, hasher, apiKeyRepo, _, _) = Build();
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync((ApiKey?)null);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_WrongSecret_Returns401()
    {
        var (middleware, hasher, apiKeyRepo, _, _) = Build();
        var apiKey = ApiKey.Create(Guid.NewGuid(), "pk123", "hashed");
        hasher.Setup(h => h.Parse("orch_live_pk123.wrong")).Returns(new ParsedApiKey("pk123", "wrong"));
        hasher.Setup(h => h.Verify("wrong", "hashed")).Returns(false);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.wrong");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_RevokedKey_Returns401()
    {
        var (middleware, hasher, apiKeyRepo, _, _) = Build();
        var apiKey = ApiKey.Create(Guid.NewGuid(), "pk123", "hashed");
        apiKey.Revoke();
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ValidKeyForSuspendedTenant_Returns403()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, _) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Suspend();
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/swagger/index.html")]
    [InlineData("/api/v1/admin/tenants")]
    public async Task InvokeAsync_ExemptPaths_SkipAuthEntirely(string path)
    {
        var (middleware, _, _, _, _) = Build();
        var context = BuildContext(path);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_RecentlyUsedKey_DoesNotUpdateLastUsedAtAgain()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, _) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");
        apiKey.RecordUsage(); // simulate a very recent prior use
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        apiKeyRepo.Verify(r => r.UpdateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()), Times.Never,
            "a key used moments ago must not trigger another LastUsedAt write within the debounce window");
    }

    [Fact]
    public async Task InvokeAsync_UsageRecordingFails_AuthenticationStillSucceeds()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, _) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        apiKeyRepo.Setup(r => r.UpdateAsync(apiKey, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("transient DB error"));
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue("a failed best-effort LastUsedAt write must never block authentication");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantAuthenticationMiddlewareTests"`
Expected: FAIL — `TenantAuthenticationMiddleware` doesn't exist yet (compile error).

- [ ] **Step 3: Implement the middleware**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Tenancy;

// Missing/malformed/unknown/revoked/expired key -> 401. Valid key, suspended tenant -> 403.
// Sets the ambient ICurrentTenantAccessor scope for the request's downstream pipeline only —
// the scope is disposed (cleared) the instant this middleware returns. See ADR-014.
public sealed class TenantAuthenticationMiddleware : IMiddleware
{
    private static readonly TimeSpan LastUsedAtDebounceInterval = TimeSpan.FromMinutes(10);
    private const string BearerPrefix = "Bearer ";

    private readonly IApiKeyHasher _hasher;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ILogger<TenantAuthenticationMiddleware> _logger;

    public TenantAuthenticationMiddleware(
        IApiKeyHasher hasher,
        IApiKeyRepository apiKeyRepository,
        ITenantRepository tenantRepository,
        ICurrentTenantAccessor tenantAccessor,
        ILogger<TenantAuthenticationMiddleware> logger)
    {
        _hasher = hasher;
        _apiKeyRepository = apiKeyRepository;
        _tenantRepository = tenantRepository;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (IsExemptPath(context.Request.Path))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var rawKey = header[BearerPrefix.Length..].Trim();
        var parsed = _hasher.Parse(rawKey);
        if (parsed is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var apiKey = await _apiKeyRepository.GetByPublicKeyIdAsync(parsed.PublicKeyId, context.RequestAborted).ConfigureAwait(false);
        if (apiKey is null || !apiKey.IsUsable() || !_hasher.Verify(parsed.RawSecret, apiKey.HashedSecret))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var tenant = await _tenantRepository.GetByIdAsync(apiKey.TenantId, context.RequestAborted).ConfigureAwait(false);
        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (tenant.Status != TenantStatus.Active)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await MaybeRecordUsageAsync(apiKey, context.RequestAborted).ConfigureAwait(false);

        using (_tenantAccessor.SetTenant(tenant.Id))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private static bool IsExemptPath(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/swagger") ||
        path.StartsWithSegments("/api/v1/admin");

    private async Task MaybeRecordUsageAsync(ApiKey apiKey, CancellationToken cancellationToken)
    {
        if (apiKey.LastUsedAt is { } lastUsed && DateTimeOffset.UtcNow - lastUsed < LastUsedAtDebounceInterval)
            return;

        try
        {
            apiKey.RecordUsage();
            await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort — a failed usage-timestamp write must never fail authentication itself.
            _logger.LogWarning(ex, "Failed to record API key usage timestamp for key {PublicKeyId}", apiKey.PublicKeyId);
        }
    }
}
```

- [ ] **Step 4: Register in DI and wire into `Program.cs`**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`:

```csharp
        services.AddScoped<TenantAuthenticationMiddleware>();
```

In `src/OrchestAI.API/Program.cs`, add `using OrchestAI.Infrastructure.Tenancy;` and insert the middleware between `app.UseCors("Frontend");` and `app.MapControllers();`:

```csharp
    app.UseCors("Frontend");
    app.UseMiddleware<TenantAuthenticationMiddleware>();
    app.MapControllers();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantAuthenticationMiddlewareTests"`
Expected: PASS, all 10 tests green (7 facts + 3 theory cases).

Run: `dotnet build OrchestAI.sln`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Infrastructure/Tenancy/TenantAuthenticationMiddleware.cs \
  src/OrchestAI.API/Program.cs src/OrchestAI.Infrastructure/DependencyInjection.cs \
  tests/OrchestAI.Tests/Infrastructure/TenantAuthenticationMiddlewareTests.cs
git commit -m "feat: add tenant API-key authentication middleware with fail-closed status codes"
```

---

### Task 10: Explicit foreign-ID ownership checks (the filter alone is not enough)

**Files:**
- Modify: `src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs` — **read the current file first**; the exact addition is a single guard block, shown below, inserted before `EvalRun.Create(...)`. Do not assume the rest of the file's structure without reading it.
- Test: Modify `tests/OrchestAI.Tests/Application/RunEvalSuiteHandlerTests.cs`
- Test: Create `tests/OrchestAI.Tests/Application/ResumeOrchestrationTaskHandlerCrossTenantTests.cs`
- Test: Create `tests/OrchestAI.Tests/Application/RequestPostHocScoringExplicitTraceIdsCrossTenantTests.cs`

**What actually needs a code change vs. what the filter already handles — verified, not assumed:**

- **`RunEvalSuiteCommand.BaselineRunId`: needs a real code change.** Confirmed by re-reading the handler: it never looks the baseline run up at all today — it passes `request.BaselineRunId` straight into `EvalRun.Create(suite.Id, request.SubjectVersion, request.BaselineRunId)` with zero validation that it exists, let alone belongs to the caller. This is exactly the "accepts a foreign ID with no ownership check" gap confirmation #3 calls out.
- **`ResumeOrchestrationTaskCommand.TaskId`: the filter alone already handles this correctly — no code change, only a test.** The handler already does `_taskRepository.GetByIdWithExecutionsAsync(request.TaskId, ...) ?? throw new NotFoundException(...)`. Once Task 4's global filter is active, a foreign tenant's `TaskId` is invisible to this query regardless of who asks — it naturally 404s. Write the test proving this; do not add new ownership-check code here (if you find yourself wanting to, that's a signal the filter isn't working, not a normal step — see Task 4's own note).
- **`RequestPostHocScoringCommand`'s explicit `TraceIds` list: the filter silently excludes foreign IDs — this is correct, not a gap.** `SelectForPostHocScoringAsync` queries `AgentExecutions`, which is tenant-filtered. A cross-tenant ID in an explicit list is simply invisible and gets excluded from `TotalMatched`/the resolved set — the same way a date-range selection already silently scopes to what's visible. Write the test proving this; do not add explicit per-ID rejection logic (that would be inconsistent with how the rest of this command already treats "not visible" as "not selected," not as an error).

- [ ] **Step 1: Write the failing test for `RunEvalSuiteHandler`**

Add to `tests/OrchestAI.Tests/Application/RunEvalSuiteHandlerTests.cs` (read the file first to match its exact existing mock-setup style before adding):

```csharp
    [Fact]
    public async Task Handle_BaselineRunIdBelongingToAnotherTenant_ThrowsNotFound()
    {
        // Simulates the tenant-filtered repository's real behavior: a foreign-tenant
        // BaselineRunId resolves to null via GetByIdAsync, exactly as it would once the global
        // query filter (Task 4) is live against a real AppDbContext scoped to a different tenant.
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var foreignBaselineRunId = Guid.NewGuid();

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(foreignBaselineRunId, It.IsAny<CancellationToken>())).ReturnsAsync((EvalRun?)null);

        var handler = new RunEvalSuiteHandler(suiteRepoMock.Object, runRepoMock.Object, Mock.Of<IEvalRunQueue>(), NullLogger<RunEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new RunEvalSuiteCommand(suite.Id, "v1", foreignBaselineRunId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        runRepoMock.Verify(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()), Times.Never,
            "no EvalRun should be created when the requested baseline can't be verified");
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~RunEvalSuiteHandlerTests"`
Expected: FAIL — the new test's `runRepoMock.Verify(...AddAsync... Times.Never)` fails because the current handler creates the run unconditionally without ever calling `GetByIdAsync` on the baseline.

- [ ] **Step 3: Add the ownership guard to `RunEvalSuiteHandler`**

Read `src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs` in full first. Insert this guard immediately before the line that calls `EvalRun.Create(...)`:

```csharp
        if (request.BaselineRunId is { } baselineRunId)
        {
            _ = await _runRepository.GetByIdAsync(baselineRunId, cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException(nameof(EvalRun), baselineRunId);
        }
```

(`_runRepository` is the handler's existing `IEvalRunRepository` field — confirm its exact field name by reading the file; do not guess.)

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~RunEvalSuiteHandlerTests"`
Expected: PASS, including all pre-existing tests in the file (the guard only activates when `BaselineRunId` is non-null, so the existing no-baseline test cases are unaffected).

- [ ] **Step 5: Write the `ResumeOrchestrationTaskHandler` cross-tenant test (no code change)**

Create `tests/OrchestAI.Tests/Application/ResumeOrchestrationTaskHandlerCrossTenantTests.cs` — read `ResumeOrchestrationTaskHandler.cs` and its existing test file first to match constructor/mock conventions exactly, then add:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.ResumeOrchestrationTask;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

// Proves confirmation #3's claim for THIS specific command: no new ownership-check code is
// needed here because the tenant query filter (Task 4) already makes a foreign tenant's TaskId
// invisible to GetByIdWithExecutionsAsync, which the handler already null-checks into a
// NotFoundException. This test documents that the filter is sufficient for a "fetch by ID as
// the request's primary subject" pattern — unlike RunEvalSuiteCommand's BaselineRunId, which
// needed an explicit new lookup because the handler never looked the referenced entity up at all.
public sealed class ResumeOrchestrationTaskHandlerCrossTenantTests
{
    [Fact]
    public async Task Handle_TaskIdInvisibleUnderCurrentTenantFilter_ThrowsNotFound()
    {
        var foreignTaskId = Guid.NewGuid();
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        // Simulates the tenant-filtered repository call returning null for a foreign tenant's
        // task, exactly as the real filtered AppDbContext would.
        taskRepoMock.Setup(r => r.GetByIdWithExecutionsAsync(foreignTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.OrchestrationTask?)null);

        // Construct the handler with whatever its other dependencies are, per the file you just
        // read — mock them minimally (Mock.Of<T>()) since this test only exercises the
        // not-found path before any of them would be touched.
        var handler = BuildHandlerWithMinimalMocks(taskRepoMock.Object);

        var act = async () => await handler.Handle(
            new ResumeOrchestrationTaskCommand(foreignTaskId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // Fill this in based on ResumeOrchestrationTaskHandler's actual constructor signature (read
    // the file — do not guess). Every other dependency can be a bare Mock.Of<T>() since the
    // not-found path returns before touching them.
    private static object BuildHandlerWithMinimalMocks(IOrchestrationTaskRepository taskRepository)
    {
        throw new NotImplementedException("Replace with the real ResumeOrchestrationTaskHandler constructor call once you've read its actual dependency list.");
    }
}
```

**This step's test scaffold is intentionally incomplete** — `ResumeOrchestrationTaskHandler`'s full constructor dependency list was not re-read in this planning session closely enough to specify exactly (it's known to take more than just `IOrchestrationTaskRepository`, per the investigation's mention of sub-agent execution/memory injection). Read the actual file now, replace `BuildHandlerWithMinimalMocks`'s body with a real constructor call passing `Mock.Of<T>()` for every other dependency, delete the `throw new NotImplementedException(...)`, and only then proceed to Step 6.

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~ResumeOrchestrationTaskHandlerCrossTenantTests"`
Expected: PASS — no production code change was needed for this one; the test should pass against the handler exactly as it exists today (plus Task 4's filter, which this test simulates via the repository mock rather than a real filtered `AppDbContext`).

- [ ] **Step 7: Write the `RequestPostHocScoringHandler` explicit-`TraceIds` cross-tenant test (no code change)**

Create `tests/OrchestAI.Tests/Application/RequestPostHocScoringExplicitTraceIdsCrossTenantTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Application;

// Proves confirmation #3's claim for explicit TraceIds: a cross-tenant ID in the list is
// silently excluded by the (mocked-here, real-in-production) tenant-filtered
// SelectForPostHocScoringAsync query — it does not throw, it just isn't in the resolved set.
// This is deliberately the SAME behavior as a date-range selection already has (silently
// scoped to what's visible), not a new explicit-rejection code path.
public sealed class RequestPostHocScoringExplicitTraceIdsCrossTenantTests
{
    [Fact]
    public async Task Handle_ExplicitTraceIdsIncludingForeignTenantId_ForeignIdSilentlyExcluded()
    {
        var ownTraceId = Guid.NewGuid();
        var foreignTraceId = Guid.NewGuid();

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        // Simulates the tenant-filtered repository call: the foreign trace ID was in the
        // request, but the (real, filtered) query only ever resolves the caller's own trace —
        // TotalMatched reflects only what's actually visible.
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                null, null, null,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ownTraceId) && ids.Contains(foreignTraceId)),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult([ownTraceId], TotalMatched: 1));

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.AddAsync(It.IsAny<Domain.Entities.EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var options = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });

        var handler = new RequestPostHocScoringHandler(
            executionRepoMock.Object, runRepoMock.Object, queueMock.Object, options,
            NullLogger<RequestPostHocScoringHandler>.Instance);

        var command = new RequestPostHocScoringCommand(
            DateFrom: null, DateTo: null, AgentType: null, TraceIds: [ownTraceId, foreignTraceId],
            ScorerType: EvalScorerType.LlmJudge, Rubric: "was it appropriate?", PassThreshold: null, MaxTraces: 10);

        var response = await handler.Handle(command, CancellationToken.None);

        response.ResolvedTraceCount.Should().Be(1, "the foreign-tenant trace ID must be silently excluded, not rejected as an error");
    }
}
```

- [ ] **Step 8: Run to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~RequestPostHocScoringExplicitTraceIdsCrossTenantTests"`
Expected: PASS — no production code change needed; `RequestPostHocScoringHandler` already just forwards `TraceIds` into `SelectForPostHocScoringAsync` and trusts its resolved count.

- [ ] **Step 9: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, full suite green.

- [ ] **Step 10: Commit**

```bash
git add src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs \
  tests/OrchestAI.Tests/Application/RunEvalSuiteHandlerTests.cs \
  tests/OrchestAI.Tests/Application/ResumeOrchestrationTaskHandlerCrossTenantTests.cs \
  tests/OrchestAI.Tests/Application/RequestPostHocScoringExplicitTraceIdsCrossTenantTests.cs
git commit -m "feat: verify baseline-run ownership explicitly; prove filter-based isolation for resume and post-hoc trace selection"
```

---

### Task 11: Background propagation — `TenantId` travels with queued work, the worker restores it explicitly

**Files:**
- Modify: `src/OrchestAI.Domain/Interfaces/IEvalRunQueue.cs`
- Create: `src/OrchestAI.Domain/Models/EvalRunQueueItem.cs`
- Modify: `src/OrchestAI.Infrastructure/Eval/InMemoryEvalRunQueue.cs`
- Modify: `src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs`
- Modify: `src/OrchestAI.Application/Commands/RequestPostHocScoring/RequestPostHocScoringHandler.cs`
- Modify: `src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs`
- Test: Modify `tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerTests.cs`, `EvalRunBackgroundWorkerPostHocTests.cs` (update `DequeueAsync`/`EnqueueAsync` mock setups for the new signature)
- Test: Modify every other test file with an `IEvalRunQueue.EnqueueAsync` mock setup — this includes, at minimum, `RunEvalSuiteHandlerTests.cs` and the `RequestPostHocScoringExplicitTraceIdsCrossTenantTests` class added in Task 10 (`queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))` — this two-argument form no longer compiles once this task's Step 3 lands)
- Test: Create `tests/OrchestAI.Tests/Integration/CrossTenantBackgroundFlowIntegrationTests.cs`

**Interfaces:**
- Consumes: `EvalRun.TenantId` (already stamped by `TenantScopingInterceptor` — Task 5 — by the time `AddAsync` returns, since the handler calls it while the HTTP request's ambient tenant scope from Task 9's middleware is still active), `ITenantRepository` (Task 8), `TenantStatus` (Task 1).
- Produces: `IEvalRunQueue.EnqueueAsync(Guid evalRunId, Guid tenantId, CancellationToken)`; `IEvalRunQueue.DequeueAsync(...)` now returns `EvalRunQueueItem(Guid EvalRunId, Guid TenantId)`; `EvalRunBackgroundWorker` restores the ambient tenant scope from the dequeued item **before** touching any tenant-scoped repository, and checks the owning tenant's status before doing any work.

**Why the handlers don't need a new `ICurrentTenantAccessor` dependency:** `EvalRun` is `ITenantScoped`. By the time `RunEvalSuiteHandler`/`RequestPostHocScoringHandler` call `await _runRepository.AddAsync(run, cancellationToken)`, `TenantScopingInterceptor` has already stamped `run`'s `TenantId` from the ambient tenant (set by Task 9's middleware for the whole HTTP request) — and because the interceptor sets it via `entry.Property(...).CurrentValue`, the **in-memory** `run` object's `TenantId` is updated too, not just the database row (this is exactly how `UpdatedAtInterceptor` already makes `UpdatedAt` correct on the in-memory entity after `SaveChangesAsync`). So the handler can simply read `run.TenantId` straight off the entity after `AddAsync` returns — no new dependency, no re-deriving the ambient tenant a second time.

- [ ] **Step 1: Write the failing worker/queue tests first**

In `tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerTests.cs` and `EvalRunBackgroundWorkerPostHocTests.cs`, every existing `queueMock.Setup(q => q.DequeueAsync(...))` and any direct call to `EnqueueAsync` will now fail to compile against the new `EvalRunQueueItem`-returning signature — this is expected. Fix each one by wrapping the existing `run.Id` (or `evalRunId`) in `new EvalRunQueueItem(run.Id, /* whatever tenant the test's EvalRun already has */)`. Since these existing tests call `worker.ProcessRunAsync(run.Id, ...)` directly (not through `ExecuteAsync`/`DequeueAsync` at all), most of them are unaffected by the queue signature change — only tests that directly touch `IEvalRunQueue` mocks need updating. Read both files now and fix each affected setup before proceeding; do not leave any file in a non-compiling state.

Before moving on, run `grep -rn "EnqueueAsync" tests/` from the repo root and fix **every** match, not just the two files above — the two-argument mock form `queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))` no longer matches the interface once Step 3 below lands, wherever it appears. This is known to include `tests/OrchestAI.Tests/Application/RunEvalSuiteHandlerTests.cs` and the `RequestPostHocScoringExplicitTraceIdsCrossTenantTests` class added in Task 10 — update each to `queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))`. Do not proceed to Step 2 with any test file left non-compiling.

Add this new test to `tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerPostHocTests.cs` (or a new adjacent file if you prefer, matching the existing file's `BuildWorker` helper — extend it to also accept an `ITenantRepository` mock):

```csharp
    [Fact]
    public async Task ProcessRunAsync_TenantSuspendedAfterEnqueue_RunMarkedFailedNotCompleted()
    {
        var taskId = Guid.NewGuid();
        var execution = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        execution.Start();
        execution.Complete("output", 10, 5, 0.01m);

        var criteriaJson = System.Text.Json.JsonSerializer.Serialize(new { resolvedTraceIds = new[] { execution.Id } });
        var run = EvalRun.CreatePostHoc("posthoc-1", "was the tool call appropriate?", criteriaJson);
        var tenantId = Guid.NewGuid();
        typeof(EvalRun).GetProperty(nameof(EvalRun.TenantId))!.SetValue(run, tenantId);

        var suspendedTenant = Tenant.Create("Acme", "acme");
        suspendedTenant.Suspend();

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(suspendedTenant);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        var executionRepoMock = new Mock<IAgentExecutionRepository>();

        var worker = BuildWorkerWithTenantRepository(
            runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, tenantRepoMock.Object);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.Status.Should().Be(EvalRunStatus.Failed, "a tenant suspended after enqueue must reject queued work, not silently complete it");
        executionRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "no trace should even be looked up once the owning tenant is known to be suspended");
    }
```

Add a `BuildWorkerWithTenantRepository` helper alongside the existing `BuildWorker` in the same file, mirroring its DI-container setup exactly but also registering `ITenantRepository` (and any scorer factory the existing helper already wires) — read the existing `BuildWorker` method first and extend it minimally rather than duplicating its whole body if a single optional parameter can serve both.

- [ ] **Step 2: Run to verify the new/updated tests fail**

Run: `dotnet build OrchestAI.sln`
Expected: compile errors everywhere `IEvalRunQueue`'s old signature is used — this confirms the interface hasn't changed yet. Fix the interface/implementation next, then return to fix call sites.

- [ ] **Step 3: Update `IEvalRunQueue` and add `EvalRunQueueItem`**

```csharp
namespace OrchestAI.Domain.Models;

// TenantId travels with the queued item because EvalRunBackgroundWorker processes this entirely
// outside any HTTP request — there is no ambient tenant context to infer once dequeued; it must
// be captured explicitly at enqueue time. See ADR-014 confirmation #5.
public sealed record EvalRunQueueItem(Guid EvalRunId, Guid TenantId);
```

Replace `src/OrchestAI.Domain/Interfaces/IEvalRunQueue.cs`:

```csharp
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalRunQueue
{
    Task EnqueueAsync(Guid evalRunId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<EvalRunQueueItem> DequeueAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Update `InMemoryEvalRunQueue`**

Read the current file first, then replace its channel type and both methods to match:

```csharp
using System.Threading.Channels;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Eval;

public sealed class InMemoryEvalRunQueue : IEvalRunQueue
{
    private readonly Channel<EvalRunQueueItem> _channel = Channel.CreateUnbounded<EvalRunQueueItem>();

    public async Task EnqueueAsync(Guid evalRunId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(new EvalRunQueueItem(evalRunId, tenantId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<EvalRunQueueItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Update the two enqueue call sites**

In `src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs`, change:
```csharp
        await _queue.EnqueueAsync(run.Id, cancellationToken).ConfigureAwait(false);
```
to:
```csharp
        await _queue.EnqueueAsync(run.Id, run.TenantId, cancellationToken).ConfigureAwait(false);
```

In `src/OrchestAI.Application/Commands/RequestPostHocScoring/RequestPostHocScoringHandler.cs`, change the equivalent line the same way: `await _queue.EnqueueAsync(run.Id, run.TenantId, cancellationToken).ConfigureAwait(false);`. Read each file first to find the exact current line before editing — do not assume identical surrounding formatting between the two files.

- [ ] **Step 6: Update `EvalRunBackgroundWorker`**

Read the current file in full. Change `ExecuteAsync` from:
```csharp
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid evalRunId;
            try
            {
                evalRunId = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessRunAsync(evalRunId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Eval run {RunId} processing failed unexpectedly", evalRunId);
            }
        }
    }
```
to:
```csharp
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            EvalRunQueueItem item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                // Restores the ambient tenant BEFORE any tenant-scoped repository call —
                // ProcessRunAsync's very first line fetches the EvalRun itself, which is
                // ITenantScoped, so the scope must already be active by the time it's called.
                using var tenantScope = _tenantAccessor.SetTenant(item.TenantId);
                await ProcessRunAsync(item.EvalRunId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Eval run {RunId} processing failed unexpectedly", item.EvalRunId);
            }
        }
    }
```

Add a new constructor-injected field `private readonly ICurrentTenantAccessor _tenantAccessor;` (this is a singleton hosted service; `ICurrentTenantAccessor` is registered Singleton — this is a clean, valid injection with no scope mismatch), set in the constructor alongside the existing `_queue`/`_scopeFactory`/`_logger` fields — add `ICurrentTenantAccessor tenantAccessor` as a new constructor parameter and update every existing call site that constructs `EvalRunBackgroundWorker` directly (both in `DependencyInjection.cs`'s `AddHostedService<EvalRunBackgroundWorker>()` — no change needed there, DI resolves it automatically — and in every test file that calls `new EvalRunBackgroundWorker(...)` directly, which all need the new argument added).

In `ProcessRunAsync`, add a tenant-status check immediately after the existing `run is null` guard and before the `run.Source == EvalRunSource.PostHoc` branch:

```csharp
        var tenant = await tenantRepository.GetByIdAsync(run.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            run.MarkFailed(tenant is null
                ? "Owning tenant no longer exists."
                : "Tenant was suspended after this run was enqueued.");
            await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Eval run {RunId} rejected — tenant {TenantId} is not active", run.Id, run.TenantId);
            return;
        }
```

This needs `var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();` added alongside `ProcessRunAsync`'s existing scope-resolution lines (`runRepository`, `suiteRepository`, etc.) — add it there, in the same style as the rest.

- [ ] **Step 7: Write the cross-tenant background-flow integration test**

Create `tests/OrchestAI.Tests/Integration/CrossTenantBackgroundFlowIntegrationTests.cs`, mirroring Week 9's `PostHocScoringIntegrationTests` structure (real repositories against a shared in-memory `AppDbContext`, only `ILlmProvider`/`IModelPricingCache` mocked) but adding the tenant dimension end to end:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Eval;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Integration;

// Proves the full background-flow propagation contract from ADR-014 confirmation #5: tenant A
// authenticates (simulated by setting the ambient tenant), triggers post-hoc scoring, the
// "HTTP request" ends (the scope is disposed), the worker processes the queued item entirely
// outside that scope by restoring TenantId from the persisted EvalRun, and tenant B's own
// ambient scope can never see any of tenant A's resulting data.
public sealed class CrossTenantBackgroundFlowIntegrationTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName, ICurrentTenantAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return new PooledDbContextFactory<AppDbContext>(options, accessor);
    }

    [Fact]
    public async Task FullFlow_TenantAEnqueuesPostHocScoring_WorkerRestoresTenantOutsideHttpScope_TenantBCannotSeeResults()
    {
        var dbName = Guid.NewGuid().ToString();
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = BuildFactory(dbName, accessor);
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        var executionRepository = new AgentExecutionRepository(factory);
        var runRepository = new EvalRunRepository(factory);
        var resultRepository = new EvalResultRepository(factory);
        var tenantRepository = new TenantRepository(factory);
        var queue = new InMemoryEvalRunQueue();

        // Seed tenant A's tenant row and one historical AgentExecution, scoped to tenant A.
        Guid executionId, taskId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var user = TestUserFactory.Create("cross-tenant-bg@test.local");
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            execution.Start();
            execution.Complete("Researched thoroughly.", 100, 50, 0.02m);
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            executionId = execution.Id;
            taskId = task.Id;
        }

        // "HTTP request" for tenant A: submit the post-hoc scoring request while tenant A's
        // scope is active, then the scope ends (simulating the request finishing).
        Guid evalRunId;
        using (accessor.SetTenant(tenantAId))
        {
            var evalOptions = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });
            var requestHandler = new RequestPostHocScoringHandler(
                executionRepository, runRepository, queue, evalOptions, NullLogger<RequestPostHocScoringHandler>.Instance);

            var command = new RequestPostHocScoringCommand(
                DateFrom: DateTimeOffset.UtcNow.AddDays(-1), DateTo: DateTimeOffset.UtcNow.AddDays(1),
                AgentType: AgentType.Research, TraceIds: null, ScorerType: EvalScorerType.LlmJudge,
                Rubric: "Did the agent research thoroughly?", PassThreshold: 0.5m, MaxTraces: 10);

            var response = await requestHandler.Handle(command, CancellationToken.None);
            response.ResolvedTraceCount.Should().Be(1);
            evalRunId = response.EvalRunId;
        }
        // Ambient scope for tenant A is now cleared — accessor.TenantId is null here.
        accessor.TenantId.Should().BeNull("the simulated HTTP request has ended");

        // Dequeue and process exactly like EvalRunBackgroundWorker.ExecuteAsync would, entirely
        // outside any tenant scope until the worker restores one from the queued item itself.
        var queuedItem = await queue.DequeueAsync(CancellationToken.None);
        queuedItem.TenantId.Should().Be(tenantAId, "TenantId must have been captured at enqueue time from the EvalRun the interceptor stamped");

        var services = new ServiceCollection();
        services.AddSingleton<IEvalSuiteRepository>(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton<IEvalRunRepository>(runRepository);
        services.AddSingleton<IEvalResultRepository>(resultRepository);
        services.AddSingleton<IOrchestrationTaskRepository>(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton<IAgentExecutionRepository>(executionRepository);
        services.AddSingleton<ITenantRepository>(tenantRepository);
        services.AddSingleton<IAgentFactory>(Mock.Of<IAgentFactory>());

        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", """{"score":0.9,"reasoning":"Thorough."}""", [], 200, 40));
        var providerFactoryMock = new Mock<ILlmProviderFactory>();
        providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);
        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock.Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));
        var costLedgerRepository = new CostLedgerRepository(factory);
        var judgeOptions = Options.Create(new EvalOptions { JudgeModel = "anthropic/claude-haiku-4-5-20251001", DefaultJudgePassThreshold = 0.7m });
        var judgeScorer = new LlmJudgeScorer(providerFactoryMock.Object, pricingCacheMock.Object, costLedgerRepository, judgeOptions);
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([judgeScorer]));
        var provider = services.BuildServiceProvider();

        var worker = new EvalRunBackgroundWorker(
            Mock.Of<IEvalRunQueue>(), provider.GetRequiredService<IServiceScopeFactory>(),
            accessor, NullLogger<EvalRunBackgroundWorker>.Instance);

        // Simulates exactly what ExecuteAsync does: restore the scope from the dequeued item's
        // TenantId, THEN process — never before.
        using (accessor.SetTenant(queuedItem.TenantId))
        {
            await worker.ProcessRunAsync(queuedItem.EvalRunId, CancellationToken.None);
        }

        // Tenant A can see the result.
        using (accessor.SetTenant(tenantAId))
        {
            var results = await resultRepository.GetByRunIdAsync(evalRunId, CancellationToken.None);
            results.Should().ContainSingle();
            results[0].TenantId.Should().Be(tenantAId);
        }

        // Tenant B — a completely different tenant who was never involved — sees nothing.
        using (accessor.SetTenant(tenantBId))
        {
            var results = await resultRepository.GetByRunIdAsync(evalRunId, CancellationToken.None);
            results.Should().BeEmpty("tenant B must never see tenant A's post-hoc scoring results, background-processed or not");

            await using var ctx = await factory.CreateDbContextAsync();
            var visibleExecution = await ctx.AgentExecutions.FirstOrDefaultAsync(e => e.Id == executionId);
            visibleExecution.Should().BeNull("tenant B must not see tenant A's underlying AgentExecution either");
        }
    }
}
```

- [ ] **Step 8: Run to verify everything passes**

Run: `dotnet build OrchestAI.sln`
Expected: 0 errors.

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~EvalRunBackgroundWorkerTests|FullyQualifiedName~EvalRunBackgroundWorkerPostHocTests|FullyQualifiedName~CrossTenantBackgroundFlowIntegrationTests"`
Expected: PASS, including the new suspension test and the full cross-tenant background-flow integration test.

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, full suite green.

- [ ] **Step 9: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalRunQueue.cs src/OrchestAI.Domain/Models/EvalRunQueueItem.cs \
  src/OrchestAI.Infrastructure/Eval/InMemoryEvalRunQueue.cs \
  src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs \
  src/OrchestAI.Application/Commands/RequestPostHocScoring/RequestPostHocScoringHandler.cs \
  src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs \
  tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerTests.cs \
  tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerPostHocTests.cs \
  tests/OrchestAI.Tests/Integration/CrossTenantBackgroundFlowIntegrationTests.cs
git commit -m "feat: propagate TenantId through the eval run queue and restore it explicitly in the background worker"
```

---

### Task 12: Cost rollup — the one deliberate, narrow, audited system-data-access path

**The design tension this task resolves:** `CostRollupBackgroundService` aggregates `CostLedger` across **every** tenant in one pass (one row per `(Date, TenantId, UserId, AgentType, Model)`), and `CostRollup` itself needs `TenantId` (Task 2 already added it as `ITenantScoped`) so tenant-facing dashboard reads are correctly isolated. But the *write* side is the opposite of every other entity in this system: every other `ITenantScoped` write happens inside a single tenant's ambient scope, and the interceptor correctly auto-stamps that one tenant onto every row. The rollup job legitimately writes **many different tenants' rows in one batch**, and it already knows which tenant each row belongs to (from an authoritative join over `OrchestrationTask.TenantId`) — the interceptor's normal "auto-stamp from the one ambient tenant, reject anything else" rule would either throw (no ambient tenant) or actively corrupt the data (force every row to one wrong tenant) if applied here unchanged. Confirmation #5b's answer: give this **one** job an explicit, narrow, auditable bypass — not a general escape hatch.

**Files:**
- Modify: `src/OrchestAI.Domain/Interfaces/ICurrentTenantAccessor.cs` (add `IsSystemWriteScope` + `BeginSystemWriteScope()`)
- Modify: `src/OrchestAI.Infrastructure/Tenancy/AsyncLocalCurrentTenantAccessor.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/Interceptors/TenantScopingInterceptor.cs`
- Modify: `src/OrchestAI.Domain/Entities/CostRollup.cs` (`Create(...)` gains an explicit `tenantId` parameter — the one deliberate exception to "never in `Create(...)`," justified below)
- Modify: `src/OrchestAI.Domain/Models/CostLedgerAggregate.cs` (add `TenantId`)
- Modify: `src/OrchestAI.Domain/Interfaces/ICostLedgerRepository.cs` and `src/OrchestAI.Infrastructure/Repositories/CostLedgerRepository.cs` (add `GetDailyAggregatesForRollupAsync`, the cross-tenant variant)
- Modify: `src/OrchestAI.Infrastructure/Observability/CostRollupBackgroundService.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/SystemWriteScopeTests.cs`
- Test: Modify `tests/OrchestAI.Tests/Infrastructure/CostRollupBackgroundServiceTests.cs`

**Why `CostRollup.Create(...)` takes a `tenantId` parameter:** every `ITenantScoped` entity created by request-driven application code has the ONLY trustworthy source of `TenantId` be the ambient `ICurrentTenantAccessor` — accepting it as a parameter would open exactly the "client-supplied TenantId" attack surface Task 5 closes by design. `CostRollup` is different: it is created exclusively by `CostRollupBackgroundService`, a trusted system process that derives each row's `TenantId` from an authoritative SQL join (`OrchestrationTask.TenantId`), never from anything a caller supplies. Accepting it as a constructor parameter here is not a design regression — it's the correct shape for the one entity whose writer legitimately varies tenant per-row within a single operation. This is one of exactly two named exceptions to the Global Constraints' "no factory ever takes TenantId" rule — the other is `ApiKey.Create(tenantId, ...)` (Task 1), which shares the same justification shape: a trusted, non-tenant-authenticated writer (the admin-secret-gated `CreateApiKeyHandler`, Task 8) operating with no ambient tenant scope to bypass, where `TenantId` is legitimately an input the caller is choosing rather than a value that should be inferred from request context.

- [ ] **Step 1: Write the failing tests**

Create `tests/OrchestAI.Tests/Infrastructure/SystemWriteScopeTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class SystemWriteScopeTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName, AsyncLocalCurrentTenantAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return new PooledDbContextFactory<AppDbContext>(options, accessor);
    }

    [Fact]
    public void IsSystemWriteScope_DefaultsToFalse()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();

        accessor.IsSystemWriteScope.Should().BeFalse();
    }

    [Fact]
    public void BeginSystemWriteScope_SetsFlagAndRestoresOnDispose()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();

        using (accessor.BeginSystemWriteScope())
        {
            accessor.IsSystemWriteScope.Should().BeTrue();
        }

        accessor.IsSystemWriteScope.Should().BeFalse();
    }

    [Fact]
    public async Task SaveChanges_InSystemWriteScope_AllowsMultipleDistinctTenantIdsInOneBatch()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = BuildFactory(Guid.NewGuid().ToString(), accessor);
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using (accessor.BeginSystemWriteScope())
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var rollupA = CostRollup.Create(DateOnly.FromDateTime(DateTime.UtcNow), tenantAId, Guid.NewGuid(), AgentType.Research, "model", 10, 5, 0.01m, 1);
            var rollupB = CostRollup.Create(DateOnly.FromDateTime(DateTime.UtcNow), tenantBId, Guid.NewGuid(), AgentType.Research, "model", 20, 10, 0.02m, 1);
            ctx.CostRollups.AddRange(rollupA, rollupB);

            var act = async () => await ctx.SaveChangesAsync();

            await act.Should().NotThrowAsync("the system-write scope must allow a single batch to persist rows for multiple different tenants");
        }
    }

    [Fact]
    public async Task SaveChanges_OutsideSystemWriteScope_StillEnforcesNormalTenantRulesForCostRollup()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = BuildFactory(Guid.NewGuid().ToString(), accessor);

        // Deliberately no system-write scope and no tenant scope — CostRollup is still
        // ITenantScoped, so ordinary (non-system) code must not be able to bypass enforcement
        // just because CostRollup happens to also support the system path.
        await using var ctx = await factory.CreateDbContextAsync();
        var rollup = CostRollup.Create(DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), Guid.NewGuid(), AgentType.Research, "model", 10, 5, 0.01m, 1);
        ctx.CostRollups.Add(rollup);

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<OrchestAI.Application.Exceptions.TenantContextViolationException>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~SystemWriteScopeTests"`
Expected: FAIL — `IsSystemWriteScope`/`BeginSystemWriteScope` don't exist yet, and `CostRollup.Create` doesn't take a `tenantId` parameter yet (compile errors).

- [ ] **Step 3: Extend `ICurrentTenantAccessor` and its implementation**

Add to `src/OrchestAI.Domain/Interfaces/ICurrentTenantAccessor.cs`:

```csharp
    // True only inside CostRollupBackgroundService's per-tick operation (Task 12) — the ONE
    // audited, narrow exception where TenantScopingInterceptor skips its normal auto-stamp/reject
    // enforcement, because this job legitimately writes many different tenants' rows in one
    // batch. See ADR-014 confirmation #5b. Grep for BeginSystemWriteScope call sites to audit
    // this remains the only caller.
    bool IsSystemWriteScope { get; }
    IDisposable BeginSystemWriteScope();
```

Add to `src/OrchestAI.Infrastructure/Tenancy/AsyncLocalCurrentTenantAccessor.cs` (a second, independent `AsyncLocal<bool>` alongside the existing tenant-id one):

```csharp
    private static readonly AsyncLocal<bool> SystemWriteScopeFlag = new();

    public bool IsSystemWriteScope => SystemWriteScopeFlag.Value;

    public IDisposable BeginSystemWriteScope()
    {
        var previous = SystemWriteScopeFlag.Value;
        SystemWriteScopeFlag.Value = true;
        return new RestoreSystemWriteScope(previous);
    }

    private sealed class RestoreSystemWriteScope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public RestoreSystemWriteScope(bool previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SystemWriteScopeFlag.Value = _previous;
        }
    }
}
```

- [ ] **Step 4: Update `TenantScopingInterceptor`**

Add this check as the very first line of `EnforceTenantScoping`, before the existing `foreach`:

```csharp
        if (_tenantAccessor.IsSystemWriteScope) return;
```

- [ ] **Step 5: Update `CostRollup.Create`**

Read `src/OrchestAI.Domain/Entities/CostRollup.cs` first (already fully read during this plan's investigation phase — see the Investigation summary). Add a `tenantId` parameter as the second positional argument (right after `date`, before `userId`):

```csharp
    public static CostRollup Create(
        DateOnly date, Guid tenantId, Guid userId, AgentType agentType, string model,
        int inputTokens, int outputTokens, decimal costUsd, int executionCount)
    {
        return new CostRollup
        {
            Id = Guid.NewGuid(),
            Date = date,
            TenantId = tenantId,
            UserId = userId,
            AgentType = agentType,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd,
            ExecutionCount = executionCount,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
```

This changes the existing call site in `CostRollupBackgroundService` (Step 8 below) and every existing test constructing a `CostRollup` — fix each one to pass the correct tenant ID (search the codebase for `CostRollup.Create(` and update every call site; do not leave any non-compiling).

- [ ] **Step 6: Run to verify the new tests pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~SystemWriteScopeTests"`
Expected: PASS, all 4 tests green.

(The full suite will not build yet — `CostRollupBackgroundService` and its tests still call the old 8-parameter `CostRollup.Create` — fix those in Steps 7-9 below before running the full suite.)

- [ ] **Step 7: Add `TenantId` to `CostLedgerAggregate` and the new cross-tenant repository method**

Read `src/OrchestAI.Domain/Models/CostLedgerAggregate.cs` and add a `TenantId` property (or, if it's a positional record, add `Guid TenantId` as a new parameter) alongside its existing `UserId`/`Date`/`AgentType`/`Model`/token/cost fields — match whatever shape the file already has.

Read `src/OrchestAI.Domain/Interfaces/ICostLedgerRepository.cs` and `src/OrchestAI.Infrastructure/Repositories/CostLedgerRepository.cs` in full. Add a new method, distinct from the existing `GetDailyAggregatesAsync` (which stays tenant-filtered as-is for the live dashboard's blended "today" query — no change to its behavior, since the ambient-tenant query filter already scopes it correctly when called from tenant-authenticated request code):

```csharp
    // Cross-tenant by design — the ONLY caller is CostRollupBackgroundService, inside its
    // BeginSystemWriteScope(). Bypasses the tenant query filter deliberately via
    // IgnoreQueryFilters() and asserts it's only ever invoked from within that scope (defense in
    // depth — a future accidental call from tenant-facing code fails loudly instead of silently
    // leaking cross-tenant data). See ADR-014 confirmation #5b.
    Task<IReadOnlyList<CostLedgerAggregate>> GetDailyAggregatesForRollupAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
```

Implement it in `CostLedgerRepository` by copying the existing `GetDailyAggregatesAsync` query's join/grouping logic, adding `.IgnoreQueryFilters()` on the `CostLedger`/`OrchestrationTask` query root(s), adding `TenantId` (from `OrchestrationTask.TenantId`) to both the `GroupBy` key and the projected `CostLedgerAggregate`, and guarding entry with:

```csharp
    public async Task<IReadOnlyList<CostLedgerAggregate>> GetDailyAggregatesForRollupAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!_tenantAccessor.IsSystemWriteScope)
            throw new InvalidOperationException(
                "GetDailyAggregatesForRollupAsync must only be called from within a system-write scope (CostRollupBackgroundService).");

        // ... rest of the method: same join/grouping shape as GetDailyAggregatesAsync, but
        // querying with .IgnoreQueryFilters() and grouping by (Date, TenantId, UserId, AgentType, Model)
        // instead of (Date, UserId, AgentType, Model).
    }
```

This requires injecting `ICurrentTenantAccessor` into `CostLedgerRepository`'s constructor (it currently only takes `IDbContextFactory<AppDbContext>` — add the new dependency alongside it).

- [ ] **Step 8: Update `CostRollupBackgroundService`**

Read the file in full. Wrap the entire per-tick aggregation-and-write operation (`RunOnceAsync`, or whatever its current per-tick method is called) in the system-write scope, and switch it to call the new rollup-specific aggregate method:

```csharp
        using var systemScope = _tenantAccessor.BeginSystemWriteScope();

        var aggregates = await _costLedgerRepository.GetDailyAggregatesForRollupAsync(from, today, cancellationToken).ConfigureAwait(false);

        foreach (var aggregate in aggregates)
        {
            // ... existing per-aggregate CostRollup.Create/ReplaceValues logic, unchanged except
            // CostRollup.Create(...) now also takes aggregate.TenantId as its second argument.
        }
```

This requires injecting `ICurrentTenantAccessor` into `CostRollupBackgroundService`'s constructor alongside its existing dependencies.

- [ ] **Step 9: Fix existing `CostRollup.Create` call sites and tests**

Search the codebase for every remaining `CostRollup.Create(` call (in `CostRollupBackgroundService.cs` itself, already covered by Step 8, and in `tests/OrchestAI.Tests/Infrastructure/CostRollupBackgroundServiceTests.cs` and `CostLedgerRepositoryEvalFilterTests.cs` if it constructs one directly) and add a tenant ID argument to each. In `CostRollupBackgroundServiceTests.cs`, also wrap any direct `CostRollup`/`CostLedger` seeding or `RunOnceAsync` invocation in `using (accessor.BeginSystemWriteScope())` where needed to keep the interceptor from rejecting the test's own setup — read the file first and adapt precisely rather than guessing its exact current mock/seed structure.

- [ ] **Step 10: Run the full suite**

Run: `dotnet build OrchestAI.sln`
Expected: 0 errors.

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~SystemWriteScopeTests|FullyQualifiedName~CostRollupBackgroundServiceTests"`
Expected: PASS.

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, full suite green.

- [ ] **Step 11: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/ICurrentTenantAccessor.cs \
  src/OrchestAI.Infrastructure/Tenancy/AsyncLocalCurrentTenantAccessor.cs \
  src/OrchestAI.Infrastructure/Data/Interceptors/TenantScopingInterceptor.cs \
  src/OrchestAI.Domain/Entities/CostRollup.cs src/OrchestAI.Domain/Models/CostLedgerAggregate.cs \
  src/OrchestAI.Domain/Interfaces/ICostLedgerRepository.cs src/OrchestAI.Infrastructure/Repositories/CostLedgerRepository.cs \
  src/OrchestAI.Infrastructure/Observability/CostRollupBackgroundService.cs \
  tests/OrchestAI.Tests/Infrastructure/SystemWriteScopeTests.cs \
  tests/OrchestAI.Tests/Infrastructure/CostRollupBackgroundServiceTests.cs
git commit -m "feat: add narrow system-write-scope bypass for the cross-tenant cost rollup job"
```

---

### Task 13: Completeness tests — every `ITenantScoped` type is filtered, and nothing was forgotten

**Files:**
- Test: Create `tests/OrchestAI.Tests/Architecture/TenantScopingCompletenessTests.cs`

**Interfaces:**
- Consumes: `ITenantScoped` (Task 1), `AppDbContext` (Task 4), every entity in `OrchestAI.Domain.Entities`.

**Why two separate checks, not one:** a single "does every `ITenantScoped` type have a filter" check has a blind spot — it can only see types that *already* implement the interface. It cannot catch a *future* entity that holds tenant data but forgot to implement `ITenantScoped` in the first place. The second check (a hand-maintained classification of every entity in the assembly as either tenant-scoped or globally-shared) is what catches that — it fails loudly the moment someone adds a new entity without deciding, and recording, which category it belongs to.

- [ ] **Step 1: Write the tests**

Create `tests/OrchestAI.Tests/Architecture/TenantScopingCompletenessTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Architecture;

// Two complementary checks (see ADR-014): (1) every entity that DOES implement ITenantScoped
// has an active query filter — proves Task 4's generic reflection-based wiring actually took
// effect. (2) A hand-maintained classification of every entity in the assembly as tenant-scoped
// or globally-shared — catches a FUTURE entity that holds tenant data but forgot to implement
// the interface, which check (1) alone cannot see (it only sees what already opted in).
public sealed class TenantScopingCompletenessTests
{
    // Update this list deliberately whenever a new tenant-owned entity is added — that update IS
    // the point of this test existing.
    private static readonly Type[] ExpectedTenantScopedTypes =
    [
        typeof(OrchestrationTask), typeof(AgentExecution), typeof(AgentMemory), typeof(AgentMessage),
        typeof(AgentRetryAttempt), typeof(CostLedger), typeof(CostRollup), typeof(McpToolCall),
        typeof(TaskCheckpoint), typeof(EvalSuite), typeof(EvalCase), typeof(EvalRun), typeof(EvalResult)
    ];

    // Deliberately NOT tenant-scoped — global/shared data (see ADR-014). Listed explicitly so a
    // reviewer of this test sees the full picture, not just the positive list.
    private static readonly Type[] ExpectedGloballySharedTypes =
    [
        typeof(User), typeof(Tenant), typeof(ApiKey), typeof(ModelPricing)
    ];

    [Fact]
    public void EveryExpectedType_ImplementsITenantScoped()
    {
        foreach (var type in ExpectedTenantScopedTypes)
        {
            typeof(ITenantScoped).IsAssignableFrom(type).Should().BeTrue(
                $"{type.Name} is expected to be tenant-scoped per the hand-maintained list in this test");
        }
    }

    [Fact]
    public void EveryEntityInTheDomainAssembly_IsAccountedForAsEitherTenantScopedOrGloballyShared()
    {
        var allEntityTypes = typeof(OrchestrationTask).Assembly.GetTypes()
            .Where(t => t.Namespace == "OrchestAI.Domain.Entities" && t.IsClass && !t.IsAbstract)
            .ToList();

        var accountedFor = ExpectedTenantScopedTypes.Concat(ExpectedGloballySharedTypes).ToHashSet();
        var unaccounted = allEntityTypes.Where(t => !accountedFor.Contains(t)).ToList();

        unaccounted.Should().BeEmpty(
            "every entity in OrchestAI.Domain.Entities must be explicitly classified as tenant-scoped or " +
            $"globally-shared in this test — found unclassified: {string.Join(", ", unaccounted.Select(t => t.Name))}");
    }

    [Fact]
    public void EveryITenantScopedEntity_HasAnActiveQueryFilterOnTheRealModel()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var context = new AppDbContext(options, accessor);

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType)) continue;

            entityType.GetQueryFilter().Should().NotBeNull(
                $"{entityType.ClrType.Name} implements ITenantScoped and must have an active query filter (Task 4's generic wiring)");
        }
    }
}
```

- [ ] **Step 2: Run to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantScopingCompletenessTests"`
Expected: PASS, all 3 tests green. If `EveryEntityInTheDomainAssembly_...` fails, it means a 14th or 18th entity exists in the assembly that this plan didn't account for — go back and verify against a fresh `ls src/OrchestAI.Domain/Entities/` rather than assuming the count in this plan is exhaustive; add it to whichever list is correct and re-run.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, full suite green.

- [ ] **Step 4: Commit**

```bash
git add tests/OrchestAI.Tests/Architecture/TenantScopingCompletenessTests.cs
git commit -m "test: add completeness guardrails proving no ITenantScoped entity or filter was forgotten"
```

---

### Task 14: Raw/exceptional data-access-path audit

**Files:**
- Test: Create `tests/OrchestAI.Tests/Infrastructure/TenantFilterNavigationJoinTests.cs`

**What this task checks, and how each is verified:**

1. **`IgnoreQueryFilters()` usage** — must be exactly one call site after this plan is fully implemented (the one added in Task 12). Run: `grep -rn "IgnoreQueryFilters" src/` and confirm the only match is inside `GetDailyAggregatesForRollupAsync` in `CostLedgerRepository.cs`. Record this exact grep output in your task report — if there is a second hit anywhere, that is an undocumented bypass and must be justified (added to ADR-014) or removed before this task is considered done.
2. **`ExecuteUpdate`/`ExecuteDelete` usage** — run: `grep -rn "ExecuteUpdate\|ExecuteDelete" src/`. Confirmed zero hits anywhere in this codebase as of the start of Week 10 (verified during planning) and this plan introduces none — confirm the grep is still empty after all prior tasks are implemented.
3. **Raw SQL (`ExecuteSqlRawAsync`/`FromSqlRaw`) usage** — run: `grep -rn "ExecuteSqlRaw\|FromSqlRaw" src/`. Confirmed the only pre-existing hits are in `DatabaseSeeder.cs` (seeding `Users`/`ModelPricing`, neither `ITenantScoped`) and Task 6 added raw SQL only inside the `AddTenantIsolation` migration's `Up()` (migrations are schema/data setup, not application-request code paths, and are not subject to the tenant filter question at all — they run once, outside any request or worker context). Confirm no NEW raw-SQL writer was introduced anywhere in `src/OrchestAI.*` application code (Application/Infrastructure/API layers) by this plan's other tasks.
4. **Fresh-`DbContext`-per-repository-call pattern is safe** — already proven structurally: the tenant query filter is registered once in `AppDbContext.OnModelCreating` (Task 4), which EF Core evaluates for the *model* (shared across every instance of that `DbContext` type), not per-instance — so every repository's `IDbContextFactory<AppDbContext>.CreateDbContextAsync()` call gets a freshly-constructed context that still carries the identical filter. This has already been implicitly proven by every passing test in Tasks 4, 5, 6, 11 (all of which use exactly this fresh-context-per-call pattern and correctly observe tenant isolation) — no new test needed here, just this explicit note in your task report connecting the dots.
5. **Navigation-join queries respect the filter** — this is the one genuinely new thing to test in this task (the others are audits/greps + reasoning about already-passing tests).

- [ ] **Step 1: Write the failing navigation-join test**

Create `tests/OrchestAI.Tests/Infrastructure/TenantFilterNavigationJoinTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Proves the tenant query filter is respected even when an entity is reached via a navigation
// property (Include/ThenInclude) from another tenant-scoped entity, not just when queried
// directly as the root of a LINQ query — EF Core applies each entity's own filter to it
// regardless of how it's reached, as long as both ends are correctly tenant-scoped and
// consistent (which TenantScopingInterceptor guarantees at write time).
public sealed class TenantFilterNavigationJoinTests
{
    private static (PooledDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return (new PooledDbContextFactory<AppDbContext>(options, accessor), accessor);
    }

    [Fact]
    public async Task IncludeAgentExecutions_ForATenantATask_NeverPullsInTenantBsExecutions()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("nav-join@test.local");

        Guid taskAId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "Tenant A task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
            taskAId = task.Id;
        }

        // A row that (if the filter were somehow bypassed on the navigation side) could
        // incorrectly appear via a join if AgentExecution.OrchestrationTaskId were reused —
        // seeded under tenant B, on ITS OWN task, to confirm cross-tenant isolation holds even
        // when both sides of a relationship are populated.
        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "Tenant B task", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            ctx.AgentExecutions.Add(execution);
            await ctx.SaveChangesAsync();
        }

        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var tasksWithExecutions = await ctx.OrchestrationTasks
                .Include(t => t.AgentExecutions)
                .ToListAsync();

            tasksWithExecutions.Should().ContainSingle(t => t.Id == taskAId);
            tasksWithExecutions.SelectMany(t => t.AgentExecutions).Should()
                .OnlyContain(e => e.TenantId == tenantAId, "Include()'d navigation rows must be filtered exactly like a direct query would be");
        }
    }
}
```

- [ ] **Step 2: Run to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantFilterNavigationJoinTests"`
Expected: PASS. (This should pass on the first run given Task 4's already-correct filter wiring — if it fails, that is a real, serious gap in the filter design worth stopping to investigate before continuing, not something to work around.)

- [ ] **Step 3: Run the greps and record the results**

Run each of these and record the exact output in your task report:

```bash
grep -rn "IgnoreQueryFilters" src/
grep -rn "ExecuteUpdate\|ExecuteDelete" src/
grep -rn "ExecuteSqlRaw\|FromSqlRaw" src/
```

Confirm: exactly one `IgnoreQueryFilters` hit (in `CostLedgerRepository.GetDailyAggregatesForRollupAsync`), zero `ExecuteUpdate`/`ExecuteDelete` hits, and the only `ExecuteSqlRaw`/`FromSqlRaw` hits are in `DatabaseSeeder.cs` and the `AddTenantIsolation` migration file. If anything else turns up, stop and resolve it (either remove the unaudited bypass, or add it to ADR-014's confirmation #3/#5b documentation with the same rigor as the cost rollup path) before proceeding to Task 15.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, full suite green.

- [ ] **Step 5: Commit**

```bash
git add tests/OrchestAI.Tests/Infrastructure/TenantFilterNavigationJoinTests.cs
git commit -m "test: prove tenant filter holds across navigation-property joins; audit raw-SQL/filter-bypass surface"
```

---

### Task 15: Cross-tenant integration test suite — the broad sweep across multiple entity types

**What's already proven by earlier tasks (do not re-test — cross-reference in your report instead of duplicating):**
- Fail-closed reads (empty results with no ambient tenant) — `TenantQueryFilterTests` (Task 4).
- Fail-closed writes (throws with no ambient tenant) and rejection of a mismatched explicit `TenantId` — `TenantScopingInterceptorTests` (Task 5).
- Suspension rejected at the auth layer (401/403 contract) — `TenantAuthenticationMiddlewareTests` (Task 9).
- Cannot select a foreign tenant's `EvalRun` as a baseline; foreign IDs in an explicit post-hoc trace list are silently excluded; a foreign `OrchestrationTask` ID 404s — Task 10.
- Full background-flow propagation (enqueue as tenant A, process outside any HTTP scope, tenant B can't see results) — Task 11.
- Navigation-join (`Include`) isolation — Task 14.

**What this task adds — a broader sweep proving the SAME mechanism generalizes across every entity type, not just the two or three already spot-checked above, plus the "update/delete by guessed ID" angle that hasn't been directly tested yet.**

**Files:**
- Test: Create `tests/OrchestAI.Tests/Integration/CrossTenantIsolationSweepTests.cs`

- [ ] **Step 1: Write the sweep tests**

Create `tests/OrchestAI.Tests/Integration/CrossTenantIsolationSweepTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Integration;

// A broader sweep across multiple entity types proving the SAME generic filter mechanism
// (Task 4) generalizes, rather than re-spot-checking the one or two entities earlier tasks
// already exercised. Also covers "update/delete by a guessed foreign ID," which no earlier task
// tested directly.
public sealed class CrossTenantIsolationSweepTests
{
    private static (PooledDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return (new PooledDbContextFactory<AppDbContext>(options, accessor), accessor);
    }

    private sealed record SeededData(
        Guid TenantAId, Guid TenantBId,
        Guid TenantAExecutionId, Guid TenantBExecutionId,
        Guid TenantASuiteId, Guid TenantBSuiteId);

    private static async Task<SeededData> SeedTwoFullTenants(PooledDbContextFactory<AppDbContext> factory, AsyncLocalCurrentTenantAccessor accessor)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("sweep@test.local");

        Guid executionAId, suiteAId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            ctx.Users.Add(user);
            var task = OrchestrationTask.Create(user.Id, "A", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            execution.Start();
            execution.Complete("output", 10, 5, 0.01m);
            ctx.AgentExecutions.Add(execution);
            ctx.CostLedger.Add(CostLedger.Create(task.Id, "model", 10, 5, 0.01m, execution.Id));
            var suite = EvalSuite.Create("Suite A", "desc", AgentType.Research);
            ctx.EvalSuites.Add(suite);
            await ctx.SaveChangesAsync();
            executionAId = execution.Id;
            suiteAId = suite.Id;
        }

        Guid executionBId, suiteBId;
        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "B", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            execution.Start();
            execution.Complete("output", 10, 5, 0.01m);
            ctx.AgentExecutions.Add(execution);
            ctx.CostLedger.Add(CostLedger.Create(task.Id, "model", 20, 10, 0.02m, execution.Id));
            var suite = EvalSuite.Create("Suite B", "desc", AgentType.Research);
            ctx.EvalSuites.Add(suite);
            await ctx.SaveChangesAsync();
            executionBId = execution.Id;
            suiteBId = suite.Id;
        }

        return new SeededData(tenantAId, tenantBId, executionAId, executionBId, suiteAId, suiteBId);
    }

    [Fact]
    public async Task ReadIsolation_HoldsAcrossAgentExecutions_CostLedger_AndEvalSuites_Simultaneously()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var seed = await SeedTwoFullTenants(factory, accessor);

        using (accessor.SetTenant(seed.TenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();

            (await ctx.AgentExecutions.ToListAsync()).Should().ContainSingle(e => e.Id == seed.TenantAExecutionId);
            (await ctx.CostLedger.ToListAsync()).Should().OnlyContain(c => c.TenantId == seed.TenantAId);
            (await ctx.EvalSuites.ToListAsync()).Should().ContainSingle(s => s.Id == seed.TenantASuiteId);
        }
    }

    [Fact]
    public async Task UpdateByGuessedForeignId_NoRowsAffected_BecauseTheRowIsInvisible()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var seed = await SeedTwoFullTenants(factory, accessor);

        using (accessor.SetTenant(seed.TenantAId))
        {
            // Tenant A attempts to fetch-then-update tenant B's AgentExecution by its (guessed
            // or otherwise obtained) real ID — the standard repository pattern in this codebase
            // is always fetch-first, so the filtered fetch must return null before any update
            // is even attempted.
            await using var ctx = await factory.CreateDbContextAsync();
            var found = await ctx.AgentExecutions.FirstOrDefaultAsync(e => e.Id == seed.TenantBExecutionId);

            found.Should().BeNull("tenant A must never be able to even locate tenant B's row to update it");
        }

        // Confirm tenant B's row is genuinely untouched.
        using (accessor.SetTenant(seed.TenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var stillThere = await ctx.AgentExecutions.SingleAsync(e => e.Id == seed.TenantBExecutionId);
            stillThere.Status.Should().Be(ExecutionStatus.Completed);
        }
    }

    [Fact]
    public async Task DeleteByGuessedForeignId_NoRowsAffected_BecauseTheRowIsInvisible()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var seed = await SeedTwoFullTenants(factory, accessor);

        using (accessor.SetTenant(seed.TenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var found = await ctx.EvalSuites.FirstOrDefaultAsync(s => s.Id == seed.TenantBSuiteId);

            found.Should().BeNull("tenant A must never be able to locate tenant B's suite to delete it");
        }

        using (accessor.SetTenant(seed.TenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            (await ctx.EvalSuites.AnyAsync(s => s.Id == seed.TenantBSuiteId)).Should().BeTrue(
                "tenant B's suite must be completely untouched by tenant A's attempt");
        }
    }
}
```

- [ ] **Step 2: Run to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~CrossTenantIsolationSweepTests"`
Expected: PASS, all 3 tests green.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, full suite green.

- [ ] **Step 4: Commit**

```bash
git add tests/OrchestAI.Tests/Integration/CrossTenantIsolationSweepTests.cs
git commit -m "test: add broad cross-tenant isolation sweep across multiple entity types"
```

---

### Task 16: Frontend — temporary in-memory API key auth

**Files:**
- Create: `frontend/src/apiKey.js`
- Create: `frontend/src/ApiKeyPrompt.jsx`
- Modify: `frontend/src/App.jsx` (gate rendering behind `hasApiKey()`; replace every `fetch(` call with `authenticatedFetch(`)
- Modify: `frontend/src/EvalsPage.jsx` (replace every `fetch(` call with `authenticatedFetch(`)
- Modify: `frontend/src/ObservabilityPage.jsx` (replace every `fetch(` call with `authenticatedFetch(`)

**This is explicitly a temporary, non-production flow (see ADR-014 confirmation #10):** in-memory-only storage avoids *persistence*-based exposure (nothing survives a refresh, nothing is written to disk/localStorage/the build), but it does **not** avoid *runtime* exposure — any JavaScript-accessible value, including one held only in a module-level variable, is still readable by an XSS vulnerability during an active session. Document this honestly; do not imply in-memory storage is a complete solution.

- [ ] **Step 1: Create the in-memory key module**

```javascript
// frontend/src/apiKey.js
//
// In-memory only — never persisted to localStorage/sessionStorage, never baked into the build,
// never sent to any URL as a query parameter, never logged. This is a temporary
// development/testing auth flow, not a production design: any JavaScript-accessible value, even
// one held only in memory, is still readable by an XSS vulnerability during an active session.
// A production design needs a backend-for-frontend session, short-lived tokens, or httpOnly
// cookies — not a long-lived machine API key living in browser JS at all. See ADR-014
// confirmation #10.

let currentApiKey = null

export function setApiKey(key) {
  currentApiKey = key || null
}

export function getApiKey() {
  return currentApiKey
}

export function hasApiKey() {
  return currentApiKey !== null && currentApiKey !== ''
}

export function clearApiKey() {
  currentApiKey = null
}

// Drop-in replacement for fetch() that injects the Authorization header when a key is set.
// Never appends the key to the URL/query string.
export async function authenticatedFetch(url, options = {}) {
  const headers = { ...(options.headers || {}) }
  if (currentApiKey) {
    headers['Authorization'] = `Bearer ${currentApiKey}`
  }
  return fetch(url, { ...options, headers })
}
```

- [ ] **Step 2: Create the prompt component**

```javascript
// frontend/src/ApiKeyPrompt.jsx
import { useState } from 'react'
import { setApiKey } from './apiKey'

export default function ApiKeyPrompt({ onSubmitted }) {
  const [value, setValue] = useState('')

  const submit = () => {
    if (!value.trim()) return
    setApiKey(value.trim())
    onSubmitted()
  }

  return (
    <div style={{ padding: 40, maxWidth: 480, margin: '80px auto', fontFamily: 'sans-serif' }}>
      <h2 style={{ color: '#cdd6f4' }}>OrchestAI — API Key Required</h2>
      <p style={{ color: '#6c7086', fontSize: 13 }}>
        Enter your API key. It is held in memory for this browser session only — never saved to
        disk, never sent anywhere except as an Authorization header on requests to this API.
        Refreshing the page will require re-entering it. This is a temporary development flow,
        not a production authentication design.
      </p>
      <input
        type="password"
        value={value}
        onChange={e => setValue(e.target.value)}
        onKeyDown={e => e.key === 'Enter' && submit()}
        placeholder="orch_live_..."
        autoComplete="off"
        style={{
          width: '100%', padding: '10px 12px', fontSize: 14, borderRadius: 6,
          border: '1px solid #313244', background: '#181825', color: '#cdd6f4',
        }}
      />
      <button
        onClick={submit}
        style={{
          marginTop: 12, padding: '8px 16px', borderRadius: 6, border: 'none',
          background: '#89b4fa', color: '#11111b', fontWeight: 700, cursor: 'pointer',
        }}
      >
        Continue
      </button>
    </div>
  )
}
```

- [ ] **Step 3: Gate `App.jsx` behind the key prompt, and replace every `fetch(` call across all three files**

Read `frontend/src/App.jsx` in full first. Add `import { hasApiKey } from './apiKey'` and `import ApiKeyPrompt from './ApiKeyPrompt'`, add a `const [keySet, setKeySet] = useState(hasApiKey())` state near the component's other `useState` declarations, and wrap the component's existing top-level return value so that when `!keySet`, it renders `<ApiKeyPrompt onSubmitted={() => setKeySet(true)} />` instead of the normal app content — do not restructure anything else about the component's existing logic.

In all three files (`App.jsx`, `EvalsPage.jsx`, `ObservabilityPage.jsx`), add `import { authenticatedFetch } from './apiKey'` and replace every bare `fetch(` call with `authenticatedFetch(` — this is a pure rename with an identical call signature (`authenticatedFetch` forwards its arguments to `fetch` unchanged, just injecting the header), so no other part of any existing `.then(...)`/`.catch(...)` chain needs to change. Grep each file for `fetch(` after editing to confirm none were missed:

```bash
grep -n "[^d]fetch(" frontend/src/App.jsx frontend/src/EvalsPage.jsx frontend/src/ObservabilityPage.jsx
```

(the `[^d]` prefix excludes `authenticatedFetch(` matches themselves — every remaining hit is one you missed).

- [ ] **Step 4: Manually verify in the browser**

This codebase has no frontend automated test suite (confirmed during Week 9) — verification here is manual, matching this project's existing convention for frontend changes. Run `cd frontend && npm run dev`, open the app: confirm the API-key prompt appears before any content, confirm entering a key (any string — the backend call will 401 if it's wrong, but the point here is that the prompt gates rendering and the header gets attached) makes the app proceed to its normal views, and use the browser's Network tab to confirm `Authorization: Bearer ...` is present on requests and the key never appears in the URL. Refresh the page and confirm the prompt reappears (proving nothing persisted).

- [ ] **Step 5: Run backend tests to confirm nothing else broke**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS — this is a frontend-only change; the full backend suite must remain exactly as it was after Task 15.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/apiKey.js frontend/src/ApiKeyPrompt.jsx frontend/src/App.jsx \
  frontend/src/EvalsPage.jsx frontend/src/ObservabilityPage.jsx
git commit -m "feat: add temporary in-memory API key auth flow to the frontend"
```

---

### Task 17: Documentation — ADR-014, `OBSERVABILITY.md` update, `DESIGN_PRINCIPLES.md`

**Files:**
- Modify: `DECISIONS.md` (append ADR-014)
- Modify: `OBSERVABILITY.md`
- Create: `DESIGN_PRINCIPLES.md`

- [ ] **Step 1: Append ADR-014 to `DECISIONS.md`**

Add after ADR-013's final line:

```markdown

## ADR-014: Tenant Identity and Isolation

**Status:** Accepted

### Investigation — what already existed vs. what's net-new
Confirmed by reading every entity in `src/OrchestAI.Domain/Entities/` in full: 13 entities
already carried an ownership chain back to a `User` (directly or transitively via
`OrchestrationTask`/`AgentExecution`), but `EvalSuite`/`EvalCase`/`EvalRun`/`EvalResult` had
**zero** ownership concept — every "user" implicitly shared one global eval-suite space since
Week 8. Zero authentication of any kind existed (`Program.cs` has no `UseAuthentication()`/
`UseAuthorization()`, confirmed by full read); every `UserId` in this codebase was a plain,
unvalidated `Guid` supplied by the caller. `User` itself carries no auth/tenant field —
deliberately left untouched this week; `Tenant` is a new, separate identity layer, not a
retrofit of `User`.

### Confirmation #1 — Tenant definition
One `Tenant` = one external org/customer; `ApiKey` is many-to-one to `Tenant` (revoking a key
never orphans data). `User` stays an internal actor label, orthogonal to `Tenant` — not part of
the auth chain this week. Introducing a `User`-belongs-to-`Tenant` relationship, or any
within-tenant RBAC, is explicitly deferred (non-goal).

### Confirmation #2 — Tenant-scoped tables
13 entities implement a new `ITenantScoped` marker interface: `OrchestrationTask`,
`AgentExecution`, `AgentMemory`, `AgentMessage`, `AgentRetryAttempt`, `CostLedger`, `CostRollup`,
`McpToolCall`, `TaskCheckpoint`, `EvalSuite`, `EvalCase`, `EvalRun`, `EvalResult`. `ModelPricing`
and `User` remain global/shared, `Tenant`/`ApiKey` are the identity layer itself (also not
`ITenantScoped` — there is no "current tenant" to scope a tenant lookup to before one is
resolved). `EvalSuite`/`EvalCase`/`EvalRun`/`EvalResult` becoming tenant-private is a deliberate,
user-visible behavior change from Week 8-9: eval suites are no longer implicitly global.

### Confirmation #3 — Centralized enforcement, reads and writes, plus explicit relationship checks
EF Core global query filters, applied **generically** via reflection over every entity
implementing `ITenantScoped` in `AppDbContext.OnModelCreating` — a future entity that implements
the interface is protected automatically, with no per-entity `HasQueryFilter` call to remember.
Writes are enforced by a new `TenantScopingInterceptor` (mirroring the existing
`UpdatedAtInterceptor` pattern exactly): it stamps `TenantId` on every new `ITenantScoped` entity
from the ambient tenant, and rejects (never silently overwrites) any entity that somehow already
carries a mismatched `TenantId`. No domain `Create(...)` factory accepts a `TenantId` parameter
(the one deliberate exception is `CostRollup`, see Decision 7) — this closes the "client-supplied
`TenantId`" attack surface at the design level, not just at a runtime check.

The filter/interceptor pair is the *default*, not the *only*, mechanism: `RunEvalSuiteCommand`'s
`BaselineRunId` needed an explicit new ownership lookup (the handler never looked the referenced
run up at all before this week), while `ResumeOrchestrationTaskCommand`'s `TaskId` and
`RequestPostHocScoringCommand`'s explicit `TraceIds` turned out to already be correctly handled
by the filter alone (a foreign ID is either invisible — 404 — or silently excluded from a
resolved set, consistent with how these commands already treat "not visible" elsewhere). Each of
these three was verified individually against the actual current handler code, not assumed.

### Confirmation #4 — Fail closed
The filter is `e.TenantId == accessor.TenantId`, comparing a non-nullable `Guid` against a
`Guid?`. With no ambient tenant, `accessor.TenantId` is `null`, and SQL's `x = NULL` is never
`TRUE` — reads return zero rows with no special-casing, and critically, no `||` fallback branch
was written that could flip this fail-open. The interceptor explicitly throws
`TenantContextViolationException` on any write with no resolved tenant. The default/system
tenant (`00000000-0000-0000-0000-000000000001`, created by the `AddTenantIsolation` migration for
backfill only) has zero `ApiKey` rows, ever — there is no code path that could mint one for it,
since key issuance always requires an explicit, already-existing `TenantId` argument.

### Confirmation #5 — Background propagation
`TenantId` travels explicitly with queued work: `EvalRunQueueItem(Guid EvalRunId, Guid TenantId)`
replaces the old bare-`Guid` queue payload. The handlers don't need a new dependency to supply
this — by the time `RunEvalSuiteHandler`/`RequestPostHocScoringHandler` call
`_runRepository.AddAsync(run, ct)`, `TenantScopingInterceptor` has already stamped `run.TenantId`
from the ambient tenant (set by the auth middleware for the whole HTTP request), and because the
interceptor sets it via `entry.Property(...).CurrentValue`, the in-memory entity reflects the
stamped value too — the handler just reads `run.TenantId` straight off the entity.
`EvalRunBackgroundWorker.ExecuteAsync` restores the ambient tenant scope from the dequeued item
**before** `ProcessRunAsync` touches any tenant-scoped repository (its very first call fetches
the `EvalRun` itself, which is `ITenantScoped`), and checks the owning tenant's status
(`Active`/`Suspended`/deleted) before doing any further work — a tenant suspended after enqueue
gets its queued run marked `Failed` with a clear reason, never silently completed.

### Confirmation #5b — Cost rollup: a deliberate, narrow, audited exception
`CostRollupBackgroundService` aggregates across every tenant by nature — it cannot run inside
any single tenant's ambient scope. A new `ICurrentTenantAccessor.BeginSystemWriteScope()` (a
second, independent `AsyncLocal<bool>` flag) is the **only** bypass: while active,
`TenantScopingInterceptor` skips its normal auto-stamp/reject logic entirely, and a new
`ICostLedgerRepository.GetDailyAggregatesForRollupAsync` explicitly calls `IgnoreQueryFilters()`
— guarded by an `InvalidOperationException` if called outside a system-write scope, so an
accidental future call from tenant-facing code fails loudly instead of silently leaking
cross-tenant data. `CostRollups` gained `TenantId` in its grouping key
(`Date, TenantId, UserId, AgentType, Model`) so two tenants' costs are never collapsed into one
aggregate row. Auditable by construction: grep for `IgnoreQueryFilters`/`BeginSystemWriteScope`
and confirm each has exactly the one call site described here (Task 14's audit).

**Two named exceptions to "no factory ever takes `TenantId`," not one:** `CostRollup.Create(...)`
(this task) and `ApiKey.Create(...)` (Task 1) both accept an explicit `tenantId` parameter — the
review that surfaced this during Task 1's implementation initially read as a contradiction of the
Global Constraints until traced to its actual justification (see Task 1's code comment on
`ApiKey.Create` and the Global Constraints entry above). Both share the same shape: a trusted
writer with no ambient ITenantScoped write path to bypass. `CostRollup`'s writer
(`CostRollupBackgroundService`) derives `TenantId` from an authoritative SQL join
(`OrchestrationTask.TenantId`), never from a caller. `ApiKey`'s writer (`CreateApiKeyHandler`,
admin-secret-gated, Task 8) has no ambient tenant scope during the call at all — the operator
explicitly designating the tenant for a new key IS the operation. Neither is reachable from a
tenant-authenticated request path, which is the actual property the constraint protects. Any
*other* factory method accepting `TenantId` is a defect, not a third instance of this pattern —
`TenantScopingCompletenessTests` (Task 13) and this ADR are what a future reviewer checks before
accepting a claimed third exception.

### Confirmation #6 — Ambient tenant mechanism
`ICurrentTenantAccessor`, backed by `AsyncLocal<Guid?>` (plus the separate `AsyncLocal<bool>` for
system-write scope). Chosen over `IHttpContextAccessor`/DI-scope alignment because it must serve
both an HTTP request (set once by auth middleware) and a background-worker job (set explicitly
per dequeued item) identically — `IHttpContextAccessor` doesn't exist in the latter, and
`AppDbContext` instances are created per-call via `IDbContextFactory`, which resolves
constructor dependencies from an internal per-call scope rather than the ambient HTTP request
scope. `AsyncLocal` flows correctly across async continuations within whichever logical call
chain is executing, independent of DI scope boundaries — proven directly by
`AsyncLocalCurrentTenantAccessorTests`'s concurrent-flows test (two simultaneous `Task.Run`
bodies, each setting a different tenant, never observe each other's value).

### Confirmation #7 — API key format
`orch_live_<publicKeyId>.<secret>` — a 12-character random base62 `publicKeyId` (indexed lookup
key) and a 32-character random base62 `secret` (≈190 bits of entropy). Hashed with SHA-256, not
a slow KDF (bcrypt/argon2 exist to resist brute-forcing a *low-entropy human-chosen* password —
this is a long, randomly-generated machine credential, which that threat model doesn't apply
to). Verified via `CryptographicOperations.FixedTimeEquals`, never a raw string comparison, which
can leak timing information proportional to how many leading bytes match. The raw key is
returned to the caller exactly once, at creation, and is never persisted, logged, or retrievable
again — only `HashedSecret` is stored.

### Confirmation #8 — Backfill and provisioning bootstrap
One well-known default/system tenant (`00000000-0000-0000-0000-000000000001`, `Tenant.DefaultTenantId`),
created by the `AddTenantIsolation` migration, with zero `ApiKey` rows ever — structurally
unauthenticatable (confirmation #4), enforced at two independent layers rather than one:
`CreateApiKeyHandler` rejects the ID explicitly at the application layer, and a Postgres `CHECK`
constraint (`CK_ApiKeys_TenantId_NotDefault` on the `ApiKeys` table, Task 6) refuses the row at
the database layer regardless of how the insert was attempted — a raw SQL statement, a future
code path that forgets the handler-level check, or a bug in the handler itself all still fail.
Convention alone ("no code path currently does this") was judged insufficient for the one
invariant fail-closed enforcement itself depends on; a schema constraint doesn't erode as the
codebase changes. `CreateTenantCommand`/`CreateApiKeyCommand`/`RevokeApiKeyCommand` are reachable
only through `AdminController`, gated by `RequireAdminSecretFilter` (a static, separately
configured `Admin:BootstrapSecret`, checked via constant-time comparison, never a tenant API
key) — this is deliberately not the same auth path as Task 9's tenant middleware, since an
ordinary tenant must never be able to create another tenant or mint itself unlimited keys.

### Confirmation #9 — Suspension
Invalid/missing/revoked/expired key → `401`. Valid key, `Suspended` tenant → `403`. Queued work
for a tenant suspended after enqueue is rejected (marked `Failed` with a clear reason) when the
worker checks status, before any further processing — never silently completed. Verified directly
against real `TenantAuthenticationMiddleware`/`EvalRunBackgroundWorker` code, not assumed.

### Confirmation #10 — Frontend authentication transition
Temporary, explicitly non-production: an in-memory-only (module-scoped JS variable, never
`localStorage`/`sessionStorage`, never baked into the build) API-key prompt, gating the app's
render and injecting `Authorization: Bearer <key>` via a thin `authenticatedFetch` wrapper. This
avoids *persistence*-based exposure (nothing survives a refresh or ships in a build artifact) —
it does **not** avoid *runtime* exposure: any JavaScript-accessible value, including one held
only in memory, remains readable by an XSS vulnerability during an active session. A real
production design needs a backend-for-frontend session, short-lived tokens, or httpOnly cookies
— not a long-lived machine credential living in browser JS at all. CORS (`Program.cs`) already
has no `.AllowCredentials()` and explicit allowed origins (unchanged this week) — relevant since
a cookie-based future redesign would need to revisit this.

### Decision 11 — `EvalSuite`/`EvalCase`/`EvalRun`/`EvalResult` becoming tenant-private is a deliberate behavior change
Before this week, every eval suite was implicitly shared across all callers (there was no
isolation concept at all). After this week, each suite belongs to exactly one tenant, and a
different tenant cannot see or run it. This is called out explicitly because it's the one
tenant-scoping decision that changes *product* behavior, not just adds a security boundary
around already-private data — flagged here so it isn't a surprise to whoever operates multiple
tenants' eval suites going forward.

### Trigger for revisiting
- The first time a `User`-within-`Tenant` concept (multiple named users per tenant, with
  RBAC) is needed — `User` and `Tenant` are deliberately orthogonal this week; revisit once
  there's a real multi-seat-per-tenant requirement.
- The first time self-service tenant/key management is needed — today it's operator-only via
  `AdminController`; revisit once there's a real reason a tenant needs to rotate its own keys
  without an operator.
- The first time the frontend auth flow needs to survive a page refresh or be used by a
  non-technical end user — the in-memory prompt (confirmation #10) is deliberately not that;
  revisit with a real backend-for-frontend session design at that point.
- The first time `CostRollupBackgroundService`'s system-write-scope pattern is needed by a
  second cross-tenant job — at that point, confirm the bypass is still narrow and auditable with
  two call sites instead of one, not three or four with divergent justifications.
```

- [ ] **Step 2: Add a tenant-isolation section to `OBSERVABILITY.md`**

Read the file's existing structure first (it has numbered sections `## 1. Capture` through `## 4. UI`, plus `2a`/`2b` sub-sections from Weeks 8-9). Insert a new `## 2c. Tenant isolation` section after `## 2b. Post-hoc scoring`:

```markdown

## 2c. Tenant isolation

Every table in sections 1-2b above is now tenant-scoped (see ADR-014) — `AgentExecution`,
`McpToolCall`, `CostLedger`, `EvalRun`, `EvalResult`, and everything else holding task/execution/
cost/eval data carries a `TenantId`, enforced by an EF Core global query filter (reads) and a
`SaveChanges` interceptor (writes). Every query in this document's sections 1-4 is automatically
scoped to whichever tenant authenticated the request — no query handler needed to change to
achieve this, since the filter is applied generically to every entity implementing
`ITenantScoped`, not per-query.

**Week 1-9 data** (everything that existed before this week) was backfilled onto one well-known
default/system tenant (`00000000-0000-0000-0000-000000000001`), which has zero API keys and is
structurally unreachable by any real caller — it exists only so historical data has a valid
`TenantId`, not as an authentication fallback.

**One exception**: `CostRollupBackgroundService`'s cross-tenant aggregation (section 2, Decision
1) runs inside a narrow, explicit `BeginSystemWriteScope()` — the only code path in this system
that deliberately bypasses per-tenant isolation, because rolling up costs *across* every tenant
is its entire purpose. `CostRollups` rows still carry `TenantId` in their grouping key, so the
resulting aggregates remain per-tenant even though the job that produces them reads across all of
them at once.

Full reasoning: ADR-014 in `DECISIONS.md`.
```

- [ ] **Step 3: Create `DESIGN_PRINCIPLES.md`**

```markdown
# OrchestAI — Design Principles

Standing architectural conventions this project has followed since Week 7, written down once so
future weekly specs can reference them directly instead of re-explaining them each time.

## Enterprise-first, not demo-first
Every feature is built to the standard a real paying customer's data would demand — no "good
enough for a portfolio demo" shortcuts on auth, isolation, or data integrity. Week 10's tenant
isolation work is the clearest expression of this: it was treated as a security boundary, not a
feature, from the first line of its spec.

## Observability and security are defaults, not add-ons
Every agent execution, tool call, and cost event has been traceable since Week 7 (`ADR-011`); every
tenant-scoped table has been isolated since Week 10 (`ADR-014`). Neither was bolted on after the
fact — both were designed to apply automatically to new tables/entities as the system grows,
rather than requiring a developer to remember to add tracing or isolation to each new feature.

## Single-choke-point enforcement for cross-cutting concerns
When a concern applies to many tables/queries at once (cost segregation by `Source` — `ADR-012`;
tenant isolation by `TenantId` — `ADR-014`), the enforcement lives in exactly one place (a
repository method's `Where` clause, a global EF query filter, a `SaveChanges` interceptor) that
every caller passes through, rather than being re-implemented at each call site where it could be
forgotten. If a review ever finds the same cross-cutting check duplicated in multiple handlers,
that's a signal the centralization isn't working as designed, not a normal implementation detail.

## Fail closed, not fail open
Missing tenant context denies access; it never falls back to a default/permissive state
(`ADR-014`). A missing regression baseline throws rather than returning a zeroed-out report
(`ADR-012`). When a system doesn't know the answer, the safe default is "no," not "yes, probably."

## Empirical verification over plausible-sounding review
A diff that reads correctly is not the same as a diff that behaves correctly at runtime — ASP.NET
Core routing bugs, DI captive-dependency issues, and Docker build corruption have all been caught
in this project only by actually running the app, not by review alone. Claims about test counts,
migration correctness, or isolation behavior are verified by running the relevant command and
reading its real output, not by reasoning about what "should" happen.

## Decide, and write down why — including what's deliberately deferred
Every ADR in `DECISIONS.md` records not just what was decided but why, and explicitly lists what
was NOT decided yet and what would trigger revisiting it (e.g. retention policy, baseline
auto-selection, rate limiting). Guessing at a policy with no real usage data to inform it is
treated as worse than leaving it explicitly open.

## Reuse before rebuild
When an existing mechanism already solves a new problem's shape (`EvalCase.CreateEphemeral`
reusing `IEvalScorer` unchanged for post-hoc scoring; `TenantScopingInterceptor` mirroring
`UpdatedAtInterceptor`'s exact shape), extend or reuse it rather than introducing a parallel
abstraction that could drift from the original over time.
```

- [ ] **Step 4: Run the full suite (docs-only change, confirm no regression)**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, unchanged from Task 16.

- [ ] **Step 5: Commit**

```bash
git add DECISIONS.md OBSERVABILITY.md DESIGN_PRINCIPLES.md
git commit -m "docs: add ADR-014 for tenant isolation, update OBSERVABILITY.md, add DESIGN_PRINCIPLES.md"
```

---

### Task 18: Final integration/build validation

**Files:** None created or modified — this task is verification only. If it finds a real defect, stop
and fix it in the file(s) where it actually lives (do not patch around it here), then re-run the
whole task from Step 1.

**Interfaces:**
- Consumes: everything from Tasks 1-17 — the full tenant isolation implementation, all tests, `AdminController` (Task 8 + Step 13), `TenantAuthenticationMiddleware` (Task 9), `LayeringTests` (pre-Week-10 cleanup), `TenantScopingCompletenessTests` (Task 13).
- Produces: nothing new. This is the last task — its passing is the definition of "Week 10 done."

This task has no unit-test step of its own. Its four steps are: full suite, architecture
guardrails specifically, a real Postgres migration apply, and a real end-to-end HTTP smoke test
proving cross-tenant isolation over the wire — not just in-process. This mirrors the live
verification done at the end of Week 9 (starting the API against real Postgres and curling
endpoints) and is required by the spec's framing: "a partially-correct implementation is not '80%
done' — it's a false sense of security."

- [ ] **Step 1: Full suite, green**

Run: `dotnet build OrchestAI.sln` — expect 0 errors, 0 warnings.
Run: `dotnet test tests/OrchestAI.Tests`

Record the exact pass count reported. Compare it against the running total implied by Tasks 1-17
(every task above added tests; if the reported count is lower than expected, STOP — this is
exactly the stale-count signal that caught the Week 9 worktree-isolation bug, see
`feedback_verify_subagent_worktree_isolation` — do not proceed past a suspicious count without
first running `git log --oneline` and `git status` to confirm you're testing the actual
tip-of-branch commit, not a stale checkout).

Expected: PASS, 0 failures, 0 skipped.

- [ ] **Step 2: Architecture guardrails, run in isolation**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~LayeringTests"`
Expected: PASS (4/4) — confirms Task 1-17's new `Tenancy`/`Security`/`Admin` code didn't introduce
a layering violation (e.g. `AsyncLocalCurrentTenantAccessor` living in Infrastructure but
implementing a Domain-defined `ICurrentTenantAccessor` interface — never the reverse).

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantScopingCompletenessTests"`
Expected: PASS — confirms every one of the 13 tenant-scoped entities from Task 2 has a live query
filter and nothing new was added to `OrchestAI.Domain.Entities` without being classified into
either `ExpectedTenantScopedTypes` or `ExpectedGloballySharedTypes`.

If either of these fails, the failure IS the finding — stop and fix the actual violation before
continuing; do not silence the test or add an exemption without confirming with the user first
(these two tests exist specifically to make an isolation gap loud instead of silent, per
`DESIGN_PRINCIPLES.md`'s "empirical verification" principle from Task 17).

- [ ] **Step 3: Apply the migration against a real database, from empty and from populated**

This proves the Task 6 migration works both for a fresh install (no backfill needed) and for the
upgrade path (backfill required) — the two paths exercise different branches of the hand-written
SQL.

```bash
docker compose up -d postgres
dotnet ef database drop --force --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API
dotnet ef database update --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API
```

Expected: all migrations apply cleanly against an empty database, including `AddTenantIsolation`
(its backfill branches become no-ops with zero rows, but the `ALTER COLUMN SET NOT NULL` step
still must succeed).

Then seed pre-Week-10-shaped data and re-apply from a known-good prior migration to prove the
backfill path itself:

```bash
dotnet ef database update <migration-immediately-before-AddTenantIsolation> --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API
psql -h localhost -U orchestai -d orchestai -c "INSERT INTO \"OrchestrationTasks\" (\"Id\", \"UserId\", \"Prompt\", \"Status\", \"CreatedAt\") VALUES (gen_random_uuid(), gen_random_uuid(), 'pre-tenant task', 0, now());"
dotnet ef database update --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API
psql -h localhost -U orchestai -d orchestai -c "SELECT \"Id\", \"TenantId\" FROM \"OrchestrationTasks\" WHERE \"Prompt\" = 'pre-tenant task';"
```

Expected: the pre-existing row's `TenantId` is `00000000-0000-0000-0000-000000000001` (the default
migration tenant from Task 6), and the column is `NOT NULL` (confirm via
`\d "OrchestrationTasks"` in `psql` — `TenantId` shows `not null`).

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~TenantBackfillIntegrationTests"`
Expected: PASS (this is the Task 6 integration test re-run here as a final confirmation against a
clean database state, not a new test).

- [ ] **Step 4: Live end-to-end smoke test over real HTTP — the actual proof of isolation**

Everything above proves the pieces work in isolation. This step proves the composed system
enforces isolation for a real HTTP client, which is the only claim the spec actually cares about.

Start the API against the real database from Step 3:

```bash
ADMIN__BOOTSTRAPSECRET="smoke-test-secret-do-not-use-in-prod" dotnet run --project src/OrchestAI.API
```

In a second terminal, with the API listening on `http://localhost:5000` (adjust the port to match
`launchSettings.json` if different):

```bash
# 1. Create Tenant A
TENANT_A=$(curl -s -X POST http://localhost:5000/api/v1/admin/tenants \
  -H "X-Admin-Secret: smoke-test-secret-do-not-use-in-prod" \
  -H "Content-Type: application/json" \
  -d '{"Name":"Tenant A","Slug":"tenant-a"}')
echo "$TENANT_A"
TENANT_A_ID=$(echo "$TENANT_A" | jq -r '.tenantId')

# 2. Create an API key for Tenant A — RawKey is returned exactly once, capture it now
KEY_A=$(curl -s -X POST http://localhost:5000/api/v1/admin/api-keys \
  -H "X-Admin-Secret: smoke-test-secret-do-not-use-in-prod" \
  -H "Content-Type: application/json" \
  -d "{\"TenantId\":\"$TENANT_A_ID\",\"DisplayName\":\"smoke-test-a\"}")
RAW_KEY_A=$(echo "$KEY_A" | jq -r '.rawKey')

# 3. Create Tenant B and its key, same shape
TENANT_B=$(curl -s -X POST http://localhost:5000/api/v1/admin/tenants \
  -H "X-Admin-Secret: smoke-test-secret-do-not-use-in-prod" \
  -H "Content-Type: application/json" \
  -d '{"Name":"Tenant B","Slug":"tenant-b"}')
TENANT_B_ID=$(echo "$TENANT_B" | jq -r '.tenantId')
KEY_B=$(curl -s -X POST http://localhost:5000/api/v1/admin/api-keys \
  -H "X-Admin-Secret: smoke-test-secret-do-not-use-in-prod" \
  -H "Content-Type: application/json" \
  -d "{\"TenantId\":\"$TENANT_B_ID\",\"DisplayName\":\"smoke-test-b\"}")
RAW_KEY_B=$(echo "$KEY_B" | jq -r '.rawKey')

# 4. As Tenant A, create an eval suite
curl -s -X POST http://localhost:5000/api/v1/eval-suites \
  -H "Authorization: Bearer $RAW_KEY_A" \
  -H "Content-Type: application/json" \
  -d '{"Name":"Tenant A Suite","Description":"smoke test","TargetAgentType":"Research"}' | jq .

# 5. As Tenant A, list suites — expect exactly the one just created
curl -s http://localhost:5000/api/v1/eval-suites -H "Authorization: Bearer $RAW_KEY_A" | jq .

# 6. As Tenant B, list suites — expect an EMPTY list (this is the actual isolation proof)
curl -s http://localhost:5000/api/v1/eval-suites -H "Authorization: Bearer $RAW_KEY_B" | jq .

# 7. No key at all — expect HTTP 401
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/api/v1/eval-suites

# 8. Garbage key — expect HTTP 401
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/api/v1/eval-suites -H "Authorization: Bearer orch_live_garbagegarbage.garbagegarbagegarbagegarbage"

# 9. Suspend Tenant B, then retry Tenant B's key — expect HTTP 403
curl -s -X POST "http://localhost:5000/api/v1/admin/tenants/$TENANT_B_ID/suspend" \
  -H "X-Admin-Secret: smoke-test-secret-do-not-use-in-prod"
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/api/v1/eval-suites -H "Authorization: Bearer $RAW_KEY_B"

# 10. Revoke Tenant A's key, then retry — expect HTTP 401
curl -s -X POST "http://localhost:5000/api/v1/admin/api-keys/$(echo "$KEY_A" | jq -r '.apiKeyId')/revoke" \
  -H "X-Admin-Secret: smoke-test-secret-do-not-use-in-prod"
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/api/v1/eval-suites -H "Authorization: Bearer $RAW_KEY_A"

# 11. The admin surface itself requires no tenant key but IS gated by the admin secret — confirm
# both directions: missing secret is rejected, and the admin routes are NOT reachable with a
# tenant API key instead of the admin secret.
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5000/api/v1/admin/tenants -d '{"Name":"x","Slug":"x"}' -H "Content-Type: application/json"
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5000/api/v1/admin/tenants -H "Authorization: Bearer $RAW_KEY_B" -d '{"Name":"x","Slug":"x"}' -H "Content-Type: application/json"
```

Expected results, in order: (4) 201 with the created suite echoed back; (5) one suite, belonging to
Tenant A; (6) `{"suites":[]}` — Tenant B sees nothing Tenant A created, over real HTTP, through the
full middleware + EF filter stack, not an in-process test double; (7) `401`; (8) `401`; (9) `403`
(fail-closed on suspension, not a silent 200); (10) `401` (revoked key fails closed, same as an
unknown key); (11) both `401`/`403`-class rejections — a tenant API key must never satisfy the
admin gate.

If step 6 returns Tenant A's suite instead of an empty list, this is a Critical, stop-everything
finding — it means the global query filter, the middleware, or the ambient `AsyncLocal` context is
not wired correctly end-to-end despite every unit and integration test passing, and no further
work should proceed until root-caused. Do not rationalize a partial pass here; re-read the spec's
opening framing before deciding this is "close enough."

Stop the API (`Ctrl+C`) and tear down the smoke-test database state:

```bash
docker compose down -v
```

- [ ] **Step 5: Final tally**

Report, in one place: total test count and pass/fail from Step 1, the two architecture-test
results from Step 2, the migration-apply confirmation from Step 3, and the full list of HTTP status
codes observed in Step 4 against their expected values. This report is what "Week 10 complete"
means — not "the tasks are all checked off," but "here is the evidence this security boundary
actually holds under a real client."

No commit for this task — it verifies work already committed in Tasks 1-17. If Step 4 uncovers a
defect and you fix it, that fix gets its own commit in the file(s) where the defect actually lives,
followed by re-running this entire task from Step 1.
