# Week 11: Abuse and Cost Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **This sits directly on top of Week 10's tenant isolation boundary.** A limiter/budget/queue check that only holds under a single request is a false sense of protection — the whole point is holding under concurrency. Do not compress, skip, or "simplify for now" any task below. Every task's tests must actually run and pass against real (in-memory or real Postgres) data, not be reasoned about abstractly.

**Goal:** Add per-tenant rate limiting, concurrency limiting, per-task structural caps (max agents/tool calls), atomic cost-budget reservation, and bounded queue backpressure — all fail-closed, all observable via a unified rejection contract, all read through one `ITenantLimitsProvider`.

**Architecture:** Five enforcement points — rate limiter (ASP.NET Core `RateLimiting` middleware, tenant-partitioned token bucket), a new synchronous admission step inserted into `POST /tasks/{id}/start` (atomic task-state transition + concurrency-slot + budget reservation, all-or-nothing in one DB transaction), an orchestrator-internal structural cap (agents checked pre-dispatch, tool calls checked as a running counter during execution), a bounded `IEvalRunQueue`, and `POST /tasks` idempotency. Reservations (`TaskAdmissionReservation`) are operational state, fully separate from the immutable `CostLedger`/`CostRollup` audit trail — released in full on task completion, excluded from admission math once older than a staleness TTL (crash recovery, not a reconciliation service). One shared exception (`TenantLimitExceededException`) and one shared response writer produce every 429 across all five enforcement points, and log a `RejectionEvent` in the same place they build the response.

**Tech Stack:** C# .NET 8, `Microsoft.AspNetCore.RateLimiting` (built into the shared framework, no new package), EF Core 8 (PostgreSQL, `ExecuteUpdateAsync` CAS + explicit `BeginTransactionAsync` + `FOR UPDATE` row locking — new patterns for this codebase), MediatR, `System.Threading.Channels` (existing `IEvalRunQueue`, extended not replaced), xUnit + FluentAssertions + Moq, NetArchTest (existing `LayeringTests`).

## Global Constraints

- Fail closed: any enforcement point that cannot resolve `TenantLimits` uses `TenantLimitsDefaults` (a single central options class), never an unbounded/unlimited fallback. A tenant with zero rows anywhere still gets the conservative system default, not a bypass.
- Every one of the five enforcement points (rate limiter, concurrency check, budget validator, orchestrator cap, queue manager) reads limits through `ITenantLimitsProvider` only — no independent DB query, no independently-interpreted null-coalescing. If a review finds a second interpretation of "what happens when a limit is unset," that's a defect, not a variation.
- One response contract everywhere a rejection can happen: HTTP 429, `Retry-After` header, JSON body with a `reason` field (`RateLimited | ConcurrencyExceeded | BudgetExceeded | AgentCapExceeded | QueueBackpressure`). Built in exactly one place (`RejectionResponder`, API layer) and reused by both the rate limiter's `OnRejected` callback and a `TenantLimitExceededException`-handling `IExceptionHandler`.
- Every rejection writes one `RejectionEvent` row, in the same place the HTTP response is built — never a rejection that produces a 429 with no corresponding queryable record, never a `RejectionEvent` with no corresponding 429.
- Admission (task-state transition + concurrency-slot reservation + budget reservation) is all-or-nothing: one DB transaction; any failed check rolls back every prior step in the same attempt. A task that fails admission remains `Pending`, never `Running`.
- Reservations (`TaskAdmissionReservation`) are pure operational state — see `DESIGN_PRINCIPLES.md` "Operational state vs. audit state." They are never written back into `CostLedger`/`CostRollup`, and budget checks always read actual spend (from the existing hybrid rollup+live-ledger path) plus the sum of currently-active reservations, never a reconciled/adjusted single number.
- Reservation release happens in a `try/finally` around the background dispatch, covering success, task failure, and unhandled exceptions/crash-in-process alike. A reservation surviving past `AbuseProtectionOptions.ReservationStalenessMinutes` is excluded from future admission math — this is the crash-recovery mechanism; there is no separate reconciliation sweep this week.
- `TenantLimits` extends the brief's stated field list with `MaxQueueDepth` (int?) — required by the queue-backpressure requirement but omitted from the brief's literal field list; documented as a deliberate extension in ADR-015, not a scope violation.
- `TenantLimits.Create(tenantId, ...)` is a third named exception to the Week 10 rule "no `ITenantScoped` factory takes `TenantId`" (`ApiKey.Create`/`CostRollup.Create` were the first two) — same shape: reachable only through the admin-secret-gated `AdminController`, no ambient tenant scope to bypass. `TenantLimits` itself is **not** `ITenantScoped` (like `Tenant`/`ApiKey` — an identity/config table keyed directly by an explicit `TenantId` column, not the global query filter), for the same reason `ApiKey` isn't: the admin caller has no ambient tenant to filter by.
- `RejectionEvent`, `IdempotencyRecord`, `TaskAdmissionReservation` ARE `ITenantScoped` and are always created inside a live ambient-tenant scope (never given `TenantId` explicitly in their factories) — including the exception-handler path, which must explicitly restore the ambient tenant from the exception's captured `TenantId` before writing, since the AsyncLocal scope set by `TenantAuthenticationMiddleware` is already unwound by the time global exception handling runs (see Task 2's investigation note).
- No distributed/multi-instance rate-limiter or queue state — everything here is in-memory or single-Postgres-instance state, consistent with the current single-instance deployment. Documented as a known limitation in ADR-015, not silently assumed.
- No self-service tenant limit configuration, no tiered pricing, no ML-based abuse detection — simple threshold checks, operator-set via the same `AdminController`/`RequireAdminSecretFilter` pattern Week 10 established.
- Keep the 0-warning, all-tests-green bar. Baseline before Task 1: confirm current test count by running `dotnet test` before starting.

---

## Investigation summary (already done — do not re-derive)

Verified directly against the current codebase (not assumed):

- **Task submission is two separate HTTP calls, not one.** `POST /api/v1/tasks` (`CreateOrchestrationTaskCommand` → `CreateOrchestrationTaskHandler`, `src/OrchestAI.Application/Commands/CreateOrchestrationTask/`) is fully synchronous and only records intent (`OrchestrationTask.Create`, status `Pending`). `POST /api/v1/tasks/{id}/start` (`TasksController.StartAsync`, `src/OrchestAI.API/Controllers/TasksController.cs`) dispatches `StartOrchestrationCommand` inside an **unawaited `Task.Run`** and returns `202 Accepted` immediately — the handler hasn't even started when the HTTP response is written. This means any check that must produce an HTTP 429 (concurrency, budget) has to run **synchronously in the controller, before `Task.Run`**, not inside the existing fire-and-forget handler.
- **`StartOrchestrationHandler`** (`src/OrchestAI.Application/Commands/StartOrchestration/StartOrchestrationHandler.cs`, read in full): looks up the task, throws `NotFoundException` if missing, throws bare `InvalidOperationException` if `task.Status != Pending` (never `ConflictException`, unlike `ApproveOrchestrationTaskHandler`/`RejectOrchestrationTaskHandler` which do use `ConflictException` for equivalent invalid-state cases — Task 7 fixes this inconsistency), then `task.MarkRunning()` + `UpdateAsync` (a plain read-then-write with a real TOCTOU gap today — two concurrent `/start` calls could both pass the status check), then `_orchestratorAgent.PlanAsync(...)`, optional approval wait, then either `RunSequentialAsync` or `Task.WhenAll` over `RunSubAgentAsync` for parallel agents, then aggregation, `MarkCompleted`/`MarkFailed`, one final `UpdateAsync`.
- **`OrchestrationTask`** (`src/OrchestAI.Domain/Entities/OrchestrationTask.cs`, read in full): `IHasUpdatedAt, ITenantScoped`. `MarkRunning()` is a bare setter — no state-guard inside the entity itself; the guard lives in the handler. `Status` is `OrchestrationTaskStatus` (`Pending`, `Running`, `WaitingForApproval`, `Completed`, `Failed`, per usage seen).
- **Where tool calls actually happen — this determines where `MaxToolCallsPerTask` can be enforced.** `OrchestrationPlan` (`src/OrchestAI.Domain/Models/OrchestrationPlan.cs`) only has `SelectedAgents`/`ExecutionOrder`/`AgentPrompts`/`ExecutionMode` — **no tool-call count is known at planning time.** Tool calls happen deep inside `AgentBase.ExecuteAsync`'s agentic loop (`src/OrchestAI.Infrastructure/Agents/Base/AgentBase.cs`, read in full), specifically `InvokeToolAsync` (private method, called once per `ToolRequest` inside the `foreach (var request in turn.ToolRequests)` loop). All 5 concrete agents (`ResearchAgent`, `WriterAgent`, `CodeAgent`, `DataAgent`, `BrowserAgent`) extend `AgentBase` and forward their full constructor parameter list to `base(...)` unchanged (confirmed via `ResearchAgent.cs` read in full) — adding a new `AgentBase` constructor parameter means mechanically updating all 5 concrete classes plus DI registration. Sub-agents run **in parallel** for `ExecutionMode.Parallel` (`Task.WhenAll(subAgentTasks)` in `StartOrchestrationHandler`), so a shared per-task tool-call counter must be safe under concurrent increments from sibling agents — an `AsyncLocal`-backed counter (mirroring `ICurrentTenantAccessor`'s proven pattern) wrapping a class with an `Interlocked`-incremented field satisfies this: `Task.WhenAll` children fork from the same async context and share the same `AsyncLocal` value (a reference to one counter instance), and `Interlocked.Increment` on that shared instance is safe across concurrent siblings.
- **`AppDbContext`** (`src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, read in full): constructor takes `ICurrentTenantAccessor` directly (not via `IHttpContextAccessor`); `OnModelCreating` applies a tenant query filter generically via reflection to every `ITenantScoped` entity type — a new entity implementing `ITenantScoped` is automatically protected with zero extra `HasQueryFilter` calls. Registered via `AddDbContextFactory` (`src/OrchestAI.Infrastructure/DependencyInjection.cs`); every repository takes `IDbContextFactory<AppDbContext>` and creates one fresh context per call — **no existing repository method wraps multiple operations in one explicit transaction.** Task 6 introduces this codebase's first explicit multi-statement transaction.
- **`ICurrentTenantAccessor`/`AsyncLocalCurrentTenantAccessor`** (read in full): ambient tenant is `AsyncLocal<Guid?>`, restored via an `IDisposable` scope (`SetTenant`). Critically: **`TenantAuthenticationMiddleware`'s `using (_tenantAccessor.SetTenant(tenant.Id)) { await next(context); }` unwinds (clears the ambient tenant) the instant an exception propagates back out of `next(context)`** — meaning by the time global exception-handling middleware (registered *before* the tenant middleware in `Program.cs`'s pipeline) runs, the ambient tenant is already gone. Any exception-handler-level code that needs the tenant (writing a `RejectionEvent` for a `TenantLimitExceededException`) must get it from the exception itself, then explicitly re-open a scope (`_tenantAccessor.SetTenant(ex.TenantId)`) around the write — it cannot rely on ambient state. This is a genuine, non-obvious discovery, not present in the original brief.
- **`Program.cs`** (read in full, 117 lines): current middleware order is `UseSerilogRequestLogging` → (dev) Swagger UI → `UseExceptionHandler()` → `UseStatusCodePages()` → `UseCors("Frontend")` → `UseMiddleware<TenantAuthenticationMiddleware>()` → `MapControllers()` → `/health`. `AddProblemDetails()` is already called (no options configured). No `IExceptionHandler` is registered today — `UseExceptionHandler()` relies entirely on the ASP.NET Core default fallback. The new rate limiter (`UseRateLimiter()`) must go **after** `UseMiddleware<TenantAuthenticationMiddleware>()` so its tenant-partition resolver sees a resolved ambient tenant; the new `TenantLimitExceededExceptionHandler` registers via `AddExceptionHandler<T>()` (composes with the existing `UseExceptionHandler()`/`AddProblemDetails()` call, no pipeline-order change needed for it).
- **No existing rate limiting of any kind** — confirmed via grep for `RateLimit`/`RateLimiter` across `src/`, zero hits. Fully greenfield; `System.Threading.RateLimiting`/`Microsoft.AspNetCore.RateLimiting` ship in the ASP.NET Core 8 shared framework, no new NuGet package needed.
- **`IEvalRunQueue`/`InMemoryEvalRunQueue`** (read in full): a single shared, unbounded `Channel<EvalRunQueueItem>` used by both live-suite eval runs and post-hoc scoring (`RequestPostHocScoringHandler` confirmed to enqueue through the same `IEvalRunQueue`). No queue-depth or backpressure concept exists today. **Task execution itself is never queued** — `/start` dispatches directly via `Task.Run`, so "queue backpressure for task execution" in the brief is actually satisfied by the concurrency-slot admission check (Task 6/7), not a separate queue; only `IEvalRunQueue` needs literal bounded-queue-depth semantics (Task 10). Because the channel is already a single global FIFO, adding a per-tenant depth *counter* alongside it (rather than replacing it with N per-tenant channels) trivially preserves "FIFO within a tenant's own partition" — a tenant's own items are already a FIFO subsequence of one globally-ordered channel.
- **Cost dashboard read pattern (Week 7/ADR-011)** (`GetCostDashboardHandler.cs`, read in full): for any day before today, reads pre-aggregated `CostRollup` via `ICostRollupRepository.GetByDateRangeAsync`; for today, reads `ICostLedgerRepository.GetDailyAggregatesAsync` live. Both are already tenant-scoped via the standard query filter (ambient ally). Task 5's budget-check spend read reuses these same two repository methods unchanged — no modification to `GetCostDashboardHandler` itself (avoids unrelated-refactor scope creep).
- **`ModelPricing`** (`src/OrchestAI.Domain/Entities/ModelPricing.cs`) + **`IModelPricingCache`**: DB-backed pricing, cached in memory (`ModelPricingCache`, `AddSingleton`) — the exact pattern `EfTenantLimitsProvider` (Task 1) and `IBudgetEstimator`'s conservative-cost lookup (Task 5) both reuse.
- **Admin bootstrap pattern** (`AdminController.cs`, `RequireAdminSecretFilter`, read in full): `[Route("api/v1/admin")]`, `[ServiceFilter(typeof(RequireAdminSecretFilter))]`, exempted from `TenantAuthenticationMiddleware` by path prefix (`/api/v1/admin`), each action a thin `try/catch` around one `_mediator.Send(...)` call. `SetTenantLimitsCommand` (Task 3) follows this exact shape. Note the `CreatedAtAction` gotcha already documented in-repo: must use the literal action-name string (e.g. `"SetTenantLimits"`), never `nameof(...Async)`, since ASP.NET Core strips the `Async` suffix from routed action names.
- **Migration convention**: one migration per task/logical change (`AddTenantIsolation`, `AddTenantForeignKeys`, `ExtendCostRollupUniqueIndexWithTenantId` were three separate migrations for Week 10, not one giant migration) — confirmed via `src/OrchestAI.Infrastructure/Migrations/` (note: NOT `Data/Migrations/`, which is an empty leftover directory — don't create files there). This plan follows the same convention: one migration per task that introduces new tables/columns.
- **Real-Postgres integration test pattern** (`tests/OrchestAI.Tests/Infrastructure/TenantFilterExecuteDeleteTests.cs`, read in full): a `TransactionScopedDbContextFactory` wrapping one shared `NpgsqlConnection`/`NpgsqlTransaction`, rolled back in a `finally` so the shared dev database is untouched regardless of pass/fail. Connection string `Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme` (matches `appsettings.json`). This is the pattern Task 11's concurrency/race tests use — EF Core's InMemory provider cannot translate `ExecuteUpdateAsync` or `FOR UPDATE` locking at all, so these tests **must** run against real Postgres (`docker-compose.yml`).
- **Layering rules** (`tests/OrchestAI.Tests/Architecture/LayeringTests.cs`, read in full): `Domain` depends on nothing; `Application` must not depend on `Infrastructure` or `API`; `Infrastructure` must not depend on `API`, `Microsoft.AspNetCore.Mvc`, or `Microsoft.AspNetCore.Http` — but **`Infrastructure` freely depends on `Application`** today (`LlmJudgeScorer.cs`, `TenantScopingInterceptor.cs`, `DependencyInjection.cs` all already do). This means `TenantLimitExceededException` belongs in `OrchestAI.Application.Exceptions` (thrown from both `Application` admission handlers and `Infrastructure`'s `AgentBase`), while `RejectionResponder` and the new `IExceptionHandler` (both need `HttpContext`) belong in `OrchestAI.API`, exactly like `TenantAuthenticationMiddleware`.

---

## Blocking-confirmation answers (resolved before any task below)

1. **Rate-limit granularity and mechanism:** tenant-level (aggregate across all of a tenant's API keys), via `Microsoft.AspNetCore.RateLimiting`'s `PartitionedRateLimiter`, partition key resolved from `ICurrentTenantAccessor.TenantId` (populated post-auth, since the rate limiter middleware runs after `TenantAuthenticationMiddleware`). Token bucket (`RateLimitPartition.GetTokenBucketLimiter`), chosen because it allows short legitimate bursts (a dashboard loading several widgets at once) while still enforcing a steady average rate — a fixed/sliding window would reject a legitimate burst that a token bucket smooths over. Documented in ADR-015.
2. **Cost cap is two mechanisms:** (a) a per-task structural ceiling (`MaxAgentsPerTask` checked pre-dispatch after planning; `MaxToolCallsPerTask` checked as a running counter during execution, since tool calls aren't known until agents make them) that fails the task cleanly, never truncates silently; (b) a tenant-level running budget, checked via atomic reservation at admission time (see #4), reconciled by simple deletion (not adjustment) on task completion — actual spend always comes from the untouched `CostLedger`/`CostRollup` audit trail.
3. **Response contract:** `429` + `Retry-After` header + JSON body `{ reason, detail, ... }` where `reason` is one of `RateLimited | ConcurrencyExceeded | BudgetExceeded | AgentCapExceeded | QueueBackpressure`. One shared builder (`RejectionResponder`, Task 2), called from both the rate limiter's `OnRejected` and a new `TenantLimitExceededExceptionHandler : IExceptionHandler`.
4. **Tenant isolation of new runtime state:** rate-limiter partitions keyed by `TenantId` (never a global bucket); concurrency/budget reservations keyed by `TenantId` in `TaskAdmissionReservation` (an `ITenantScoped` entity, automatically filtered); queue-depth counters keyed by `TenantId` in a `ConcurrentDictionary<Guid, int>`. Task 11's cross-tenant isolation sweep proves none of these leak between tenants empirically.
5. **Queue backpressure:** `IEvalRunQueue` (already shared by live-suite eval runs and post-hoc scoring) gets a per-tenant depth counter and rejects `EnqueueAsync` once a tenant's in-flight count reaches `TenantLimits.MaxQueueDepth`; since the underlying channel stays one global FIFO, per-tenant FIFO ordering is automatically preserved (a subsequence of a FIFO sequence is FIFO). Task execution itself needs no separate queue-depth check — the admission concurrency-slot check (Task 6/7) already gates it.
6. **Admission atomicity:** the three-step admission chain (`Pending→Running` CAS, concurrency-slot count check, budget check) runs inside one explicit DB transaction that also takes a `SELECT ... FOR UPDATE` row lock on the tenant's own `Tenants` row — serializing concurrent admissions for the *same* tenant only (different tenants' admissions never block each other). Any failed check rolls back the whole transaction, including the CAS — a rejected task is provably still `Pending`, never left `Running` with no reservation.
7. **Reservation/ledger separation:** `TaskAdmissionReservation` is pure operational state (`DESIGN_PRINCIPLES.md`), fully disjoint from `CostLedger`. Budget checks read `actual spend (CostRollup+live CostLedger) + SUM(active, non-stale TaskAdmissionReservation rows)`, never a reconciled running total. Release = `DELETE`, not an adjustment written anywhere.
8. **Budget estimation:** isolated behind `IBudgetEstimator` (`ConservativeBudgetEstimator` for this week) — `MaxAgentsPerTask × MaxToolCallsPerTask × AbuseProtectionOptions.AssumedCostPerToolCallUsd`, deliberately conservative (over-reserves rather than risks overspend), documented as a fail-closed tradeoff in ADR-015, easy to swap for a smarter estimator later without touching admission logic.
9. **Crash recovery:** TTL-based staleness exclusion (`AbuseProtectionOptions.ReservationStalenessMinutes`, default 30) applied as a `WHERE CreatedAt > now - staleness` filter inside every admission-math query — no reconciliation service, documented as an accepted single-instance-architecture limitation in ADR-015.
10. **Idempotency:** `Idempotency-Key` header on `POST /tasks` only (`CreateOrchestrationTaskCommand`) — `/start` stays protected by its (now-atomic, Task 6) `Pending→Running` CAS, which already makes a retried `/start` either 409 (already `Running`) or safely start-once. Mismatched payload on key reuse → `409 Conflict`.
11. **Centralization:** all five enforcement points read `ITenantLimitsProvider` exclusively — no independent `TenantLimits` queries, no independently-interpreted defaults. `ITenantLimitsProvider.GetAsync` (async, cached ~30s, used by admission/orchestrator-cap/queue-manager) and `GetSnapshot` (sync, cache-only, used only by the rate limiter's partition-key factory, which the `RateLimitPartition` API requires to be synchronous).

---

### Task 1: `TenantLimits` domain model, `ITenantLimitsProvider`, and system-wide defaults

**Files:**
- Create: `src/OrchestAI.Domain/Entities/TenantLimits.cs`
- Create: `src/OrchestAI.Domain/Models/ResolvedTenantLimits.cs`
- Create: `src/OrchestAI.Domain/Interfaces/ITenantLimitsRepository.cs`
- Create: `src/OrchestAI.Domain/Interfaces/ITenantLimitsProvider.cs`
- Create: `src/OrchestAI.Infrastructure/Configuration/TenantLimitsDefaults.cs`
- Create: `src/OrchestAI.Application/Configuration/AbuseProtectionOptions.cs` (Application layer, not Infrastructure — see note below)
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/TenantLimitsConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/TenantLimitsRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Tenancy/EfTenantLimitsProvider.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/AppDbContext.cs` (add `DbSet<TenantLimits>`, apply new configuration)
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register repository, provider, options)
- Modify: `src/OrchestAI.API/appsettings.json` (add `TenantLimitsDefaults`/`AbuseProtection` sections)
- Create: `src/OrchestAI.Infrastructure/Migrations/{timestamp}_AddTenantLimits.cs` (+ `.Designer.cs`, snapshot update — via `dotnet ef migrations add`)
- Test: Create `tests/OrchestAI.Tests/Domain/TenantLimitsTests.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/EfTenantLimitsProviderTests.cs`

**Interfaces:**
- Produces: `TenantLimits.Create(tenantId, requestsPerMinute?, maxConcurrentTasks?, maxAgentsPerTask?, maxToolCallsPerTask?, dailyCostBudgetUsd?, monthlyCostBudgetUsd?, maxQueueDepth?)`, `TenantLimits.Update(same 7 params)`; `ResolvedTenantLimits(int RequestsPerMinute, int MaxConcurrentTasks, int MaxAgentsPerTask, int MaxToolCallsPerTask, decimal DailyCostBudgetUsd, decimal MonthlyCostBudgetUsd, int MaxQueueDepth)` — all non-nullable, this is the only shape later tasks read; `ITenantLimitsRepository.GetByTenantIdAsync(Guid, CancellationToken)`, `.UpsertAsync(TenantLimits, CancellationToken)`; `ITenantLimitsProvider.GetAsync(Guid tenantId, CancellationToken)`, `.GetSnapshot(Guid tenantId)` (sync, cache-only).

- [ ] **Step 1: Write the failing domain test**

Create `tests/OrchestAI.Tests/Domain/TenantLimitsTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Tests.Domain;

public sealed class TenantLimitsTests
{
    [Fact]
    public void Create_AllFieldsNull_ProducesNullRow()
    {
        var tenantId = Guid.NewGuid();
        var limits = TenantLimits.Create(tenantId, null, null, null, null, null, null, null);

        limits.TenantId.Should().Be(tenantId);
        limits.RequestsPerMinute.Should().BeNull();
        limits.MaxConcurrentTasks.Should().BeNull();
        limits.MaxAgentsPerTask.Should().BeNull();
        limits.MaxToolCallsPerTask.Should().BeNull();
        limits.DailyCostBudgetUsd.Should().BeNull();
        limits.MonthlyCostBudgetUsd.Should().BeNull();
        limits.MaxQueueDepth.Should().BeNull();
    }

    [Fact]
    public void Update_ChangesAllFieldsAndBumpsUpdatedAt()
    {
        var limits = TenantLimits.Create(Guid.NewGuid(), null, null, null, null, null, null, null);
        var before = limits.UpdatedAt;

        limits.Update(200, 10, 8, 100, 75m, 750m, 200);

        limits.RequestsPerMinute.Should().Be(200);
        limits.MaxConcurrentTasks.Should().Be(10);
        limits.MaxAgentsPerTask.Should().Be(8);
        limits.MaxToolCallsPerTask.Should().Be(100);
        limits.DailyCostBudgetUsd.Should().Be(75m);
        limits.MonthlyCostBudgetUsd.Should().Be(750m);
        limits.MaxQueueDepth.Should().Be(200);
        limits.UpdatedAt.Should().BeOnOrAfter(before);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter TenantLimitsTests`
Expected: FAIL (compile error — `TenantLimits` doesn't exist yet).

- [ ] **Step 3: Implement `TenantLimits`**

Create `src/OrchestAI.Domain/Entities/TenantLimits.cs`:

```csharp
namespace OrchestAI.Domain.Entities;

// One row per tenant, created only via SetTenantLimitsHandler (admin/bootstrap-only — see
// AdminController). Every field is nullable: null means "use TenantLimitsDefaults," never
// "use zero" or "unlimited." Read exclusively through ITenantLimitsProvider — see
// DESIGN_PRINCIPLES.md "Single-choke-point enforcement" and ADR-015.
//
// Not ITenantScoped — like Tenant/ApiKey, this is an identity/config table the admin caller
// (no ambient tenant) addresses by an explicit TenantId column, not the global query filter.
public sealed class TenantLimits
{
    private TenantLimits() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public int? RequestsPerMinute { get; private set; }
    public int? MaxConcurrentTasks { get; private set; }
    public int? MaxAgentsPerTask { get; private set; }
    public int? MaxToolCallsPerTask { get; private set; }
    public decimal? DailyCostBudgetUsd { get; private set; }
    public decimal? MonthlyCostBudgetUsd { get; private set; }
    public int? MaxQueueDepth { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Third named exception to ADR-014's "no ITenantScoped factory takes TenantId" rule
    // (ApiKey.Create/CostRollup.Create were the first two) — same shape: reachable only
    // through the admin-secret-gated AdminController, no ambient tenant to bypass. See ADR-015.
    public static TenantLimits Create(
        Guid tenantId,
        int? requestsPerMinute,
        int? maxConcurrentTasks,
        int? maxAgentsPerTask,
        int? maxToolCallsPerTask,
        decimal? dailyCostBudgetUsd,
        decimal? monthlyCostBudgetUsd,
        int? maxQueueDepth)
    {
        return new TenantLimits
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestsPerMinute = requestsPerMinute,
            MaxConcurrentTasks = maxConcurrentTasks,
            MaxAgentsPerTask = maxAgentsPerTask,
            MaxToolCallsPerTask = maxToolCallsPerTask,
            DailyCostBudgetUsd = dailyCostBudgetUsd,
            MonthlyCostBudgetUsd = monthlyCostBudgetUsd,
            MaxQueueDepth = maxQueueDepth,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(
        int? requestsPerMinute,
        int? maxConcurrentTasks,
        int? maxAgentsPerTask,
        int? maxToolCallsPerTask,
        decimal? dailyCostBudgetUsd,
        decimal? monthlyCostBudgetUsd,
        int? maxQueueDepth)
    {
        RequestsPerMinute = requestsPerMinute;
        MaxConcurrentTasks = maxConcurrentTasks;
        MaxAgentsPerTask = maxAgentsPerTask;
        MaxToolCallsPerTask = maxToolCallsPerTask;
        DailyCostBudgetUsd = dailyCostBudgetUsd;
        MonthlyCostBudgetUsd = monthlyCostBudgetUsd;
        MaxQueueDepth = maxQueueDepth;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter TenantLimitsTests`
Expected: PASS (2/2).

- [ ] **Step 5: Add `ResolvedTenantLimits`, defaults, and provider interfaces**

Create `src/OrchestAI.Domain/Models/ResolvedTenantLimits.cs`:

```csharp
namespace OrchestAI.Domain.Models;

// TenantLimits with every nullable field resolved against TenantLimitsDefaults — the only
// shape any of the five enforcement points should ever read. See ITenantLimitsProvider.
public sealed record ResolvedTenantLimits(
    int RequestsPerMinute,
    int MaxConcurrentTasks,
    int MaxAgentsPerTask,
    int MaxToolCallsPerTask,
    decimal DailyCostBudgetUsd,
    decimal MonthlyCostBudgetUsd,
    int MaxQueueDepth);
```

Create `src/OrchestAI.Domain/Interfaces/ITenantLimitsRepository.cs`:

```csharp
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface ITenantLimitsRepository
{
    Task<TenantLimits?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task UpsertAsync(TenantLimits limits, CancellationToken cancellationToken = default);
}
```

Create `src/OrchestAI.Domain/Interfaces/ITenantLimitsProvider.cs`:

```csharp
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// The one place every enforcement point (rate limiter, concurrency check, budget validator,
// orchestrator cap, queue manager) reads TenantLimits through — see DESIGN_PRINCIPLES.md
// "Single-choke-point enforcement" and ADR-015 confirmation #8.
public interface ITenantLimitsProvider
{
    Task<ResolvedTenantLimits> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);

    // Cache-only, synchronous — the ONLY caller is the rate limiter's partition-key factory,
    // which the RateLimitPartition API requires to be synchronous. Returns system defaults for
    // a tenant not yet cached (safe: system defaults are the fail-closed conservative baseline,
    // never "unlimited"). Every other call site must use GetAsync.
    ResolvedTenantLimits GetSnapshot(Guid tenantId);
}
```

Create `src/OrchestAI.Infrastructure/Configuration/TenantLimitsDefaults.cs`:

```csharp
namespace OrchestAI.Infrastructure.Configuration;

// System-wide fallback values for any tenant without an explicit TenantLimits row, or with a
// null field on that row. The single source of truth for "what happens when nothing is
// configured" — see DESIGN_PRINCIPLES.md "Single-choke-point enforcement" and ADR-015.
public sealed class TenantLimitsDefaults
{
    public const string SectionName = "TenantLimitsDefaults";

    public int RequestsPerMinute { get; init; } = 120;
    public int MaxConcurrentTasks { get; init; } = 5;
    public int MaxAgentsPerTask { get; init; } = 5;
    public int MaxToolCallsPerTask { get; init; } = 50;
    public decimal DailyCostBudgetUsd { get; init; } = 50m;
    public decimal MonthlyCostBudgetUsd { get; init; } = 500m;
    public int MaxQueueDepth { get; init; } = 100;
}
```

Create `src/OrchestAI.Application/Configuration/AbuseProtectionOptions.cs` — deliberately in
`OrchestAI.Application.Configuration`, not `Infrastructure.Configuration`: Application-layer
handlers (`CreateOrchestrationTaskHandler`'s idempotency TTL in Task 4, the admission handler's
reservation staleness in Task 6) need to read this directly, and `Application` must not depend
on `Infrastructure` (`LayeringTests.Application_DoesNotDependOnInfrastructure`) — this is the
exact violation Week 9's `RequestPostHocScoringHandler`/`EvalOptions` already hit once; `EvalOptions`
was moved to `Application.Configuration` for the same reason, and this mirrors that fix
proactively instead of repeating the mistake a third time. `TenantLimitsDefaults` stays in
`Infrastructure.Configuration` since only `EfTenantLimitsProvider` (Infrastructure) reads it directly:

```csharp
namespace OrchestAI.Application.Configuration;

// System-wide, non-per-tenant abuse-protection knobs (distinct from TenantLimitsDefaults,
// which are per-tenant-overridable). See ADR-015.
public sealed class AbuseProtectionOptions
{
    public const string SectionName = "AbuseProtection";

    // Crash-recovery TTL for TaskAdmissionReservation rows — see DESIGN_PRINCIPLES.md
    // "Operational state vs. audit state" and ADR-015. Must exceed the longest expected
    // orchestration duration plus a safety margin.
    public int ReservationStalenessMinutes { get; init; } = 30;

    public int IdempotencyKeyTtlHours { get; init; } = 24;

    // How long EfTenantLimitsProvider.GetAsync caches a resolved TenantLimits row before
    // re-reading the database. GetSnapshot never re-reads regardless of this value — it is
    // cache-only by construction.
    public int TenantLimitsCacheRefreshSeconds { get; init; } = 30;

    // IBudgetEstimator's conservative per-tool-call cost assumption — deliberately a flat,
    // over-conservative number rather than a real pricing lookup. See ADR-015 confirmation #8.
    public decimal AssumedCostPerToolCallUsd { get; init; } = 0.05m;
}
```

- [ ] **Step 6: EF configuration, repository, and provider implementation**

Create `src/OrchestAI.Infrastructure/Data/Configurations/TenantLimitsConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class TenantLimitsConfiguration : IEntityTypeConfiguration<TenantLimits>
{
    public void Configure(EntityTypeBuilder<TenantLimits> builder)
    {
        builder.ToTable("TenantLimits");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.Property(x => x.DailyCostBudgetUsd).HasColumnType("decimal(18,6)");
        builder.Property(x => x.MonthlyCostBudgetUsd).HasColumnType("decimal(18,6)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Create `src/OrchestAI.Infrastructure/Repositories/TenantLimitsRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class TenantLimitsRepository : ITenantLimitsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TenantLimitsRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<TenantLimits?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.TenantLimits
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(TenantLimits limits, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ctx.TenantLimits
            .FirstOrDefaultAsync(x => x.TenantId == limits.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
            ctx.TenantLimits.Add(limits);
        else
            ctx.Entry(existing).CurrentValues.SetValues(limits);

        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

Create `src/OrchestAI.Infrastructure/Tenancy/EfTenantLimitsProvider.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Tenancy;

// Mirrors IModelPricingCache's shape (Week 7): a DB-backed value cached in memory with a short
// refresh interval, because the rate limiter needs a synchronous read on every request and the
// other four enforcement points need a "dashboard-speed," not full-scan, read.
public sealed class EfTenantLimitsProvider : ITenantLimitsProvider
{
    private readonly ITenantLimitsRepository _repository;
    private readonly TenantLimitsDefaults _defaults;
    private readonly TimeSpan _refreshInterval;
    private readonly ConcurrentDictionary<Guid, (ResolvedTenantLimits Limits, DateTimeOffset CachedAt)> _cache = new();

    public EfTenantLimitsProvider(
        ITenantLimitsRepository repository,
        IOptions<TenantLimitsDefaults> defaults,
        IOptions<AbuseProtectionOptions> abuseProtectionOptions)
    {
        _repository = repository;
        _defaults = defaults.Value;
        _refreshInterval = TimeSpan.FromSeconds(abuseProtectionOptions.Value.TenantLimitsCacheRefreshSeconds);
    }

    public async Task<ResolvedTenantLimits> GetAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(tenantId, out var cached) && DateTimeOffset.UtcNow - cached.CachedAt < _refreshInterval)
            return cached.Limits;

        var row = await _repository.GetByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var resolved = Resolve(row);
        _cache[tenantId] = (resolved, DateTimeOffset.UtcNow);
        return resolved;
    }

    public ResolvedTenantLimits GetSnapshot(Guid tenantId) =>
        _cache.TryGetValue(tenantId, out var cached) ? cached.Limits : Resolve(null);

    private ResolvedTenantLimits Resolve(Domain.Entities.TenantLimits? row) => new(
        row?.RequestsPerMinute ?? _defaults.RequestsPerMinute,
        row?.MaxConcurrentTasks ?? _defaults.MaxConcurrentTasks,
        row?.MaxAgentsPerTask ?? _defaults.MaxAgentsPerTask,
        row?.MaxToolCallsPerTask ?? _defaults.MaxToolCallsPerTask,
        row?.DailyCostBudgetUsd ?? _defaults.DailyCostBudgetUsd,
        row?.MonthlyCostBudgetUsd ?? _defaults.MonthlyCostBudgetUsd,
        row?.MaxQueueDepth ?? _defaults.MaxQueueDepth);
}
```

- [ ] **Step 7: Wire into `AppDbContext` and DI**

In `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, add after the `ModelPricing` `DbSet`:

```csharp
    public DbSet<TenantLimits> TenantLimits => Set<TenantLimits>();
```

And in `OnModelCreating`, after `modelBuilder.ApplyConfiguration(new ModelPricingConfiguration());`:

```csharp
        modelBuilder.ApplyConfiguration(new TenantLimitsConfiguration());
```

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, after `services.AddScoped<IApiKeyRepository, ApiKeyRepository>();`:

```csharp
        services.AddScoped<ITenantLimitsRepository, TenantLimitsRepository>();
        services.AddSingleton<ITenantLimitsProvider, EfTenantLimitsProvider>();
```

And after `services.Configure<EvalOptions>(configuration.GetSection(EvalOptions.SectionName));`:

```csharp
        services.Configure<TenantLimitsDefaults>(configuration.GetSection(TenantLimitsDefaults.SectionName));
        services.Configure<AbuseProtectionOptions>(configuration.GetSection(AbuseProtectionOptions.SectionName));
```

In `src/OrchestAI.API/appsettings.json`, add after the `"PiiRedaction"` section:

```json
  "TenantLimitsDefaults": {
    "RequestsPerMinute": 120,
    "MaxConcurrentTasks": 5,
    "MaxAgentsPerTask": 5,
    "MaxToolCallsPerTask": 50,
    "DailyCostBudgetUsd": 50,
    "MonthlyCostBudgetUsd": 500,
    "MaxQueueDepth": 100
  },
  "AbuseProtection": {
    "ReservationStalenessMinutes": 30,
    "IdempotencyKeyTtlHours": 24,
    "TenantLimitsCacheRefreshSeconds": 30,
    "AssumedCostPerToolCallUsd": 0.05
  }
```

- [ ] **Step 8: Generate the migration**

Run: `cd src/OrchestAI.Infrastructure && dotnet ef migrations add AddTenantLimits --startup-project ../OrchestAI.API`
Expected: creates `{timestamp}_AddTenantLimits.cs`/`.Designer.cs` under `Migrations/` and updates `AppDbContextModelSnapshot.cs`. Inspect the generated `Up()` — confirm it creates table `TenantLimits` with a unique index on `TenantId` and a FK to `Tenants`, matching `TenantLimitsConfiguration`.

- [ ] **Step 9: Provider test against a real-ish cache scenario**

Create `tests/OrchestAI.Tests/Infrastructure/EfTenantLimitsProviderTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EfTenantLimitsProviderTests
{
    private static EfTenantLimitsProvider CreateProvider(Mock<ITenantLimitsRepository> repoMock, int refreshSeconds = 30)
    {
        return new EfTenantLimitsProvider(
            repoMock.Object,
            Options.Create(new TenantLimitsDefaults()),
            Options.Create(new AbuseProtectionOptions { TenantLimitsCacheRefreshSeconds = refreshSeconds }));
    }

    [Fact]
    public async Task GetAsync_NoRow_ReturnsSystemDefaults()
    {
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantLimits?)null);
        var provider = CreateProvider(repoMock);

        var resolved = await provider.GetAsync(Guid.NewGuid());

        resolved.RequestsPerMinute.Should().Be(120);
        resolved.MaxConcurrentTasks.Should().Be(5);
        resolved.DailyCostBudgetUsd.Should().Be(50m);
    }

    [Fact]
    public async Task GetAsync_PartialRow_MergesRowValuesWithDefaults()
    {
        var tenantId = Guid.NewGuid();
        var row = TenantLimits.Create(tenantId, requestsPerMinute: 500, null, null, null, null, null, null);
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var provider = CreateProvider(repoMock);

        var resolved = await provider.GetAsync(tenantId);

        resolved.RequestsPerMinute.Should().Be(500);
        resolved.MaxConcurrentTasks.Should().Be(5, "unset fields fall back to defaults");
    }

    [Fact]
    public async Task GetAsync_CalledTwiceWithinRefreshWindow_OnlyQueriesRepositoryOnce()
    {
        var tenantId = Guid.NewGuid();
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync((TenantLimits?)null);
        var provider = CreateProvider(repoMock, refreshSeconds: 300);

        await provider.GetAsync(tenantId);
        await provider.GetAsync(tenantId);

        repoMock.Verify(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GetSnapshot_TenantNeverCached_ReturnsSystemDefaults()
    {
        var repoMock = new Mock<ITenantLimitsRepository>();
        var provider = CreateProvider(repoMock);

        var resolved = provider.GetSnapshot(Guid.NewGuid());

        resolved.RequestsPerMinute.Should().Be(120);
        repoMock.Verify(r => r.GetByTenantIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "GetSnapshot must never hit the database — it is cache-only by construction");
    }

    [Fact]
    public async Task GetSnapshot_AfterGetAsync_ReturnsCachedValue()
    {
        var tenantId = Guid.NewGuid();
        var row = TenantLimits.Create(tenantId, requestsPerMinute: 999, null, null, null, null, null, null);
        var repoMock = new Mock<ITenantLimitsRepository>();
        repoMock.Setup(r => r.GetByTenantIdAsync(tenantId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var provider = CreateProvider(repoMock);

        await provider.GetAsync(tenantId);
        var snapshot = provider.GetSnapshot(tenantId);

        snapshot.RequestsPerMinute.Should().Be(999);
    }
}
```

- [ ] **Step 10: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = prior baseline + 7).

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: add TenantLimits domain model, ITenantLimitsProvider, and system-wide defaults"
```

---

### Task 2: Unified rejection contract — `TenantLimitExceededException`, `RejectionEvent`, centralized `IExceptionHandler`, `GetRejectionsQuery`

**Files:**
- Create: `src/OrchestAI.Domain/Enums/RejectionReason.cs`
- Create: `src/OrchestAI.Domain/Entities/RejectionEvent.cs`
- Create: `src/OrchestAI.Domain/Interfaces/IRejectionEventRepository.cs`
- Create: `src/OrchestAI.Application/Exceptions/TenantLimitExceededException.cs`
- Create: `src/OrchestAI.Application/Queries/GetRejections/GetRejectionsQuery.cs`
- Create: `src/OrchestAI.Application/Queries/GetRejections/GetRejectionsResponse.cs`
- Create: `src/OrchestAI.Application/Queries/GetRejections/GetRejectionsHandler.cs`
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/RejectionEventConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/RejectionEventRepository.cs`
- Create: `src/OrchestAI.API/ExceptionHandling/RejectionResponder.cs`
- Create: `src/OrchestAI.API/ExceptionHandling/TenantLimitExceededExceptionHandler.cs`
- Create: `src/OrchestAI.API/Controllers/RejectionsController.cs`
- Modify: `src/OrchestAI.API/Middleware/TenantAuthenticationMiddleware.cs` (stash `ApiKeyId` into `HttpContext.Items`)
- Modify: `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, `src/OrchestAI.Infrastructure/DependencyInjection.cs`, `src/OrchestAI.API/Program.cs`
- Create: migration `AddRejectionEvents`
- Test: Create `tests/OrchestAI.Tests/Domain/RejectionEventTests.cs`
- Test: Create `tests/OrchestAI.Tests/Application/GetRejectionsHandlerTests.cs`
- Test: Create `tests/OrchestAI.Tests/API/TenantLimitExceededExceptionHandlerTests.cs`

**Interfaces:**
- Consumes (Task 1): `ITenantLimitsProvider` (not used directly here, but this task's exception/response contract is what Tasks 6-10 throw into).
- Produces: `RejectionReason { RateLimited, ConcurrencyExceeded, BudgetExceeded, AgentCapExceeded, QueueBackpressure }`; `RejectionEvent.Create(reason, requestId?, traceId?, apiKeyId?, detailsJson)`; `TenantLimitExceededException(Guid tenantId, RejectionReason reason, string message, int retryAfterSeconds, string detailsJson, string? traceId = null)` — every later task that rejects a synchronous HTTP request throws this; `IRejectionEventRepository.AddAsync/GetRecentAsync`; `RejectionResponder.RespondToRateLimitAsync(HttpContext, int retryAfterSeconds, CancellationToken)` (Task 9 calls this from `OnRejected`) and `.RespondToExceptionAsync(HttpContext, TenantLimitExceededException, CancellationToken)`.

**Investigation note driving this task's design:** `TenantAuthenticationMiddleware`'s `using (_tenantAccessor.SetTenant(tenant.Id))` unwinds (clears the ambient tenant) the moment an exception propagates back out of `next(context)` — and `UseExceptionHandler()` is registered *before* the tenant middleware in `Program.cs`, so by the time `IExceptionHandler.TryHandleAsync` runs, ambient tenant state is gone. `TenantLimitExceededException` therefore carries `TenantId` explicitly (captured by the throwing code, while ambient state was still valid), and `RejectionResponder` explicitly re-opens `_tenantAccessor.SetTenant(exception.TenantId)` around the `RejectionEvent` write so `TenantScopingInterceptor` can stamp it normally — no factory method needs to take `TenantId` as a parameter. The rate-limiter's `OnRejected` callback runs *inside* the live request (the limiter is placed after the tenant middleware — see Task 9), so ambient state is valid there and `RespondToRateLimitAsync` can read `ICurrentTenantAccessor.TenantId` directly.

- [ ] **Step 1: Write the failing domain test**

Create `tests/OrchestAI.Tests/Domain/RejectionEventTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class RejectionEventTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var apiKeyId = Guid.NewGuid();
        var evt = RejectionEvent.Create(RejectionReason.ConcurrencyExceeded, "req-1", "trace-1", apiKeyId, """{"limit":5}""");

        evt.Reason.Should().Be(RejectionReason.ConcurrencyExceeded);
        evt.RequestId.Should().Be("req-1");
        evt.TraceId.Should().Be("trace-1");
        evt.ApiKeyId.Should().Be(apiKeyId);
        evt.DetailsJson.Should().Be("""{"limit":5}""");
        evt.OccurredAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_NoTraceOrApiKey_AllowsNulls()
    {
        var evt = RejectionEvent.Create(RejectionReason.RateLimited, "req-2", null, null, "{}");

        evt.TraceId.Should().BeNull();
        evt.ApiKeyId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter RejectionEventTests`
Expected: FAIL (compile error — `RejectionEvent`/`RejectionReason` don't exist yet).

- [ ] **Step 3: Implement `RejectionReason` and `RejectionEvent`**

Create `src/OrchestAI.Domain/Enums/RejectionReason.cs`:

```csharp
namespace OrchestAI.Domain.Enums;

public enum RejectionReason
{
    RateLimited,
    ConcurrencyExceeded,
    BudgetExceeded,
    AgentCapExceeded,
    QueueBackpressure
}
```

Create `src/OrchestAI.Domain/Entities/RejectionEvent.cs`:

```csharp
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// A lightweight log of denials — deliberately separate from the full trace/cost event
// pipeline (AgentExecution/CostLedger), not an execution record. ITenantScoped: always created
// inside a live ambient-tenant scope (see RejectionResponder), never given TenantId directly.
public sealed class RejectionEvent : ITenantScoped
{
    private RejectionEvent() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public RejectionReason Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? RequestId { get; private set; }
    public string? TraceId { get; private set; }
    public Guid? ApiKeyId { get; private set; }
    public string DetailsJson { get; private set; } = "{}";

    public static RejectionEvent Create(
        RejectionReason reason, string? requestId, string? traceId, Guid? apiKeyId, string detailsJson)
    {
        return new RejectionEvent
        {
            Id = Guid.NewGuid(),
            Reason = reason,
            OccurredAt = DateTimeOffset.UtcNow,
            RequestId = requestId,
            TraceId = traceId,
            ApiKeyId = apiKeyId,
            DetailsJson = detailsJson
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter RejectionEventTests`
Expected: PASS (2/2).

- [ ] **Step 5: Repository interface, exception type, and query**

Create `src/OrchestAI.Domain/Interfaces/IRejectionEventRepository.cs`:

```csharp
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IRejectionEventRepository
{
    Task AddAsync(RejectionEvent rejectionEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RejectionEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
```

Create `src/OrchestAI.Application/Exceptions/TenantLimitExceededException.cs`:

```csharp
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Exceptions;

// Thrown by every synchronous enforcement point (admission's concurrency/budget checks — Task
// 6/7; queue backpressure — Task 10) that needs to reject an in-flight HTTP request with the
// unified 429 contract. Carries TenantId explicitly because the ambient ICurrentTenantAccessor
// scope has already unwound by the time global exception handling runs — see Task 2's
// investigation note. NOT used for AgentCapExceeded (Task 8), which happens inside a detached
// background dispatch with no HTTP response to write to, and is handled inline there instead.
public sealed class TenantLimitExceededException : Exception
{
    public Guid TenantId { get; }
    public RejectionReason Reason { get; }
    public int RetryAfterSeconds { get; }
    public string DetailsJson { get; }
    public string? TraceId { get; }

    public TenantLimitExceededException(
        Guid tenantId,
        RejectionReason reason,
        string message,
        int retryAfterSeconds,
        string detailsJson,
        string? traceId = null)
        : base(message)
    {
        TenantId = tenantId;
        Reason = reason;
        RetryAfterSeconds = retryAfterSeconds;
        DetailsJson = detailsJson;
        TraceId = traceId;
    }
}
```

Create `src/OrchestAI.Application/Queries/GetRejections/GetRejectionsQuery.cs`:

```csharp
using MediatR;

namespace OrchestAI.Application.Queries.GetRejections;

public sealed record GetRejectionsQuery(int Limit = 50) : IRequest<GetRejectionsResponse>;
```

Create `src/OrchestAI.Application/Queries/GetRejections/GetRejectionsResponse.cs`:

```csharp
namespace OrchestAI.Application.Queries.GetRejections;

public sealed record RejectionEntryDto(
    Guid Id,
    string Reason,
    DateTimeOffset OccurredAt,
    string? RequestId,
    string? TraceId,
    Guid? ApiKeyId,
    string DetailsJson);

public sealed record GetRejectionsResponse(IReadOnlyList<RejectionEntryDto> Rejections);
```

Create `src/OrchestAI.Application/Queries/GetRejections/GetRejectionsHandler.cs`:

```csharp
using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetRejections;

public sealed class GetRejectionsHandler : IRequestHandler<GetRejectionsQuery, GetRejectionsResponse>
{
    private readonly IRejectionEventRepository _repository;

    public GetRejectionsHandler(IRejectionEventRepository repository) => _repository = repository;

    public async Task<GetRejectionsResponse> Handle(GetRejectionsQuery request, CancellationToken cancellationToken)
    {
        var rejections = await _repository.GetRecentAsync(request.Limit, cancellationToken).ConfigureAwait(false);

        return new GetRejectionsResponse(rejections
            .Select(r => new RejectionEntryDto(
                r.Id, r.Reason.ToString(), r.OccurredAt, r.RequestId, r.TraceId, r.ApiKeyId, r.DetailsJson))
            .ToList());
    }
}
```

- [ ] **Step 6: Application-layer test**

Create `tests/OrchestAI.Tests/Application/GetRejectionsHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Queries.GetRejections;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetRejectionsHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsRepositoryResultsMappedToDto()
    {
        var rejection = RejectionEvent.Create(RejectionReason.RateLimited, "req-1", null, null, "{}");
        var repoMock = new Mock<IRejectionEventRepository>();
        repoMock.Setup(r => r.GetRecentAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RejectionEvent> { rejection });
        var handler = new GetRejectionsHandler(repoMock.Object);

        var response = await handler.Handle(new GetRejectionsQuery(50), CancellationToken.None);

        response.Rejections.Should().ContainSingle();
        response.Rejections[0].Reason.Should().Be("RateLimited");
        response.Rejections[0].RequestId.Should().Be("req-1");
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter GetRejectionsHandlerTests`
Expected: PASS (1/1).

- [ ] **Step 7: EF configuration and repository**

Create `src/OrchestAI.Infrastructure/Data/Configurations/RejectionEventConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class RejectionEventConfiguration : IEntityTypeConfiguration<RejectionEvent>
{
    public void Configure(EntityTypeBuilder<RejectionEvent> builder)
    {
        builder.ToTable("RejectionEvents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Create `src/OrchestAI.Infrastructure/Repositories/RejectionEventRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class RejectionEventRepository : IRejectionEventRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public RejectionEventRepository(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task AddAsync(RejectionEvent rejectionEvent, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.RejectionEvents.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RejectionEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.RejectionEvents
            .OrderByDescending(r => r.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
```

- [ ] **Step 8: `RejectionResponder` and `TenantLimitExceededExceptionHandler` (API layer)**

Create `src/OrchestAI.API/ExceptionHandling/RejectionResponder.cs`:

```csharp
using System.Text.Json;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.API.ExceptionHandling;

// The single place that builds every 429 response and writes the corresponding RejectionEvent —
// called from two entry points (the rate limiter's OnRejected callback, and
// TenantLimitExceededExceptionHandler) so there is exactly one place, not five, that decides
// what a rejection response looks like. See Global Constraints and ADR-015.
public sealed class RejectionResponder
{
    private readonly IRejectionEventRepository _rejectionEventRepository;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly ILogger<RejectionResponder> _logger;

    public RejectionResponder(
        IRejectionEventRepository rejectionEventRepository,
        ICurrentTenantAccessor tenantAccessor,
        ILogger<RejectionResponder> logger)
    {
        _rejectionEventRepository = rejectionEventRepository;
        _tenantAccessor = tenantAccessor;
        _logger = logger;
    }

    // Runs inside the live request's ambient tenant scope — UseRateLimiter() is registered
    // after TenantAuthenticationMiddleware (Task 9), so ICurrentTenantAccessor.TenantId is valid.
    public async Task RespondToRateLimitAsync(HttpContext context, int retryAfterSeconds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantAccessor.TenantId
            ?? throw new InvalidOperationException(
                "Rate limiter rejected a request with no ambient tenant — UseRateLimiter() must run after TenantAuthenticationMiddleware.");
        var apiKeyId = context.Items.TryGetValue("ApiKeyId", out var raw) && raw is Guid id ? id : (Guid?)null;
        var detailsJson = JsonSerializer.Serialize(new { limit = "requests_per_minute", retryAfterSeconds });

        await WriteAsync(
            context, tenantId, RejectionReason.RateLimited, "Rate limit exceeded.",
            retryAfterSeconds, detailsJson, apiKeyId, traceId: null, cancellationToken).ConfigureAwait(false);
    }

    // The ambient tenant scope has already unwound by the time global exception handling runs
    // (see Task 2's investigation note) — TenantId comes from the exception, and the scope is
    // explicitly re-opened just long enough for the RejectionEvent write to be stamped normally.
    public async Task RespondToExceptionAsync(
        HttpContext context, TenantLimitExceededException exception, CancellationToken cancellationToken)
    {
        using (_tenantAccessor.SetTenant(exception.TenantId))
        {
            await WriteAsync(
                context, exception.TenantId, exception.Reason, exception.Message,
                exception.RetryAfterSeconds, exception.DetailsJson, apiKeyId: null, exception.TraceId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(
        HttpContext context,
        Guid tenantId,
        RejectionReason reason,
        string detail,
        int retryAfterSeconds,
        string detailsJson,
        Guid? apiKeyId,
        string? traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rejectionEvent = RejectionEvent.Create(reason, context.TraceIdentifier, traceId, apiKeyId, detailsJson);
            await _rejectionEventRepository.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Logging the rejection must never prevent the caller from receiving the 429 itself.
            _logger.LogWarning(ex, "Failed to persist RejectionEvent for tenant {TenantId}, reason {Reason}", tenantId, reason);
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        context.Response.ContentType = "application/problem+json";

        var body = new
        {
            type = "https://orchestai/problems/rate-limited",
            title = "Too Many Requests",
            status = 429,
            detail,
            reason = reason.ToString(),
            retryAfterSeconds
        };

        await context.Response.WriteAsJsonAsync(body, cancellationToken).ConfigureAwait(false);
    }
}
```

Create `src/OrchestAI.API/ExceptionHandling/TenantLimitExceededExceptionHandler.cs`:

```csharp
using Microsoft.AspNetCore.Diagnostics;
using OrchestAI.Application.Exceptions;

namespace OrchestAI.API.ExceptionHandling;

// Single choke point for every synchronous-HTTP-request rejection thrown as an exception
// (ConcurrencyExceeded, BudgetExceeded, QueueBackpressure). RateLimited is handled separately
// by the rate limiter's own OnRejected callback (not an exception — see Task 9).
// AgentCapExceeded is handled inline inside the background dispatch (Task 8), since it has no
// HTTP response in flight to write to. Composes with the existing
// UseExceptionHandler()/AddProblemDetails() already configured in Program.cs.
public sealed class TenantLimitExceededExceptionHandler : IExceptionHandler
{
    private readonly RejectionResponder _responder;

    public TenantLimitExceededExceptionHandler(RejectionResponder responder) => _responder = responder;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not TenantLimitExceededException tle)
            return false;

        await _responder.RespondToExceptionAsync(httpContext, tle, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
```

Create `src/OrchestAI.API/Controllers/RejectionsController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Queries.GetRejections;

namespace OrchestAI.API.Controllers;

[ApiController]
[Route("api/v1/rejections")]
[Produces("application/json")]
public sealed class RejectionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public RejectionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Recent rate-limit/concurrency/budget/agent-cap/queue rejections for the caller's tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetRejectionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] int limit, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetRejectionsQuery(limit <= 0 ? 50 : limit), cancellationToken);
        return Ok(response);
    }
}
```

- [ ] **Step 9: API-layer test for the exception handler**

Create `tests/OrchestAI.Tests/API/TenantLimitExceededExceptionHandlerTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.API.ExceptionHandling;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.API;

public sealed class TenantLimitExceededExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_OtherExceptionType_ReturnsFalseAndWritesNothing()
    {
        var repoMock = new Mock<IRejectionEventRepository>();
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var responder = new RejectionResponder(repoMock.Object, accessor, NullLogger<RejectionResponder>.Instance);
        var handler = new TenantLimitExceededExceptionHandler(responder);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("nope"), CancellationToken.None);

        handled.Should().BeFalse();
        repoMock.Verify(r => r.AddAsync(It.IsAny<RejectionEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryHandleAsync_TenantLimitExceededException_Writes429WithRetryAfterAndReason()
    {
        var repoMock = new Mock<IRejectionEventRepository>();
        RejectionEvent? captured = null;
        repoMock.Setup(r => r.AddAsync(It.IsAny<RejectionEvent>(), It.IsAny<CancellationToken>()))
            .Callback<RejectionEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var responder = new RejectionResponder(repoMock.Object, accessor, NullLogger<RejectionResponder>.Instance);
        var handler = new TenantLimitExceededExceptionHandler(responder);

        var tenantId = Guid.NewGuid();
        var body = new MemoryStream();
        var context = new DefaultHttpContext { Response = { Body = body } };
        var exception = new TenantLimitExceededException(
            tenantId, RejectionReason.BudgetExceeded, "Daily budget exceeded.", retryAfterSeconds: 3600,
            detailsJson: """{"limit":50,"actual":52.10}""", traceId: "trace-123");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.Headers.RetryAfter.ToString().Should().Be("3600");

        body.Seek(0, SeekOrigin.Begin);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("reason").GetString().Should().Be("BudgetExceeded");
        doc.RootElement.GetProperty("retryAfterSeconds").GetInt32().Should().Be(3600);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tenantId,
            "the exception's captured TenantId must be used, not ambient state, which has already unwound by exception-handling time");
        captured.Reason.Should().Be(RejectionReason.BudgetExceeded);
        captured.TraceId.Should().Be("trace-123");
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter TenantLimitExceededExceptionHandlerTests`
Expected: PASS (2/2).

- [ ] **Step 10: Wire into `AppDbContext`, DI, and `Program.cs`**

In `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, add after `DbSet<TenantLimits>`:

```csharp
    public DbSet<RejectionEvent> RejectionEvents => Set<RejectionEvent>();
```

And in `OnModelCreating`, after `modelBuilder.ApplyConfiguration(new TenantLimitsConfiguration());`:

```csharp
        modelBuilder.ApplyConfiguration(new RejectionEventConfiguration());
```

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, after `services.AddSingleton<ITenantLimitsProvider, EfTenantLimitsProvider>();`:

```csharp
        services.AddScoped<IRejectionEventRepository, RejectionEventRepository>();
```

In `src/OrchestAI.API/Middleware/TenantAuthenticationMiddleware.cs`, in `InvokeAsync`, right before `await MaybeRecordUsageAsync(apiKey, context.RequestAborted).ConfigureAwait(false);`, add:

```csharp
        context.Items["ApiKeyId"] = apiKey.Id;
```

In `src/OrchestAI.API/Program.cs`, add after `builder.Services.AddScoped<TenantAuthenticationMiddleware>();`:

```csharp
    // API-layer rejection contract (Task 2) — RejectionResponder builds every 429 response and
    // RejectionEvent write; TenantLimitExceededExceptionHandler is the choke point for every
    // synchronous-HTTP-request rejection thrown as an exception.
    builder.Services.AddScoped<RejectionResponder>();
    builder.Services.AddExceptionHandler<TenantLimitExceededExceptionHandler>();
```

Add the using at the top of `Program.cs`:

```csharp
using OrchestAI.API.ExceptionHandling;
```

`AddProblemDetails()` is already called later in the file — no change needed there; `AddExceptionHandler<T>()` composes with it automatically (registered handlers run in registration order before the `ProblemDetails` fallback).

- [ ] **Step 11: Generate the migration**

Run: `cd src/OrchestAI.Infrastructure && dotnet ef migrations add AddRejectionEvents --startup-project ../OrchestAI.API`
Expected: creates the migration files and updates the model snapshot. Inspect `Up()` — confirm it creates table `RejectionEvents` with columns matching `RejectionEventConfiguration` and a composite index on `(TenantId, OccurredAt)`.

- [ ] **Step 12: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 1's count + 6).

- [ ] **Step 13: Commit**

```bash
git add -A
git commit -m "feat: add unified rejection contract (TenantLimitExceededException, RejectionEvent, centralized exception handler)"
```

---

### Task 3: `SetTenantLimitsCommand` (admin-only) and `AdminController` wiring

**Files:**
- Create: `src/OrchestAI.Application/Commands/SetTenantLimits/SetTenantLimitsCommand.cs`
- Create: `src/OrchestAI.Application/Commands/SetTenantLimits/SetTenantLimitsResponse.cs`
- Create: `src/OrchestAI.Application/Commands/SetTenantLimits/SetTenantLimitsHandler.cs`
- Modify: `src/OrchestAI.API/Controllers/AdminController.cs`
- Test: Create `tests/OrchestAI.Tests/Application/SetTenantLimitsHandlerTests.cs`

**Interfaces:**
- Consumes: `ITenantLimitsRepository` (Task 1), `ITenantRepository` (Week 10, existing).
- Produces: `SetTenantLimitsCommand(Guid TenantId, int? RequestsPerMinute, int? MaxConcurrentTasks, int? MaxAgentsPerTask, int? MaxToolCallsPerTask, decimal? DailyCostBudgetUsd, decimal? MonthlyCostBudgetUsd, int? MaxQueueDepth)`; `SetTenantLimitsResponse` (same 7 fields + `TenantId`/`UpdatedAt`).

- [ ] **Step 1: Write the failing handler tests**

Create `tests/OrchestAI.Tests/Application/SetTenantLimitsHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Commands.SetTenantLimits;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class SetTenantLimitsHandlerTests
{
    [Fact]
    public async Task Handle_TenantDoesNotExist_ThrowsNotFoundException()
    {
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);
        var limitsRepoMock = new Mock<ITenantLimitsRepository>();
        var handler = new SetTenantLimitsHandler(limitsRepoMock.Object, tenantRepoMock.Object);

        var act = () => handler.Handle(
            new SetTenantLimitsCommand(Guid.NewGuid(), null, null, null, null, null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NoExistingRow_CreatesNewTenantLimits()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var limitsRepoMock = new Mock<ITenantLimitsRepository>();
        limitsRepoMock.Setup(r => r.GetByTenantIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync((TenantLimits?)null);
        TenantLimits? saved = null;
        limitsRepoMock.Setup(r => r.UpsertAsync(It.IsAny<TenantLimits>(), It.IsAny<CancellationToken>()))
            .Callback<TenantLimits, CancellationToken>((l, _) => saved = l)
            .Returns(Task.CompletedTask);
        var handler = new SetTenantLimitsHandler(limitsRepoMock.Object, tenantRepoMock.Object);

        var response = await handler.Handle(
            new SetTenantLimitsCommand(tenant.Id, 200, 10, null, null, 100m, null, null), CancellationToken.None);

        response.RequestsPerMinute.Should().Be(200);
        saved.Should().NotBeNull();
        saved!.TenantId.Should().Be(tenant.Id);
        saved.MaxConcurrentTasks.Should().Be(10);
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesInPlace()
    {
        var tenant = Tenant.Create("Acme", "acme");
        var existing = TenantLimits.Create(tenant.Id, 50, null, null, null, null, null, null);
        var tenantRepoMock = new Mock<ITenantRepository>();
        tenantRepoMock.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var limitsRepoMock = new Mock<ITenantLimitsRepository>();
        limitsRepoMock.Setup(r => r.GetByTenantIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var handler = new SetTenantLimitsHandler(limitsRepoMock.Object, tenantRepoMock.Object);

        var response = await handler.Handle(
            new SetTenantLimitsCommand(tenant.Id, 300, null, null, null, null, null, null), CancellationToken.None);

        response.RequestsPerMinute.Should().Be(300);
        limitsRepoMock.Verify(r => r.UpsertAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter SetTenantLimitsHandlerTests`
Expected: FAIL (compile error — command/handler don't exist yet).

- [ ] **Step 3: Implement the command, response, and handler**

Create `src/OrchestAI.Application/Commands/SetTenantLimits/SetTenantLimitsCommand.cs`:

```csharp
using MediatR;

namespace OrchestAI.Application.Commands.SetTenantLimits;

public sealed record SetTenantLimitsCommand(
    Guid TenantId,
    int? RequestsPerMinute,
    int? MaxConcurrentTasks,
    int? MaxAgentsPerTask,
    int? MaxToolCallsPerTask,
    decimal? DailyCostBudgetUsd,
    decimal? MonthlyCostBudgetUsd,
    int? MaxQueueDepth
) : IRequest<SetTenantLimitsResponse>;
```

Create `src/OrchestAI.Application/Commands/SetTenantLimits/SetTenantLimitsResponse.cs`:

```csharp
namespace OrchestAI.Application.Commands.SetTenantLimits;

public sealed record SetTenantLimitsResponse(
    Guid TenantId,
    int? RequestsPerMinute,
    int? MaxConcurrentTasks,
    int? MaxAgentsPerTask,
    int? MaxToolCallsPerTask,
    decimal? DailyCostBudgetUsd,
    decimal? MonthlyCostBudgetUsd,
    int? MaxQueueDepth,
    DateTimeOffset UpdatedAt);
```

Create `src/OrchestAI.Application/Commands/SetTenantLimits/SetTenantLimitsHandler.cs`:

```csharp
using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.SetTenantLimits;

// Admin/bootstrap-only — same access pattern as Week 10's CreateTenantCommand/CreateApiKeyCommand,
// reachable only through AdminController's RequireAdminSecretFilter gate, never a tenant key.
public sealed class SetTenantLimitsHandler : IRequestHandler<SetTenantLimitsCommand, SetTenantLimitsResponse>
{
    private readonly ITenantLimitsRepository _limitsRepository;
    private readonly ITenantRepository _tenantRepository;

    public SetTenantLimitsHandler(ITenantLimitsRepository limitsRepository, ITenantRepository tenantRepository)
    {
        _limitsRepository = limitsRepository;
        _tenantRepository = tenantRepository;
    }

    public async Task<SetTenantLimitsResponse> Handle(SetTenantLimitsCommand request, CancellationToken cancellationToken)
    {
        _ = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

        var existing = await _limitsRepository.GetByTenantIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false);

        TenantLimits limits;
        if (existing is null)
        {
            limits = TenantLimits.Create(
                request.TenantId, request.RequestsPerMinute, request.MaxConcurrentTasks, request.MaxAgentsPerTask,
                request.MaxToolCallsPerTask, request.DailyCostBudgetUsd, request.MonthlyCostBudgetUsd,
                request.MaxQueueDepth);
        }
        else
        {
            existing.Update(
                request.RequestsPerMinute, request.MaxConcurrentTasks, request.MaxAgentsPerTask,
                request.MaxToolCallsPerTask, request.DailyCostBudgetUsd, request.MonthlyCostBudgetUsd,
                request.MaxQueueDepth);
            limits = existing;
        }

        await _limitsRepository.UpsertAsync(limits, cancellationToken).ConfigureAwait(false);

        return new SetTenantLimitsResponse(
            limits.TenantId, limits.RequestsPerMinute, limits.MaxConcurrentTasks, limits.MaxAgentsPerTask,
            limits.MaxToolCallsPerTask, limits.DailyCostBudgetUsd, limits.MonthlyCostBudgetUsd, limits.MaxQueueDepth,
            limits.UpdatedAt);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter SetTenantLimitsHandlerTests`
Expected: PASS (3/3).

- [ ] **Step 5: Wire into `AdminController`**

In `src/OrchestAI.API/Controllers/AdminController.cs`, add the using:

```csharp
using OrchestAI.Application.Commands.SetTenantLimits;
```

Add a new action (after `SuspendTenantAsync`):

```csharp
    [HttpPut("tenants/{tenantId:guid}/limits")]
    [ProducesResponseType(typeof(SetTenantLimitsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetTenantLimitsAsync(
        Guid tenantId, [FromBody] SetTenantLimitsRequestBody body, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(
                new SetTenantLimitsCommand(
                    tenantId, body.RequestsPerMinute, body.MaxConcurrentTasks, body.MaxAgentsPerTask,
                    body.MaxToolCallsPerTask, body.DailyCostBudgetUsd, body.MonthlyCostBudgetUsd, body.MaxQueueDepth),
                cancellationToken);
            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
    }
```

Add the request-body record at the bottom of the file, alongside the class closing brace (matching `ApprovalRequest`'s placement pattern in `TasksController.cs`):

```csharp
public sealed record SetTenantLimitsRequestBody(
    int? RequestsPerMinute,
    int? MaxConcurrentTasks,
    int? MaxAgentsPerTask,
    int? MaxToolCallsPerTask,
    decimal? DailyCostBudgetUsd,
    decimal? MonthlyCostBudgetUsd,
    int? MaxQueueDepth);
```

- [ ] **Step 6: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 2's count + 3).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add SetTenantLimitsCommand and admin endpoint for per-tenant limit overrides"
```

---

### Task 4: `IdempotencyRecord` and `POST /tasks` idempotency

**Files:**
- Create: `src/OrchestAI.Domain/Entities/IdempotencyRecord.cs`
- Create: `src/OrchestAI.Domain/Interfaces/IIdempotencyRecordRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/IdempotencyRecordConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/IdempotencyRecordRepository.cs`
- Modify: `src/OrchestAI.Application/Commands/CreateOrchestrationTask/CreateOrchestrationTaskCommand.cs`
- Modify: `src/OrchestAI.Application/Commands/CreateOrchestrationTask/CreateOrchestrationTaskHandler.cs`
- Modify: `src/OrchestAI.API/Controllers/TasksController.cs` (`CreateAsync`: read `Idempotency-Key` header)
- Modify: `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, `src/OrchestAI.Infrastructure/DependencyInjection.cs`
- Create: migration `AddIdempotencyRecords`
- Test: Create `tests/OrchestAI.Tests/Application/CreateOrchestrationTaskIdempotencyTests.cs`

**Interfaces:**
- Consumes (Task 2): `AbuseProtectionOptions.IdempotencyKeyTtlHours`; `ConflictException` (existing, Week 10).
- Produces: `IdempotencyRecord.Create(idempotencyKey, taskId, requestPayloadHash, ttl)`; `IIdempotencyRecordRepository.GetByKeyAsync/AddAsync`; `CreateOrchestrationTaskCommand` gains an optional `string? IdempotencyKey = null` trailing parameter (backward-compatible with every existing caller).

**Design note:** idempotency applies to `POST /tasks` only (not `/start`) — confirmed in brainstorming: `/start`'s existing `Pending`-state guard, hardened to an atomic CAS in Task 6, already makes a retried `/start` call safe (either 409, or a genuinely-still-`Pending` task starts exactly once) without a second idempotency mechanism. A known, accepted scope limit: two genuinely *concurrent* (not sequential-retry) `POST /tasks` calls with the same brand-new key could both pass the "not found" check before either inserts, racing past the unique `(TenantId, IdempotencyKey)` index into a raw `DbUpdateException` — acceptable here (unlike the budget/admission race) because the failure mode is "one extra valid task created," not a security or budget-overshoot problem; documented in ADR-015 rather than engineered around this week.

- [ ] **Step 1: Write the failing idempotency tests**

Create `tests/OrchestAI.Tests/Application/CreateOrchestrationTaskIdempotencyTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class CreateOrchestrationTaskIdempotencyTests
{
    private static CreateOrchestrationTaskHandler CreateHandler(
        Mock<IOrchestrationTaskRepository> taskRepoMock, Mock<IIdempotencyRecordRepository> idempotencyRepoMock)
    {
        return new CreateOrchestrationTaskHandler(
            taskRepoMock.Object,
            idempotencyRepoMock.Object,
            Options.Create(new AbuseProtectionOptions { IdempotencyKeyTtlHours = 24 }),
            NullLogger<CreateOrchestrationTaskHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NoIdempotencyKey_NeverTouchesIdempotencyRepository()
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        await handler.Handle(
            new CreateOrchestrationTaskCommand(Guid.NewGuid(), "T", "P", false, null), CancellationToken.None);

        idempotencyRepoMock.Verify(r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        idempotencyRepoMock.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewIdempotencyKey_CreatesTaskAndRecord()
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        idempotencyRepoMock.Setup(r => r.GetByKeyAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        var response = await handler.Handle(
            new CreateOrchestrationTaskCommand(Guid.NewGuid(), "T", "P", false, "key-1"), CancellationToken.None);

        taskRepoMock.Verify(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotencyRepoMock.Verify(r => r.AddAsync(
            It.Is<IdempotencyRecord>(rec => rec.IdempotencyKey == "key-1" && rec.TaskId == response.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RepeatedKeySamePayload_ReturnsOriginalTaskWithoutCreatingANewOne()
    {
        var originalTask = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var userId = originalTask.UserId;
        var existingRecord = IdempotencyRecord.Create("key-1", originalTask.Id, ComputeHash(userId, "T", "P", false), TimeSpan.FromHours(24));

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(originalTask.Id, It.IsAny<CancellationToken>())).ReturnsAsync(originalTask);
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        idempotencyRepoMock.Setup(r => r.GetByKeyAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync(existingRecord);
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        var response = await handler.Handle(
            new CreateOrchestrationTaskCommand(userId, "T", "P", false, "key-1"), CancellationToken.None);

        response.Id.Should().Be(originalTask.Id);
        taskRepoMock.Verify(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()), Times.Never,
            "a repeated idempotency key with the same payload must never create a second task");
    }

    [Fact]
    public async Task Handle_RepeatedKeyDifferentPayload_ThrowsConflictException()
    {
        var originalTask = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var existingRecord = IdempotencyRecord.Create(
            "key-1", originalTask.Id, ComputeHash(originalTask.UserId, "T", "P", false), TimeSpan.FromHours(24));

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        idempotencyRepoMock.Setup(r => r.GetByKeyAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync(existingRecord);
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        var act = () => handler.Handle(
            new CreateOrchestrationTaskCommand(originalTask.UserId, "Different Title", "P", false, "key-1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        taskRepoMock.Verify(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Mirrors CreateOrchestrationTaskHandler.ComputeRequestHash exactly — kept in sync manually
    // since the handler's version is private; a divergence here would make this test lie.
    private static string ComputeHash(Guid userId, string title, string prompt, bool requireApproval)
    {
        var canonical = $"{userId}|{title}|{prompt}|{requireApproval}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter CreateOrchestrationTaskIdempotencyTests`
Expected: FAIL (compile error — `IdempotencyRecord`, updated handler constructor, and the new command parameter don't exist yet).

- [ ] **Step 3: Implement `IdempotencyRecord` and its repository interface**

Create `src/OrchestAI.Domain/Entities/IdempotencyRecord.cs`:

```csharp
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// ITenantScoped — always created inside a live ambient-tenant scope (the CreateOrchestrationTask
// request), never given TenantId directly. A key is only unique within one tenant's own scope.
public sealed class IdempotencyRecord : ITenantScoped
{
    private IdempotencyRecord() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid TaskId { get; private set; }
    public string RequestPayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyRecord Create(string idempotencyKey, Guid taskId, string requestPayloadHash, TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            TaskId = taskId,
            RequestPayloadHash = requestPayloadHash,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl)
        };
    }
}
```

Create `src/OrchestAI.Domain/Interfaces/IIdempotencyRecordRepository.cs`:

```csharp
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IIdempotencyRecordRepository
{
    // Returns null for a missing OR expired record — an expired key is functionally absent,
    // free to be reused for a brand-new task.
    Task<IdempotencyRecord?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Update `CreateOrchestrationTaskCommand` and `CreateOrchestrationTaskHandler`**

Modify `src/OrchestAI.Application/Commands/CreateOrchestrationTask/CreateOrchestrationTaskCommand.cs`:

```csharp
using MediatR;

namespace OrchestAI.Application.Commands.CreateOrchestrationTask;

public sealed record CreateOrchestrationTaskCommand(
    Guid UserId,
    string Title,
    string UserPrompt,
    bool RequireApproval = false,
    string? IdempotencyKey = null
) : IRequest<CreateOrchestrationTaskResponse>;
```

Replace `src/OrchestAI.Application/Commands/CreateOrchestrationTask/CreateOrchestrationTaskHandler.cs` in full:

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateOrchestrationTask;

public sealed class CreateOrchestrationTaskHandler
    : IRequestHandler<CreateOrchestrationTaskCommand, CreateOrchestrationTaskResponse>
{
    private readonly IOrchestrationTaskRepository _repository;
    private readonly IIdempotencyRecordRepository _idempotencyRepository;
    private readonly IOptions<AbuseProtectionOptions> _abuseProtectionOptions;
    private readonly ILogger<CreateOrchestrationTaskHandler> _logger;

    public CreateOrchestrationTaskHandler(
        IOrchestrationTaskRepository repository,
        IIdempotencyRecordRepository idempotencyRepository,
        IOptions<AbuseProtectionOptions> abuseProtectionOptions,
        ILogger<CreateOrchestrationTaskHandler> logger)
    {
        _repository = repository;
        _idempotencyRepository = idempotencyRepository;
        _abuseProtectionOptions = abuseProtectionOptions;
        _logger = logger;
    }

    public async Task<CreateOrchestrationTaskResponse> Handle(
        CreateOrchestrationTaskCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var requestHash = ComputeRequestHash(request);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _idempotencyRepository
                .GetByKeyAsync(request.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                if (existing.RequestPayloadHash != requestHash)
                    throw new ConflictException(
                        $"Idempotency-Key '{request.IdempotencyKey}' was already used with a different request payload.");

                var originalTask = await _repository.GetByIdAsync(existing.TaskId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(OrchestrationTask), existing.TaskId);

                _logger.LogInformation(
                    "Idempotency-Key {IdempotencyKey} matched an existing task {TaskId} — returning it unchanged",
                    request.IdempotencyKey, originalTask.Id);

                return ToResponse(originalTask);
            }
        }

        var task = OrchestrationTask.Create(
            request.UserId,
            request.Title,
            request.UserPrompt,
            request.RequireApproval);

        await _repository.AddAsync(task, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var record = IdempotencyRecord.Create(
                request.IdempotencyKey, task.Id, requestHash,
                TimeSpan.FromHours(_abuseProtectionOptions.Value.IdempotencyKeyTtlHours));
            await _idempotencyRepository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Created orchestration task {TaskId} for user {UserId}",
            task.Id, task.UserId);

        return ToResponse(task);
    }

    private static CreateOrchestrationTaskResponse ToResponse(OrchestrationTask task) => new(
        task.Id, task.UserId, task.Title, task.Status.ToString(), task.RequireApproval, task.CreatedAt);

    // Deliberately excludes IdempotencyKey itself from the hash — the key is the lookup handle,
    // not part of "what was requested." Kept in sync manually with the test-side copy in
    // CreateOrchestrationTaskIdempotencyTests — a divergence there would make that test lie.
    private static string ComputeRequestHash(CreateOrchestrationTaskCommand request)
    {
        var canonical = $"{request.UserId}|{request.Title}|{request.UserPrompt}|{request.RequireApproval}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static void Validate(CreateOrchestrationTaskCommand request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.UserId == Guid.Empty)
            errors[nameof(request.UserId)] = ["UserId must not be empty."];

        if (string.IsNullOrWhiteSpace(request.Title))
            errors[nameof(request.Title)] = ["Title is required."];
        else if (request.Title.Length > 500)
            errors[nameof(request.Title)] = ["Title must not exceed 500 characters."];

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
            errors[nameof(request.UserPrompt)] = ["UserPrompt is required."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter CreateOrchestrationTaskIdempotencyTests`
Expected: PASS (4/4).

- [ ] **Step 6: EF configuration, repository, and wiring**

Create `src/OrchestAI.Infrastructure/Data/Configurations/IdempotencyRecordConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(255);
        builder.Property(x => x.RequestPayloadHash).HasMaxLength(64);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Create `src/OrchestAI.Infrastructure/Repositories/IdempotencyRecordRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class IdempotencyRecordRepository : IIdempotencyRecordRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public IdempotencyRecordRepository(IDbContextFactory<AppDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<IdempotencyRecord?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.IdempotencyRecords
            .Where(r => r.IdempotencyKey == idempotencyKey && r.ExpiresAt > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.IdempotencyRecords.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

In `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, add after `DbSet<RejectionEvent>`:

```csharp
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
```

And in `OnModelCreating`, after the `RejectionEventConfiguration` line:

```csharp
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
```

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, after `services.AddScoped<IRejectionEventRepository, RejectionEventRepository>();`:

```csharp
        services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();
```

And after `services.Configure<AbuseProtectionOptions>(configuration.GetSection(AbuseProtectionOptions.SectionName));` (already registered in Task 1 — no duplicate needed, `AbuseProtectionOptions` just moved namespace, the `Configure<T>` call and `appsettings.json` section are unaffected).

- [ ] **Step 7: Wire the `Idempotency-Key` header into `TasksController`**

In `src/OrchestAI.API/Controllers/TasksController.cs`, replace `CreateAsync`:

```csharp
    /// <summary>Creates a new orchestration task. Supports an optional Idempotency-Key header.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrchestrationTaskResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrchestrationTaskCommand bodyCommand,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = bodyCommand with { IdempotencyKey = idempotencyKey };

        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction("GetById", new { id = response.Id }, response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for CreateOrchestrationTask: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails { Title = "Conflict", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
        }
    }
```

- [ ] **Step 8: Generate the migration**

Run: `cd src/OrchestAI.Infrastructure && dotnet ef migrations add AddIdempotencyRecords --startup-project ../OrchestAI.API`
Expected: creates table `IdempotencyRecords` with a unique index on `(TenantId, IdempotencyKey)`.

- [ ] **Step 9: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 3's count + 4). Confirm no existing `CreateOrchestrationTaskCommand` call site broke — the new parameter is trailing and optional, so this should be a non-breaking change; grep for `new CreateOrchestrationTaskCommand(` across `tests/` to confirm every existing call site still compiles unchanged.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add Idempotency-Key support to POST /tasks"
```

---

### Task 5: `TaskAdmissionReservation`, `ITaskAdmissionReservationRepository`, and `IBudgetEstimator`

**Files:**
- Create: `src/OrchestAI.Domain/Entities/TaskAdmissionReservation.cs`
- Create: `src/OrchestAI.Domain/Interfaces/ITaskAdmissionReservationRepository.cs`
- Create: `src/OrchestAI.Domain/Interfaces/IBudgetEstimator.cs`
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/TaskAdmissionReservationConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/TaskAdmissionReservationRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Budgeting/ConservativeBudgetEstimator.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, `src/OrchestAI.Infrastructure/DependencyInjection.cs`
- Create: migration `AddTaskAdmissionReservations`
- Test: Create `tests/OrchestAI.Tests/Domain/TaskAdmissionReservationTests.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/ConservativeBudgetEstimatorTests.cs`

**Interfaces:**
- Consumes (Task 1): `ResolvedTenantLimits`. Consumes (Task 2 layering fix): `AbuseProtectionOptions.AssumedCostPerToolCallUsd`.
- Produces: `TaskAdmissionReservation.Create(taskId, reservedCostUsd)` (no `TenantId` parameter — stamped by `TenantScopingInterceptor` from ambient scope, same rule as every other `ITenantScoped` factory created inside a live request); `ITaskAdmissionReservationRepository.GetByTaskIdAsync/ReleaseAsync`; `IBudgetEstimator.EstimateWorstCaseCostAsync(ResolvedTenantLimits, CancellationToken)`.

**Design note — this is pure operational state (`DESIGN_PRINCIPLES.md` "Operational state vs. audit state"):** `TaskAdmissionReservation` is never read by, written into, or derived from `CostLedger`/`CostRollup`. `ReleaseAsync` is a hard `DELETE`, never an adjustment written anywhere else. A reservation whose owning task crashes mid-execution (so the `finally` in Task 7 never runs) is simply never released — it physically remains in the table, but Task 6's admission math excludes any reservation older than `AbuseProtectionOptions.ReservationStalenessMinutes` from its count/sum, so it stops affecting future admissions once stale. This plan deliberately does not build a background sweep to delete stale orphaned rows — an accepted, documented limitation of the current single-instance architecture (ADR-015), not an oversight.

- [ ] **Step 1: Write the failing domain test**

Create `tests/OrchestAI.Tests/Domain/TaskAdmissionReservationTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Tests.Domain;

public sealed class TaskAdmissionReservationTests
{
    [Fact]
    public void Create_SetsTaskIdAndReservedCost()
    {
        var taskId = Guid.NewGuid();

        var reservation = TaskAdmissionReservation.Create(taskId, 12.50m);

        reservation.TaskId.Should().Be(taskId);
        reservation.ReservedCostUsd.Should().Be(12.50m);
        reservation.CreatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter TaskAdmissionReservationTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `TaskAdmissionReservation`, its repository interface, and `IBudgetEstimator`**

Create `src/OrchestAI.Domain/Entities/TaskAdmissionReservation.cs`:

```csharp
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// Operational state, not audit state (see DESIGN_PRINCIPLES.md "Operational state vs. audit
// state") — a temporary capacity hold covering both the concurrency slot and the budget
// reservation for one in-flight task admission. TaskId is the primary key (1:1 with
// OrchestrationTask, see TaskAdmissionReservationConfiguration). Released in full (deleted)
// when the task reaches a terminal state; a row surviving past
// AbuseProtectionOptions.ReservationStalenessMinutes is excluded from admission math (crash
// recovery — see ADR-015). ITenantScoped: always created inside the live ambient-tenant scope
// of the /start request, never given TenantId directly.
public sealed class TaskAdmissionReservation : ITenantScoped
{
    private TaskAdmissionReservation() { }

    public Guid TaskId { get; private set; }
    public Guid TenantId { get; private set; }
    public decimal ReservedCostUsd { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static TaskAdmissionReservation Create(Guid taskId, decimal reservedCostUsd)
    {
        return new TaskAdmissionReservation
        {
            TaskId = taskId,
            ReservedCostUsd = reservedCostUsd,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

Create `src/OrchestAI.Domain/Interfaces/ITaskAdmissionReservationRepository.cs`:

```csharp
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

// Plain reads and release — the atomic admission WRITE (CAS + concurrency/budget check +
// insert, all in one transaction) lives on IOrchestrationAdmissionRepository (Task 6), not
// here, because that atomicity only holds if every step of it shares one transaction.
public interface ITaskAdmissionReservationRepository
{
    Task<TaskAdmissionReservation?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);

    // Idempotent: deleting an already-released (or never-existing) reservation is a no-op, not
    // an error — Task 7's try/finally must be safe to call this on every exit path without
    // first checking whether admission actually succeeded.
    Task ReleaseAsync(Guid taskId, CancellationToken cancellationToken = default);
}
```

Create `src/OrchestAI.Domain/Interfaces/IBudgetEstimator.cs`:

```csharp
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// Isolates the worst-case cost estimate used for budget reservation behind one seam — see
// ADR-015 confirmation #8. Deliberately conservative for this week (ConservativeBudgetEstimator
// over-reserves rather than risks overspend); a smarter estimator can replace it later without
// touching admission logic, since every caller depends only on this interface.
public interface IBudgetEstimator
{
    Task<decimal> EstimateWorstCaseCostAsync(ResolvedTenantLimits limits, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter TaskAdmissionReservationTests`
Expected: PASS (1/1).

- [ ] **Step 5: `ConservativeBudgetEstimator` and its test**

Create `src/OrchestAI.Infrastructure/Budgeting/ConservativeBudgetEstimator.cs`:

```csharp
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Budgeting;

public sealed class ConservativeBudgetEstimator : IBudgetEstimator
{
    private readonly decimal _assumedCostPerToolCallUsd;

    public ConservativeBudgetEstimator(IOptions<AbuseProtectionOptions> options)
        => _assumedCostPerToolCallUsd = options.Value.AssumedCostPerToolCallUsd;

    public Task<decimal> EstimateWorstCaseCostAsync(ResolvedTenantLimits limits, CancellationToken cancellationToken = default)
    {
        var estimate = limits.MaxAgentsPerTask * limits.MaxToolCallsPerTask * _assumedCostPerToolCallUsd;
        return Task.FromResult(estimate);
    }
}
```

Create `tests/OrchestAI.Tests/Infrastructure/ConservativeBudgetEstimatorTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Budgeting;

namespace OrchestAI.Tests.Infrastructure;

public sealed class ConservativeBudgetEstimatorTests
{
    [Fact]
    public async Task EstimateWorstCaseCostAsync_MultipliesAgentsByToolCallsByAssumedCost()
    {
        var estimator = new ConservativeBudgetEstimator(
            Options.Create(new AbuseProtectionOptions { AssumedCostPerToolCallUsd = 0.10m }));
        var limits = new ResolvedTenantLimits(120, 5, 4, 20, 50m, 500m, 100);

        var estimate = await estimator.EstimateWorstCaseCostAsync(limits);

        estimate.Should().Be(4 * 20 * 0.10m);
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter ConservativeBudgetEstimatorTests`
Expected: PASS (1/1).

- [ ] **Step 6: EF configuration, repository, and wiring**

Create `src/OrchestAI.Infrastructure/Data/Configurations/TaskAdmissionReservationConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class TaskAdmissionReservationConfiguration : IEntityTypeConfiguration<TaskAdmissionReservation>
{
    public void Configure(EntityTypeBuilder<TaskAdmissionReservation> builder)
    {
        builder.ToTable("TaskAdmissionReservations");
        builder.HasKey(x => x.TaskId);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.Property(x => x.ReservedCostUsd).HasColumnType("decimal(18,6)");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<OrchestrationTask>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Create `src/OrchestAI.Infrastructure/Repositories/TaskAdmissionReservationRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class TaskAdmissionReservationRepository : ITaskAdmissionReservationRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TaskAdmissionReservationRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<TaskAdmissionReservation?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.TaskAdmissionReservations
            .FirstOrDefaultAsync(x => x.TaskId == taskId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReleaseAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.TaskAdmissionReservations
            .Where(x => x.TaskId == taskId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
```

In `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`, add after `DbSet<IdempotencyRecord>`:

```csharp
    public DbSet<TaskAdmissionReservation> TaskAdmissionReservations => Set<TaskAdmissionReservation>();
```

And in `OnModelCreating`, after the `IdempotencyRecordConfiguration` line:

```csharp
        modelBuilder.ApplyConfiguration(new TaskAdmissionReservationConfiguration());
```

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, after `services.AddScoped<IIdempotencyRecordRepository, IdempotencyRecordRepository>();`:

```csharp
        services.AddScoped<ITaskAdmissionReservationRepository, TaskAdmissionReservationRepository>();
        services.AddSingleton<IBudgetEstimator, ConservativeBudgetEstimator>();
```

- [ ] **Step 7: Generate the migration**

Run: `cd src/OrchestAI.Infrastructure && dotnet ef migrations add AddTaskAdmissionReservations --startup-project ../OrchestAI.API`
Expected: creates table `TaskAdmissionReservations` with `TaskId` as primary key, FKs to `Tenants`/`OrchestrationTasks`, and an index on `(TenantId, CreatedAt)`.

- [ ] **Step 8: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 4's count + 2).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add TaskAdmissionReservation, ITaskAdmissionReservationRepository, and IBudgetEstimator"
```

---

### Task 6: The atomic admission transaction (`IOrchestrationAdmissionRepository`, `AdmitOrchestrationTaskCommand`)

**Files:**
- Create: `src/OrchestAI.Domain/Enums/AdmissionFailureReason.cs`
- Create: `src/OrchestAI.Domain/Models/AdmissionResult.cs`
- Create: `src/OrchestAI.Domain/Interfaces/IOrchestrationAdmissionRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/OrchestrationAdmissionRepository.cs`
- Create: `src/OrchestAI.Application/Commands/AdmitOrchestrationTask/AdmitOrchestrationTaskCommand.cs`
- Create: `src/OrchestAI.Application/Commands/AdmitOrchestrationTask/AdmitOrchestrationTaskResponse.cs`
- Create: `src/OrchestAI.Application/Commands/AdmitOrchestrationTask/AdmitOrchestrationTaskHandler.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs`
- Test: Create `tests/OrchestAI.Tests/Application/AdmitOrchestrationTaskHandlerTests.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/OrchestrationAdmissionRepositoryTests.cs` (real Postgres — see note below)

**Interfaces:**
- Consumes: `ITenantLimitsProvider` (Task 1), `IBudgetEstimator` (Task 5), `ITaskAdmissionReservationRepository`'s entity shape (Task 5, though this task writes reservations directly inside its own transaction, not through that repository), `ICostRollupRepository`/`ICostLedgerRepository` (existing, Week 7/10), `TenantLimitExceededException`/`ConflictException` (Task 2 / existing).
- Produces: `AdmissionFailureReason { TaskNotPending, ConcurrencyExceeded, BudgetExceeded }`; `AdmissionResult(bool Admitted, AdmissionFailureReason? FailureReason, string? DetailsJson)`; `IOrchestrationAdmissionRepository.TryAdmitAsync(Guid taskId, Guid tenantId, int maxConcurrentTasks, decimal reservationAmountUsd, decimal dailyBudgetUsd, decimal actualDailySpendUsd, decimal monthlyBudgetUsd, decimal actualMonthlySpendUsd, TimeSpan reservationStaleness, CancellationToken)`; `AdmitOrchestrationTaskCommand(Guid TaskId)` (Task 7 sends this synchronously from the controller, before `Task.Run`).

**Why this is one repository method, not a composition of smaller ones:** the all-or-nothing guarantee (brainstorming clarification #1) only holds if the tenant row lock, the task-state CAS, the concurrency count, the budget check, and the reservation insert all share exactly one DB transaction. Splitting this across multiple `IDbContextFactory`-per-call repository methods (this codebase's normal pattern) would silently break that guarantee — each call would get its own connection/transaction. This is a deliberate, narrow exception to the usual repository-per-entity convention, justified the same way Week 10 justified `TenantScopingInterceptor`'s system-write-scope: one documented, auditable exception to a normal rule, not a pattern to repeat casually elsewhere.

**Budget-check spend read** reuses `ICostRollupRepository.GetByDateRangeAsync`/`ICostLedgerRepository.GetDailyAggregatesAsync` unchanged (Week 7's hybrid rollup+live-ledger pattern — see Investigation summary), called with `userId: null` to get the tenant-wide total rather than one user's slice, computed in `AdmitOrchestrationTaskHandler` (Application layer) and passed into `TryAdmitAsync` as plain `decimal`s — the admission repository itself never touches cost repositories, keeping the "one transaction" boundary narrow and the spend read (deliberately a fast snapshot, not part of the atomic transaction — see Global Constraints) separate from the reservation math (which is atomic).

- [ ] **Step 1: Write the failing handler tests (mocked)**

Create `tests/OrchestAI.Tests/Application/AdmitOrchestrationTaskHandlerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.AdmitOrchestrationTask;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class AdmitOrchestrationTaskHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    private AdmitOrchestrationTaskHandler CreateHandler(OrchestrationTask task, AdmissionResult admissionResult)
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var admissionRepoMock = new Mock<IOrchestrationAdmissionRepository>();
        admissionRepoMock.Setup(r => r.TryAdmitAsync(
            task.Id, _tenantId, It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(admissionResult);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 4, 20, 50m, 500m, 100));

        var estimatorMock = new Mock<IBudgetEstimator>();
        estimatorMock.Setup(e => e.EstimateWorstCaseCostAsync(It.IsAny<ResolvedTenantLimits>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4m);

        var rollupRepoMock = new Mock<ICostRollupRepository>();
        rollupRepoMock.Setup(r => r.GetByDateRangeAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostRollup>());

        var ledgerRepoMock = new Mock<ICostLedgerRepository>();
        ledgerRepoMock.Setup(r => r.GetDailyAggregatesAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostLedgerAggregate>());

        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(_tenantId);

        return new AdmitOrchestrationTaskHandler(
            taskRepoMock.Object, admissionRepoMock.Object, limitsProviderMock.Object, estimatorMock.Object,
            rollupRepoMock.Object, ledgerRepoMock.Object, accessorMock.Object,
            Options.Create(new AbuseProtectionOptions()), NullLogger<AdmitOrchestrationTaskHandler>.Instance);
    }

    [Fact]
    public async Task Handle_TaskNotFound_ThrowsNotFoundException()
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrchestrationTask?)null);
        var handler = new AdmitOrchestrationTaskHandler(
            taskRepoMock.Object, Mock.Of<IOrchestrationAdmissionRepository>(), Mock.Of<ITenantLimitsProvider>(),
            Mock.Of<IBudgetEstimator>(), Mock.Of<ICostRollupRepository>(), Mock.Of<ICostLedgerRepository>(),
            Mock.Of<ICurrentTenantAccessor>(), Options.Create(new AbuseProtectionOptions()),
            NullLogger<AdmitOrchestrationTaskHandler>.Instance);

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_Admitted_ReturnsResponse()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(task, new AdmissionResult(true, null, null));

        var response = await handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        response.TaskId.Should().Be(task.Id);
    }

    [Fact]
    public async Task Handle_TaskNotPending_ThrowsConflictException()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(task, new AdmissionResult(false, AdmissionFailureReason.TaskNotPending, null));

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_ConcurrencyExceeded_ThrowsTenantLimitExceededExceptionWithCorrectReason()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(
            task, new AdmissionResult(false, AdmissionFailureReason.ConcurrencyExceeded, """{"limit":5,"actual":5}"""));

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TenantLimitExceededException>();
        exception.Which.Reason.Should().Be(RejectionReason.ConcurrencyExceeded);
        exception.Which.TenantId.Should().Be(_tenantId);
        exception.Which.TraceId.Should().Be(task.TraceId);
    }

    [Fact]
    public async Task Handle_BudgetExceeded_ThrowsTenantLimitExceededExceptionWithCorrectReason()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var handler = CreateHandler(
            task, new AdmissionResult(false, AdmissionFailureReason.BudgetExceeded, """{"limit":50,"actual":52}"""));

        var act = () => handler.Handle(new AdmitOrchestrationTaskCommand(task.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TenantLimitExceededException>();
        exception.Which.Reason.Should().Be(RejectionReason.BudgetExceeded);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter AdmitOrchestrationTaskHandlerTests`
Expected: FAIL (compile error — none of these types exist yet).

- [ ] **Step 3: Implement `AdmissionFailureReason`, `AdmissionResult`, and `IOrchestrationAdmissionRepository`**

Create `src/OrchestAI.Domain/Enums/AdmissionFailureReason.cs`:

```csharp
namespace OrchestAI.Domain.Enums;

public enum AdmissionFailureReason
{
    TaskNotPending,
    ConcurrencyExceeded,
    BudgetExceeded
}
```

Create `src/OrchestAI.Domain/Models/AdmissionResult.cs`:

```csharp
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

public sealed record AdmissionResult(bool Admitted, AdmissionFailureReason? FailureReason, string? DetailsJson);
```

Create `src/OrchestAI.Domain/Interfaces/IOrchestrationAdmissionRepository.cs`:

```csharp
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// The one place the admission transaction (task-state CAS + concurrency-slot count + budget
// reservation, all inside one DB transaction with a per-tenant row lock) is implemented. See
// ADR-015 and Task 6's investigation note for why this is deliberately one method, not a
// composition of smaller repository calls.
public interface IOrchestrationAdmissionRepository
{
    Task<AdmissionResult> TryAdmitAsync(
        Guid taskId,
        Guid tenantId,
        int maxConcurrentTasks,
        decimal reservationAmountUsd,
        decimal dailyBudgetUsd,
        decimal actualDailySpendUsd,
        decimal monthlyBudgetUsd,
        decimal actualMonthlySpendUsd,
        TimeSpan reservationStaleness,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement `AdmitOrchestrationTaskCommand`/`Response`/`Handler`**

Create `src/OrchestAI.Application/Commands/AdmitOrchestrationTask/AdmitOrchestrationTaskCommand.cs`:

```csharp
using MediatR;

namespace OrchestAI.Application.Commands.AdmitOrchestrationTask;

public sealed record AdmitOrchestrationTaskCommand(Guid TaskId) : IRequest<AdmitOrchestrationTaskResponse>;
```

Create `src/OrchestAI.Application/Commands/AdmitOrchestrationTask/AdmitOrchestrationTaskResponse.cs`:

```csharp
namespace OrchestAI.Application.Commands.AdmitOrchestrationTask;

public sealed record AdmitOrchestrationTaskResponse(Guid TaskId);
```

Create `src/OrchestAI.Application/Commands/AdmitOrchestrationTask/AdmitOrchestrationTaskHandler.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.AdmitOrchestrationTask;

// The synchronous admission step inserted into POST /tasks/{id}/start, run and awaited by the
// controller BEFORE the background dispatch (Task 7) — this is what makes a 429 possible for
// concurrency/budget rejections, since the existing dispatch runs fire-and-forget after the
// HTTP response is already written. See Task 7's controller changes and ADR-015.
public sealed class AdmitOrchestrationTaskHandler
    : IRequestHandler<AdmitOrchestrationTaskCommand, AdmitOrchestrationTaskResponse>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly IOrchestrationAdmissionRepository _admissionRepository;
    private readonly ITenantLimitsProvider _limitsProvider;
    private readonly IBudgetEstimator _budgetEstimator;
    private readonly ICostRollupRepository _costRollupRepository;
    private readonly ICostLedgerRepository _costLedgerRepository;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly IOptions<AbuseProtectionOptions> _abuseProtectionOptions;
    private readonly ILogger<AdmitOrchestrationTaskHandler> _logger;

    public AdmitOrchestrationTaskHandler(
        IOrchestrationTaskRepository taskRepository,
        IOrchestrationAdmissionRepository admissionRepository,
        ITenantLimitsProvider limitsProvider,
        IBudgetEstimator budgetEstimator,
        ICostRollupRepository costRollupRepository,
        ICostLedgerRepository costLedgerRepository,
        ICurrentTenantAccessor tenantAccessor,
        IOptions<AbuseProtectionOptions> abuseProtectionOptions,
        ILogger<AdmitOrchestrationTaskHandler> logger)
    {
        _taskRepository = taskRepository;
        _admissionRepository = admissionRepository;
        _limitsProvider = limitsProvider;
        _budgetEstimator = budgetEstimator;
        _costRollupRepository = costRollupRepository;
        _costLedgerRepository = costLedgerRepository;
        _tenantAccessor = tenantAccessor;
        _abuseProtectionOptions = abuseProtectionOptions;
        _logger = logger;
    }

    public async Task<AdmitOrchestrationTaskResponse> Handle(
        AdmitOrchestrationTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

        var tenantId = _tenantAccessor.TenantId
            ?? throw new InvalidOperationException("AdmitOrchestrationTaskHandler ran with no ambient tenant.");

        var limits = await _limitsProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var reservationAmount = await _budgetEstimator
            .EstimateWorstCaseCostAsync(limits, cancellationToken).ConfigureAwait(false);
        var (actualDailySpend, actualMonthlySpend) = await ReadActualSpendAsync(cancellationToken).ConfigureAwait(false);
        var staleness = TimeSpan.FromMinutes(_abuseProtectionOptions.Value.ReservationStalenessMinutes);

        var result = await _admissionRepository.TryAdmitAsync(
            task.Id, tenantId, limits.MaxConcurrentTasks, reservationAmount,
            limits.DailyCostBudgetUsd, actualDailySpend, limits.MonthlyCostBudgetUsd, actualMonthlySpend,
            staleness, cancellationToken).ConfigureAwait(false);

        if (!result.Admitted)
        {
            if (result.FailureReason == AdmissionFailureReason.TaskNotPending)
                throw new ConflictException($"Task {task.Id} is not in a startable state.");

            var reason = result.FailureReason == AdmissionFailureReason.ConcurrencyExceeded
                ? RejectionReason.ConcurrencyExceeded
                : RejectionReason.BudgetExceeded;
            var retryAfterSeconds = reason == RejectionReason.ConcurrencyExceeded ? 30 : 3600;
            var detail = reason == RejectionReason.ConcurrencyExceeded
                ? $"Tenant has reached its concurrent task limit ({limits.MaxConcurrentTasks})."
                : "Tenant cost budget would be exceeded by this task.";

            _logger.LogWarning(
                "Admission rejected for task {TaskId}, tenant {TenantId}: {Reason} — {Details}",
                task.Id, tenantId, reason, result.DetailsJson);

            throw new TenantLimitExceededException(
                tenantId, reason, detail, retryAfterSeconds, result.DetailsJson ?? "{}", task.TraceId);
        }

        _logger.LogInformation(
            "Task {TaskId} admitted for tenant {TenantId}, reserved ${Amount:F4}", task.Id, tenantId, reservationAmount);

        return new AdmitOrchestrationTaskResponse(task.Id);
    }

    // Reuses ADR-011's hybrid rollup+live-ledger read pattern (GetCostDashboardHandler) unchanged,
    // applied tenant-wide (userId: null) rather than to one user's slice — see Task 6's investigation
    // note. Deliberately not modifying GetCostDashboardHandler itself.
    private async Task<(decimal Daily, decimal Monthly)> ReadActualSpendAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var todayAggregates = await _costLedgerRepository
            .GetDailyAggregatesAsync(today, today, cancellationToken).ConfigureAwait(false);
        var actualDaily = todayAggregates.Sum(a => a.CostUsd);

        var actualMonthly = actualDaily;
        if (monthStart < today)
        {
            var monthRollups = await _costRollupRepository
                .GetByDateRangeAsync(monthStart, today.AddDays(-1), userId: null, cancellationToken)
                .ConfigureAwait(false);
            actualMonthly += monthRollups.Sum(r => r.CostUsd);
        }

        return (actualDaily, actualMonthly);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter AdmitOrchestrationTaskHandlerTests`
Expected: PASS (5/5).

- [ ] **Step 6: Implement `OrchestrationAdmissionRepository`**

Create `src/OrchestAI.Infrastructure/Repositories/OrchestrationAdmissionRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

// This codebase's first explicit multi-statement DB transaction — see Task 6's investigation
// note for why the all-or-nothing guarantee requires it. Cannot be tested against EF Core's
// InMemory provider at all (neither ExecuteUpdateAsync nor FOR UPDATE translate) — see
// OrchestrationAdmissionRepositoryTests, which runs against real Postgres.
public sealed class OrchestrationAdmissionRepository : IOrchestrationAdmissionRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public OrchestrationAdmissionRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<AdmissionResult> TryAdmitAsync(
        Guid taskId,
        Guid tenantId,
        int maxConcurrentTasks,
        decimal reservationAmountUsd,
        decimal dailyBudgetUsd,
        decimal actualDailySpendUsd,
        decimal monthlyBudgetUsd,
        decimal actualMonthlySpendUsd,
        TimeSpan reservationStaleness,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await ctx.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Serializes concurrent admissions for THIS tenant only — a different tenant's admission
        // locks its own Tenants row and never blocks on this one. Tenant is not ITenantScoped,
        // so this query is never affected by the tenant global filter either way.
        await ctx.Tenants
            .FromSqlInterpolated($"SELECT * FROM \"Tenants\" WHERE \"Id\" = {tenantId} FOR UPDATE")
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The atomic Pending -> Running CAS. OrchestrationTask IS ITenantScoped, so the global
        // query filter is automatically ANDed into this WHERE clause too — a taskId belonging to
        // a different tenant than the caller's ambient scope would find 0 rows here, failing
        // closed, not just failing on a later explicit check.
        var rowsUpdated = await ctx.OrchestrationTasks
            .Where(t => t.Id == taskId && t.Status == OrchestrationTaskStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, OrchestrationTaskStatus.Running), cancellationToken)
            .ConfigureAwait(false);

        if (rowsUpdated == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new AdmissionResult(false, AdmissionFailureReason.TaskNotPending, null);
        }

        var staleThreshold = DateTimeOffset.UtcNow - reservationStaleness;

        var activeCount = await ctx.TaskAdmissionReservations
            .Where(r => r.TenantId == tenantId && r.CreatedAt > staleThreshold)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeCount >= maxConcurrentTasks)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var details = $$"""{"limit":{{maxConcurrentTasks}},"actual":{{activeCount}}}""";
            return new AdmissionResult(false, AdmissionFailureReason.ConcurrencyExceeded, details);
        }

        var activeReservedUsd = await ctx.TaskAdmissionReservations
            .Where(r => r.TenantId == tenantId && r.CreatedAt > staleThreshold)
            .SumAsync(r => (decimal?)r.ReservedCostUsd, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var projectedDaily = actualDailySpendUsd + activeReservedUsd + reservationAmountUsd;
        if (projectedDaily > dailyBudgetUsd)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var details = $$"""{"limit":{{dailyBudgetUsd}},"actual":{{projectedDaily}},"period":"day"}""";
            return new AdmissionResult(false, AdmissionFailureReason.BudgetExceeded, details);
        }

        var projectedMonthly = actualMonthlySpendUsd + activeReservedUsd + reservationAmountUsd;
        if (projectedMonthly > monthlyBudgetUsd)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var details = $$"""{"limit":{{monthlyBudgetUsd}},"actual":{{projectedMonthly}},"period":"month"}""";
            return new AdmissionResult(false, AdmissionFailureReason.BudgetExceeded, details);
        }

        var reservation = TaskAdmissionReservation.Create(taskId, reservationAmountUsd);
        ctx.TaskAdmissionReservations.Add(reservation);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AdmissionResult(true, null, null);
    }
}
```

- [ ] **Step 7: Wire DI**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, after `services.AddSingleton<IBudgetEstimator, ConservativeBudgetEstimator>();`:

```csharp
        services.AddScoped<IOrchestrationAdmissionRepository, OrchestrationAdmissionRepository>();
```

- [ ] **Step 8: Real-Postgres tests proving atomicity and rollback**

> Requires the local dev Postgres (`docker-compose.yml`) up and migrated through Task 5 — same precondition as the existing `TenantFilterExecuteDeleteTests`.

Create `tests/OrchestAI.Tests/Infrastructure/OrchestrationAdmissionRepositoryTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Neither ExecuteUpdateAsync nor FOR UPDATE translate against EF Core's InMemory provider — same
// limitation TenantFilterExecuteDeleteTests documents. Runs against the real local dev Postgres,
// always rolled back so the shared dev database is untouched regardless of pass/fail.
public sealed class OrchestrationAdmissionRepositoryTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    private sealed class TransactionScopedDbContextFactory(
        DbContextOptions<AppDbContext> options, NpgsqlTransaction transaction, ICurrentTenantAccessor accessor)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var ctx = new AppDbContext(options, accessor);
            ctx.Database.UseTransaction(transaction);
            return ctx;
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static async Task<(TransactionScopedDbContextFactory Factory, AsyncLocalCurrentTenantAccessor Accessor,
        NpgsqlConnection Connection, NpgsqlTransaction Transaction)> SetUpAsync()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return (new TransactionScopedDbContextFactory(options, transaction, accessor), accessor, connection, transaction);
    }

    private static async Task<(Tenant Tenant, OrchestrationTask Task)> SeedTenantAndTaskAsync(
        TransactionScopedDbContextFactory factory, AsyncLocalCurrentTenantAccessor accessor, string suffix)
    {
        var tenant = Tenant.Create($"Acme-{suffix}", $"acme-{suffix}");
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
        }

        OrchestrationTask task;
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var user = TestUserFactory.Create($"admit-{suffix}@test.local");
            task = OrchestrationTask.Create(user.Id, "T", "P", false);
            ctx.Users.Add(user);
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
        }

        return (tenant, task);
    }

    [Fact]
    public async Task TryAdmitAsync_WithinLimits_TransitionsTaskAndInsertsReservation()
    {
        var (factory, accessor, connection, transaction) = await SetUpAsync();
        try
        {
            var (tenant, task) = await SeedTenantAndTaskAsync(factory, accessor, Guid.NewGuid().ToString("N"));
            var repository = new OrchestrationAdmissionRepository(factory);

            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeTrue();

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reloadedTask = await verifyCtx.OrchestrationTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
            reloadedTask.Status.Should().Be(OrchestrationTaskStatus.Running);
            var reservation = await verifyCtx.TaskAdmissionReservations
                .IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TaskId == task.Id);
            reservation.Should().NotBeNull();
            reservation!.ReservedCostUsd.Should().Be(2m);
        }
        finally
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAdmitAsync_TaskAlreadyRunning_RejectsAndLeavesNoReservation()
    {
        var (factory, accessor, connection, transaction) = await SetUpAsync();
        try
        {
            var (tenant, task) = await SeedTenantAndTaskAsync(factory, accessor, Guid.NewGuid().ToString("N"));
            using (accessor.SetTenant(tenant.Id))
            {
                await using var ctx = await factory.CreateDbContextAsync();
                var trackedTask = await ctx.OrchestrationTasks.FirstAsync(t => t.Id == task.Id);
                trackedTask.MarkRunning();
                await ctx.SaveChangesAsync();
            }

            var repository = new OrchestrationAdmissionRepository(factory);
            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeFalse();
            result.FailureReason.Should().Be(AdmissionFailureReason.TaskNotPending);

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reservation = await verifyCtx.TaskAdmissionReservations
                .IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TaskId == task.Id);
            reservation.Should().BeNull();
        }
        finally
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAdmitAsync_ConcurrencyLimitAlreadyReached_RollsBackTaskStateToo()
    {
        var (factory, accessor, connection, transaction) = await SetUpAsync();
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var (tenant, task) = await SeedTenantAndTaskAsync(factory, accessor, suffix);
            using (accessor.SetTenant(tenant.Id))
            {
                await using var ctx = await factory.CreateDbContextAsync();
                var user = TestUserFactory.Create($"other-{suffix}@test.local");
                var otherTask = OrchestrationTask.Create(user.Id, "Other", "P", false);
                ctx.Users.Add(user);
                ctx.OrchestrationTasks.Add(otherTask);
                await ctx.SaveChangesAsync();

                // Pre-seed one active reservation, filling the (deliberately tiny) limit of 1.
                ctx.TaskAdmissionReservations.Add(TaskAdmissionReservation.Create(otherTask.Id, 1m));
                await ctx.SaveChangesAsync();
            }

            var repository = new OrchestrationAdmissionRepository(factory);
            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 1, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeFalse();
            result.FailureReason.Should().Be(AdmissionFailureReason.ConcurrencyExceeded);

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reloadedTask = await verifyCtx.OrchestrationTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
            reloadedTask.Status.Should().Be(OrchestrationTaskStatus.Pending,
                "a rejected admission must roll back the task-state CAS too — all-or-nothing, never left Running with no reservation");
        }
        finally
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAdmitAsync_BudgetWouldBeExceeded_RollsBackTaskStateToo()
    {
        var (factory, accessor, connection, transaction) = await SetUpAsync();
        try
        {
            var (tenant, task) = await SeedTenantAndTaskAsync(factory, accessor, Guid.NewGuid().ToString("N"));
            var repository = new OrchestrationAdmissionRepository(factory);

            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 10m,
                    dailyBudgetUsd: 10m, actualDailySpendUsd: 5m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 5m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeFalse();
            result.FailureReason.Should().Be(AdmissionFailureReason.BudgetExceeded);

            await using var verifyCtx = await factory.CreateDbContextAsync();
            var reloadedTask = await verifyCtx.OrchestrationTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == task.Id);
            reloadedTask.Status.Should().Be(OrchestrationTaskStatus.Pending);
        }
        finally
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAdmitAsync_ReservationOlderThanStaleness_ExcludedFromConcurrencyCount()
    {
        var (factory, accessor, connection, transaction) = await SetUpAsync();
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var (tenant, task) = await SeedTenantAndTaskAsync(factory, accessor, suffix);
            using (accessor.SetTenant(tenant.Id))
            {
                await using var ctx = await factory.CreateDbContextAsync();
                var user = TestUserFactory.Create($"stale-{suffix}@test.local");
                var staleTask = OrchestrationTask.Create(user.Id, "Stale", "P", false);
                ctx.Users.Add(user);
                ctx.OrchestrationTasks.Add(staleTask);
                await ctx.SaveChangesAsync();

                // Simulate a crashed reservation from 60 minutes ago (30-minute staleness window)
                // — a process crash means the normal try/finally release (Task 7) never ran.
                await ctx.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "TaskAdmissionReservations" ("TaskId", "TenantId", "ReservedCostUsd", "CreatedAt")
                    VALUES ({staleTask.Id}, {tenant.Id}, 1.0, {DateTimeOffset.UtcNow.AddMinutes(-60)})
                    """);
            }

            var repository = new OrchestrationAdmissionRepository(factory);
            AdmissionResult result;
            using (accessor.SetTenant(tenant.Id))
            {
                result = await repository.TryAdmitAsync(
                    task.Id, tenant.Id, maxConcurrentTasks: 1, reservationAmountUsd: 2m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            result.Admitted.Should().BeTrue(
                "a reservation older than the staleness window must be excluded from the concurrency count — this is the crash-recovery mechanism, not a reconciliation service");
        }
        finally
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter OrchestrationAdmissionRepositoryTests`
Expected: PASS (5/5) — requires the local dev Postgres running (`docker compose up -d` if not already) and migrated at least through Task 5.

- [ ] **Step 9: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 5's count + 10).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add atomic admission transaction (IOrchestrationAdmissionRepository, AdmitOrchestrationTaskCommand)"
```

---

### Task 7: Restructure `/start` — synchronous admission, then background dispatch with guaranteed reservation release

**Files:**
- Modify: `src/OrchestAI.API/Controllers/TasksController.cs` (`StartAsync`)
- Modify: `src/OrchestAI.Application/Commands/StartOrchestration/StartOrchestrationHandler.cs`
- Test: Create `tests/OrchestAI.Tests/Application/StartOrchestrationReservationReleaseTests.cs`
- Investigate + adapt: any existing `StartOrchestrationHandler` test file that asserts the old `Status != Pending` precondition (see Step 1)

**Interfaces:**
- Consumes: `AdmitOrchestrationTaskCommand` (Task 6), `ITaskAdmissionReservationRepository.ReleaseAsync` (Task 5).
- Produces: `StartOrchestrationHandler`'s guard changes from "`task.Status != Pending` throws bare `InvalidOperationException`" to "`task.Status != Running` throws `InvalidOperationException`" (defensive only — admission already performed the real CAS) — this is a breaking behavioral change for anything asserting the old guard; Step 1 finds and fixes it.

- [ ] **Step 1: Find and inventory any existing test coverage of the old `/start` guard**

Run: `grep -rl "StartOrchestrationHandler\|StartOrchestrationCommand" tests/OrchestAI.Tests --include=*.cs`

For every file found, read it in full and note which tests assert `task.Status != Pending` / construct a task already in a non-`Pending` state and expect `InvalidOperationException` from a bare `MarkRunning()`-less path. Task 3's replacement handler (Step 4 below) changes that precondition to `task.Status != Running` — any such test must be updated to construct the task with `.MarkRunning()` already called (simulating that admission already ran) instead of leaving it `Pending`, or it will fail for the wrong reason after this task's change.

- [ ] **Step 2: Write the failing reservation-release tests**

Create `tests/OrchestAI.Tests/Application/StartOrchestrationReservationReleaseTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

// Covers the reservation try/finally added in Task 7 — StartOrchestrationHandler must release
// the admission reservation on every exit path (success, task failure, unhandled exception),
// never leaking a tenant's concurrency/budget capacity. The genuine process-crash path (where
// even this finally can't run) is covered separately by Task 11's staleness-TTL test.
public sealed class StartOrchestrationReservationReleaseTests
{
    private static (StartOrchestrationHandler Handler, Mock<ITaskAdmissionReservationRepository> ReservationRepoMock,
        Mock<IOrchestratorAgent> OrchestratorMock) CreateHandler(OrchestrationTask task)
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        var agentFactoryMock = new Mock<IAgentFactory>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object, eventBusMock.Object,
            approvalGatewayMock.Object, checkpointRepoMock.Object, reservationRepoMock.Object,
            NullLogger<StartOrchestrationHandler>.Instance);

        return (handler, reservationRepoMock, orchestratorMock);
    }

    [Fact]
    public async Task Handle_TaskNotRunning_ThrowsInvalidOperationException()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false); // still Pending
        var (handler, _, _) = CreateHandler(task);

        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_PlanAsyncThrows_StillReleasesReservation()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        task.MarkRunning();
        var (handler, reservationRepoMock, orchestratorMock) = CreateHandler(task);
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM provider unavailable"));

        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        reservationRepoMock.Verify(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()), Times.Once,
            "the reservation must be released even when the background dispatch throws before completing");
    }

    [Fact]
    public async Task Handle_ReleaseAsyncItselfThrows_DoesNotMaskTheOriginalOutcome()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        task.MarkRunning();
        var (handler, reservationRepoMock, orchestratorMock) = CreateHandler(task);
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM provider unavailable"));
        reservationRepoMock.Setup(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable during release"));

        // The release failure must be caught and logged internally (best-effort, matching the
        // existing MaybeRecordUsageAsync/SaveCheckpointAsync pattern) — the ORIGINAL PlanAsync
        // failure must still be what propagates, not the release failure masking it.
        var act = () => handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("LLM provider unavailable");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter StartOrchestrationReservationReleaseTests`
Expected: FAIL (compile error — `StartOrchestrationHandler`'s constructor doesn't accept `ITaskAdmissionReservationRepository` yet).

- [ ] **Step 4: Replace `StartOrchestrationHandler.cs` in full**

Replace `src/OrchestAI.Application/Commands/StartOrchestration/StartOrchestrationHandler.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Application.Commands.StartOrchestration;

public sealed class StartOrchestrationHandler
    : IRequestHandler<StartOrchestrationCommand, StartOrchestrationResponse>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly IOrchestratorAgent _orchestratorAgent;
    private readonly IAgentFactory _agentFactory;
    private readonly IOrchestrationEventBus _eventBus;
    private readonly IApprovalGateway _approvalGateway;
    private readonly ITaskCheckpointRepository _checkpointRepository;
    private readonly ITaskAdmissionReservationRepository _reservationRepository;
    private readonly ILogger<StartOrchestrationHandler> _logger;

    public StartOrchestrationHandler(
        IOrchestrationTaskRepository taskRepository,
        IOrchestratorAgent orchestratorAgent,
        IAgentFactory agentFactory,
        IOrchestrationEventBus eventBus,
        IApprovalGateway approvalGateway,
        ITaskCheckpointRepository checkpointRepository,
        ITaskAdmissionReservationRepository reservationRepository,
        ILogger<StartOrchestrationHandler> logger)
    {
        _taskRepository = taskRepository;
        _orchestratorAgent = orchestratorAgent;
        _agentFactory = agentFactory;
        _eventBus = eventBus;
        _approvalGateway = approvalGateway;
        _checkpointRepository = checkpointRepository;
        _reservationRepository = reservationRepository;
        _logger = logger;
    }

    public async Task<StartOrchestrationResponse> Handle(
        StartOrchestrationCommand request,
        CancellationToken cancellationToken)
    {
        var task = await _taskRepository
            .GetByIdAsync(request.TaskId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

        // AdmitOrchestrationTaskHandler (Task 6) already performed the Pending -> Running CAS
        // and the concurrency/budget reservation atomically, awaited by the controller BEFORE
        // this background dispatch was even started (see TasksController.StartAsync). This is a
        // defensive check for a state that should be unreachable, not the primary guard.
        if (task.Status != OrchestrationTaskStatus.Running)
            throw new InvalidOperationException(
                $"Task {request.TaskId} reached StartOrchestrationHandler in unexpected state " +
                $"'{task.Status}' — admission should have already transitioned it to Running.");

        try
        {
            _eventBus.Publish(request.TaskId, new SseEvent(
                "task_started",
                request.TaskId,
                new { taskId = request.TaskId, status = "Running" },
                DateTimeOffset.UtcNow));

            _logger.LogInformation("Task {TaskId} started, running orchestrator", request.TaskId);

            var plan = await _orchestratorAgent
                .PlanAsync(request.TaskId, task.UserPrompt, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Orchestrator selected {AgentCount} agents for task {TaskId}: {Agents}",
                plan.SelectedAgents.Count,
                request.TaskId,
                string.Join(", ", plan.SelectedAgents));

            if (task.RequireApproval)
            {
                task.RequestApproval();
                await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

                _eventBus.Publish(request.TaskId, new SseEvent(
                    "approval_required",
                    request.TaskId,
                    new
                    {
                        taskId = request.TaskId,
                        plan = plan.Plan,
                        selectedAgents = plan.SelectedAgents.Select(a => a.ToString()).ToList(),
                        agentPrompts = plan.AgentPrompts.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
                        executionMode = plan.ExecutionMode.ToString()
                    },
                    DateTimeOffset.UtcNow));

                _logger.LogInformation("Task {TaskId} waiting for human approval", request.TaskId);

                await _approvalGateway.WaitForApprovalAsync(request.TaskId, cancellationToken).ConfigureAwait(false);

                task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

                if (task.ApprovalStatus == TaskApprovalStatus.Rejected)
                {
                    // RejectOrchestrationTaskHandler already marked the task Failed and published task_failed.
                    _logger.LogInformation("Task {TaskId} rejected — aborting before agent dispatch", request.TaskId);
                    return new StartOrchestrationResponse(request.TaskId, []);
                }

                _logger.LogInformation("Task {TaskId} approved — resuming agent dispatch", request.TaskId);
            }

            AgentExecutionResult[] results;

            if (plan.ExecutionMode == ExecutionMode.Sequential)
            {
                _logger.LogInformation(
                    "Task {TaskId} using sequential execution across {AgentCount} agents",
                    request.TaskId, plan.ExecutionOrder.Count);
                results = await RunSequentialAsync(
                    request.TaskId, task.UserId, plan, plan.OrchestratorExecution.SpanId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var subAgentTasks = plan.ExecutionOrder
                    .Select(agentType => RunSubAgentAsync(
                        request.TaskId, task.UserId, agentType, plan.AgentPrompts[agentType],
                        plan.OrchestratorExecution.SpanId, cancellationToken))
                    .ToList();
                results = await Task.WhenAll(subAgentTasks).ConfigureAwait(false);
            }

            var successResults = results.Where(r => r.Success).ToList();
            var failedResults = results.Where(r => !r.Success).ToList();

            var aggregatedOutput = string.Join("\n\n---\n\n",
                successResults.Select(r => r.Output).Where(o => !string.IsNullOrWhiteSpace(o)));

            var reviewResult = await _orchestratorAgent
                .ReviewAsync(request.TaskId, task.UserPrompt, plan, results, cancellationToken)
                .ConfigureAwait(false);

            var allResults = results.Append(plan.OrchestratorExecution).Append(reviewResult).ToList();
            var synthesizedOutput = reviewResult.Success ? reviewResult.Output : aggregatedOutput;

            var totalInputTokens = allResults.Sum(r => r.InputTokens);
            var totalOutputTokens = allResults.Sum(r => r.OutputTokens);
            var totalCostUsd = allResults.Sum(r => r.CostUsd);
            task.AccumulateCost(totalInputTokens, totalOutputTokens, totalCostUsd);

            if (failedResults.Count == 0)
            {
                task.MarkCompleted(synthesizedOutput);
                await _checkpointRepository.DeleteByTaskIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false);

                _eventBus.Publish(request.TaskId, new SseEvent(
                    "task_completed",
                    request.TaskId,
                    new { taskId = request.TaskId, totalCostUsd = task.TotalCostUsd, agentCount = results.Length },
                    DateTimeOffset.UtcNow));

                _logger.LogInformation(
                    "Task {TaskId} completed. Cost: ${CostUsd:F4}", request.TaskId, task.TotalCostUsd);
            }
            else
            {
                var errorSummary = string.Join("; ", failedResults.Select(r => r.ErrorMessage));
                task.MarkFailed(errorSummary, synthesizedOutput);

                _eventBus.Publish(request.TaskId, new SseEvent(
                    "task_failed",
                    request.TaskId,
                    new { taskId = request.TaskId, errorMessage = errorSummary },
                    DateTimeOffset.UtcNow));

                _logger.LogWarning(
                    "Task {TaskId} failed with {FailedCount} agent failures", request.TaskId, failedResults.Count);
            }

            await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

            return new StartOrchestrationResponse(
                request.TaskId,
                results.Select(r => r.AgentExecutionId).ToList().AsReadOnly());
        }
        finally
        {
            // Releases both the concurrency slot and the budget reservation together — covers
            // success, task failure (MarkFailed above), and any unhandled exception in this
            // block alike. Best-effort: a failure here must never mask whatever outcome the try
            // block actually produced (see StartOrchestrationReservationReleaseTests) — it only
            // means the reservation leaks until it ages past the staleness TTL (Task 6/ADR-015).
            try
            {
                await _reservationRepository.ReleaseAsync(request.TaskId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to release admission reservation for task {TaskId} — this may leak tenant " +
                    "concurrency/budget capacity until the reservation ages past the staleness TTL",
                    request.TaskId);
            }
        }
    }

    private async Task<AgentExecutionResult> RunSubAgentAsync(
        Guid taskId,
        Guid userId,
        AgentType agentType,
        string prompt,
        string? parentSpanId,
        CancellationToken cancellationToken)
    {
        try
        {
            var agent = _agentFactory.Create(agentType);
            return await agent.ExecuteAsync(taskId, userId, prompt, cancellationToken, parentSpanId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sub-agent {AgentType} threw an unhandled exception for task {TaskId}",
                agentType, taskId);
            return new AgentExecutionResult(Guid.Empty, string.Empty, false, 0, 0, 0m, ex.Message);
        }
    }

    private async Task<AgentExecutionResult[]> RunSequentialAsync(
        Guid taskId,
        Guid userId,
        OrchestrationPlan plan,
        string? parentSpanId,
        CancellationToken cancellationToken)
    {
        var results = new List<AgentExecutionResult>();
        string? priorOutput = null;

        foreach (var agentType in plan.ExecutionOrder)
        {
            var prompt = BuildSequentialPrompt(plan.AgentPrompts[agentType], priorOutput);
            var result = await RunSubAgentAsync(taskId, userId, agentType, prompt, parentSpanId, cancellationToken)
                .ConfigureAwait(false);
            results.Add(result);

            if (result.Success)
                priorOutput = result.Output;
            else
                _logger.LogWarning(
                    "Sequential agent {AgentType} failed for task {TaskId}: {Error}. Continuing to next agent.",
                    agentType, taskId, result.ErrorMessage);
        }

        return [.. results];
    }

    private static string BuildSequentialPrompt(string basePrompt, string? priorOutput)
    {
        if (string.IsNullOrWhiteSpace(priorOutput))
            return basePrompt;

        const int MaxPriorLength = 3000;
        var prior = priorOutput.Length > MaxPriorLength
            ? priorOutput[..MaxPriorLength] + "\n[...truncated...]"
            : priorOutput;

        return $"{basePrompt}\n\n--- Prior Agent Output ---\n{prior}";
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter StartOrchestrationReservationReleaseTests`
Expected: PASS (3/3). Also re-run any existing `StartOrchestrationHandler` tests identified in Step 1 to confirm they still pass after adaptation.

- [ ] **Step 6: Update `TasksController.StartAsync`**

In `src/OrchestAI.API/Controllers/TasksController.cs`, add the using:

```csharp
using OrchestAI.Application.Commands.AdmitOrchestrationTask;
```

Replace `StartAsync` in full:

```csharp
    /// <summary>Admits (concurrency/budget checks) and starts agent execution for a pending
    /// task. Admission is synchronous — a 429/404/409 here means no dispatch was ever queued;
    /// agent dispatch itself continues in the background after a 202.</summary>
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(typeof(StartOrchestrationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> StartAsync(
        Guid id,
        [FromServices] IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Send(new AdmitOrchestrationTaskCommand(id), cancellationToken);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails { Title = "Conflict", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
        }
        // TenantLimitExceededException is deliberately not caught here — it propagates to the
        // global TenantLimitExceededExceptionHandler (Task 2), the one place that builds the
        // unified 429 response and writes the RejectionEvent. Catching it locally would
        // duplicate that logic in a second place.

        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            try
            {
                await mediator.Send(new StartOrchestrationCommand(id), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning("Dispatch failed — task not found: {TaskId}", ex.EntityId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Dispatch failed — invalid state: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error dispatching task {TaskId}", id);
            }
        });

        return Accepted(new StartOrchestrationResponse(id, []));
    }
```

- [ ] **Step 7: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 6's count + 3, plus/minus any adjustments from Step 1's adaptation of pre-existing tests).

- [ ] **Step 8: Manual smoke check**

Run the API (`dotnet run --project src/OrchestAI.API`) and, using an admin-bootstrapped tenant/API key from Week 10's flow: create a task (`POST /api/v1/tasks`), start it (`POST /api/v1/tasks/{id}/start`), and confirm the response is `202` and the task completes normally via `GET /api/v1/tasks/{id}` or the SSE stream — confirms the synchronous-admission-then-background-dispatch split didn't break the golden path before Tasks 8-11 add more enforcement on top of it.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: restructure /start into synchronous admission plus background dispatch with guaranteed reservation release"
```

---

### Task 8: Orchestrator structural caps — `MaxAgentsPerTask` (pre-dispatch) and `MaxToolCallsPerTask` (running counter)

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/ITaskToolCallBudget.cs`
- Create: `src/OrchestAI.Domain/Models/ToolCallBudgetCheck.cs`
- Create: `src/OrchestAI.Domain/Exceptions/AgentCapExceededException.cs`
- Create: `src/OrchestAI.Infrastructure/Agents/AsyncLocalTaskToolCallBudget.cs`
- Modify: `src/OrchestAI.Infrastructure/Agents/Base/AgentBase.cs`
- Modify: `src/OrchestAI.Infrastructure/Agents/ResearchAgent.cs`, `WriterAgent.cs`, `CodeAgent.cs`, `DataAgent.cs`, `BrowserAgent.cs` (identical mechanical change to each)
- Modify: `src/OrchestAI.Application/Commands/StartOrchestration/StartOrchestrationHandler.cs`
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/AsyncLocalTaskToolCallBudgetTests.cs`
- Test: Create `tests/OrchestAI.Tests/Application/StartOrchestrationAgentCapTests.cs`

**Interfaces:**
- Produces: `ITaskToolCallBudget.BeginScope(int maxToolCalls) : IDisposable`, `.TryIncrement() : ToolCallBudgetCheck`; `ToolCallBudgetCheck(bool Allowed, int CurrentCount, int MaxToolCalls)`; `AgentCapExceededException(string message)`.

**Design note — why `MaxAgentsPerTask` and `MaxToolCallsPerTask` are enforced completely differently:** `OrchestrationPlan.SelectedAgents` is known the instant `PlanAsync` returns, so `MaxAgentsPerTask` is checked once, pre-dispatch, in `StartOrchestrationHandler` — the cleanest "reject before any work happens" case. Tool calls are never known in advance (`OrchestrationPlan` carries no tool-call count) — they happen one at a time, deep inside each sub-agent's own agentic loop (`AgentBase.InvokeToolAsync`), and sub-agents run in parallel via `Task.WhenAll`. `MaxToolCallsPerTask` is therefore a single, shared, task-wide running counter (`ITaskToolCallBudget`, `AsyncLocal`-backed exactly like `ICurrentTenantAccessor`) that every sub-agent increments atomically (`Interlocked`) as it makes calls — the first call that would exceed the cap throws `AgentCapExceededException`, which `AgentBase.ExecuteAsync`'s *existing* catch-all turns into a normal Failed `AgentExecutionResult` (reusing the existing failure-aggregation machinery — no new StartOrchestrationHandler branching needed for this half). "Fail clearly, never truncate silently" still holds: the task ends up `Failed` (a status the eval/scoring layer already treats as incomplete), not `Completed` with a quietly-dropped tool call. A `RejectionEvent` is written at the exact point of rejection in both cases so this is independently observable regardless of which failure path produced it.

**Correctness note verified from real code, not assumed:** all 5 concrete agents (`ResearchAgent`, `WriterAgent`, `CodeAgent`, `DataAgent`, `BrowserAgent`) forward their *entire* constructor parameter list unchanged to `base(...)` (confirmed by reading `ResearchAgent.cs` in full) — adding a parameter to `AgentBase`'s constructor means the identical two-parameter addition applies mechanically to all 5 files.

**Before writing `StartOrchestrationAgentCapTests.cs` (Step 5 below):** read `src/OrchestAI.Domain/Models/OrchestrationPlan.cs` and `src/OrchestAI.Domain/Models/AgentExecutionResult.cs` in full first. This plan's investigation only inferred their property names from call-site usage in `StartOrchestrationHandler.cs` (`plan.Plan`, `plan.SelectedAgents`, `plan.ExecutionOrder`, `plan.AgentPrompts`, `plan.ExecutionMode`, `plan.OrchestratorExecution`; `AgentExecutionResult`'s 6 leading positional args plus named `ErrorMessage`/`SpanId`), never read the files directly — the object-initializer syntax below is written defensively (property-name-based, not positional) for exactly this reason, but confirm the property names still match before running the test, and adjust if they don't.

- [ ] **Step 1: Write the failing `ITaskToolCallBudget` tests**

Create `tests/OrchestAI.Tests/Infrastructure/AsyncLocalTaskToolCallBudgetTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Infrastructure.Agents;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AsyncLocalTaskToolCallBudgetTests
{
    [Fact]
    public void TryIncrement_NoScopeOpen_AlwaysAllowed()
    {
        var budget = new AsyncLocalTaskToolCallBudget();

        budget.TryIncrement().Allowed.Should().BeTrue();
    }

    [Fact]
    public void TryIncrement_WithinCap_Allowed()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using var scope = budget.BeginScope(maxToolCalls: 3);

        budget.TryIncrement().Allowed.Should().BeTrue();
        budget.TryIncrement().Allowed.Should().BeTrue();
        budget.TryIncrement().Allowed.Should().BeTrue();
    }

    [Fact]
    public void TryIncrement_ExceedingCap_NotAllowed()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using var scope = budget.BeginScope(maxToolCalls: 2);

        budget.TryIncrement();
        budget.TryIncrement();
        var thirdCheck = budget.TryIncrement();

        thirdCheck.Allowed.Should().BeFalse();
        thirdCheck.MaxToolCalls.Should().Be(2);
        thirdCheck.CurrentCount.Should().Be(3);
    }

    [Fact]
    public void Dispose_RestoresPreviousScope()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using (budget.BeginScope(maxToolCalls: 1))
        {
            using (budget.BeginScope(maxToolCalls: 100))
            {
                budget.TryIncrement().Allowed.Should().BeTrue();
            }

            // Back in the outer (cap-of-1) scope — the inner scope's consumed calls must not
            // have touched the outer counter.
            budget.TryIncrement().Allowed.Should().BeTrue();
            budget.TryIncrement().Allowed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task TryIncrement_ConcurrentIncrementsWithinOneScope_NeverExceedsCapAcrossParallelCallers()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using var scope = budget.BeginScope(maxToolCalls: 50);

        // Mirrors how parallel sub-agents (Task.WhenAll in StartOrchestrationHandler) each fork
        // from the same ambient scope and increment the SAME shared counter concurrently.
        var tasks = Enumerable.Range(0, 200).Select(_ => Task.Run(() => budget.TryIncrement().Allowed)).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(allowed => allowed).Should().Be(50,
            "exactly the cap's worth of concurrent increments should succeed, never more");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter AsyncLocalTaskToolCallBudgetTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `ITaskToolCallBudget`, `ToolCallBudgetCheck`, `AgentCapExceededException`, and `AsyncLocalTaskToolCallBudget`**

Create `src/OrchestAI.Domain/Models/ToolCallBudgetCheck.cs`:

```csharp
namespace OrchestAI.Domain.Models;

public sealed record ToolCallBudgetCheck(bool Allowed, int CurrentCount, int MaxToolCalls);
```

Create `src/OrchestAI.Domain/Interfaces/ITaskToolCallBudget.cs`:

```csharp
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

// AsyncLocal-backed, mirrors ICurrentTenantAccessor's proven shape — scoped once per task's
// background dispatch (StartOrchestrationHandler), shared safely across parallel sub-agents
// forked via Task.WhenAll: each fork gets a reference to the same counter instance (AsyncLocal
// copy-on-fork semantics), and increments are Interlocked. See Task 8 and ADR-015.
public interface ITaskToolCallBudget
{
    // Opens a new scope with the given cap; disposing restores whatever scope (if any) was
    // ambient before. Opened once per task, before any sub-agent is dispatched.
    IDisposable BeginScope(int maxToolCalls);

    // Atomically increments the call count for the ambient scope. Returns Allowed: true with no
    // scope open at all (uncapped — callers outside a StartOrchestrationHandler-managed
    // dispatch, e.g. eval/post-hoc scoring agent runs, are intentionally not capped by this
    // mechanism this week).
    ToolCallBudgetCheck TryIncrement();
}
```

Create `src/OrchestAI.Domain/Exceptions/AgentCapExceededException.cs`:

```csharp
namespace OrchestAI.Domain.Exceptions;

// Thrown deep inside AgentBase.InvokeToolAsync when the task-wide tool-call cap would be
// exceeded. Deliberately NOT TenantLimitExceededException — that type is reserved for the
// synchronous-HTTP-rejection contract (Task 2); this one is always caught internally by
// AgentBase.ExecuteAsync's existing catch-all, which converts it into a normal Failed
// AgentExecutionResult, reusing the existing failure-aggregation path in StartOrchestrationHandler.
public sealed class AgentCapExceededException : Exception
{
    public AgentCapExceededException(string message) : base(message) { }
}
```

Create `src/OrchestAI.Infrastructure/Agents/AsyncLocalTaskToolCallBudget.cs`:

```csharp
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Agents;

public sealed class AsyncLocalTaskToolCallBudget : ITaskToolCallBudget
{
    private static readonly AsyncLocal<Counter?> Ambient = new();

    public IDisposable BeginScope(int maxToolCalls)
    {
        var previous = Ambient.Value;
        Ambient.Value = new Counter(maxToolCalls);
        return new RestoreScope(previous);
    }

    public ToolCallBudgetCheck TryIncrement()
    {
        var counter = Ambient.Value;
        if (counter is null)
            return new ToolCallBudgetCheck(true, 0, int.MaxValue);

        var newCount = Interlocked.Increment(ref counter.Count);
        return new ToolCallBudgetCheck(newCount <= counter.Max, newCount, counter.Max);
    }

    private sealed class Counter(int max)
    {
        public int Count;
        public readonly int Max = max;
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly Counter? _previous;
        private bool _disposed;

        public RestoreScope(Counter? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = _previous;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter AsyncLocalTaskToolCallBudgetTests`
Expected: PASS (5/5).

- [ ] **Step 5: Write the failing `MaxAgentsPerTask` test**

First, read `src/OrchestAI.Domain/Models/OrchestrationPlan.cs` and `src/OrchestAI.Domain/Models/AgentExecutionResult.cs` in full (see the design note above) and adjust the object-initializer property names below if they differ.

Create `tests/OrchestAI.Tests/Application/StartOrchestrationAgentCapTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

public sealed class StartOrchestrationAgentCapTests
{
    [Fact]
    public async Task Handle_PlanExceedsMaxAgentsPerTask_FailsTaskWithoutDispatchingAnyAgent()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        task.MarkRunning();
        var tenantId = Guid.NewGuid();

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(task);

        var orchestratorExecution = new AgentExecutionResult(
            Guid.NewGuid(), "plan text", true, 10, 10, 0.01m, ErrorMessage: null, SpanId: "span-1");
        var plan = new OrchestrationPlan
        {
            Plan = "plan text",
            SelectedAgents = [AgentType.Research, AgentType.Writer, AgentType.Code, AgentType.Data, AgentType.Browser, AgentType.Research],
            ExecutionOrder = [],
            AgentPrompts = new Dictionary<AgentType, string>(),
            ExecutionMode = ExecutionMode.Parallel,
            OrchestratorExecution = orchestratorExecution
        };

        var orchestratorMock = new Mock<IOrchestratorAgent>();
        orchestratorMock.Setup(o => o.PlanAsync(task.Id, task.UserPrompt, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var agentFactoryMock = new Mock<IAgentFactory>();
        var eventBusMock = new Mock<IOrchestrationEventBus>();
        var approvalGatewayMock = new Mock<IApprovalGateway>();
        var checkpointRepoMock = new Mock<ITaskCheckpointRepository>();
        var reservationRepoMock = new Mock<ITaskAdmissionReservationRepository>();
        var rejectionEventRepoMock = new Mock<IRejectionEventRepository>();
        var toolCallBudgetMock = new Mock<ITaskToolCallBudget>();
        toolCallBudgetMock.Setup(b => b.BeginScope(It.IsAny<int>())).Returns(Mock.Of<IDisposable>());

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 4, 20, 50m, 500m, 100)); // MaxAgentsPerTask = 4

        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(tenantId);

        var handler = new StartOrchestrationHandler(
            taskRepoMock.Object, orchestratorMock.Object, agentFactoryMock.Object, eventBusMock.Object,
            approvalGatewayMock.Object, checkpointRepoMock.Object, reservationRepoMock.Object,
            limitsProviderMock.Object, accessorMock.Object, rejectionEventRepoMock.Object, toolCallBudgetMock.Object,
            NullLogger<StartOrchestrationHandler>.Instance);

        var response = await handler.Handle(new StartOrchestrationCommand(task.Id), CancellationToken.None);

        response.AgentExecutionIds.Should().BeEmpty();
        task.Status.Should().Be(OrchestrationTaskStatus.Failed);
        agentFactoryMock.Verify(f => f.Create(It.IsAny<AgentType>()), Times.Never,
            "exceeding the agent cap must fail the task before dispatching a single agent — never a partial/silent truncation");
        rejectionEventRepoMock.Verify(r => r.AddAsync(
            It.Is<RejectionEvent>(e => e.Reason == RejectionReason.AgentCapExceeded), It.IsAny<CancellationToken>()), Times.Once);
        reservationRepoMock.Verify(r => r.ReleaseAsync(task.Id, It.IsAny<CancellationToken>()), Times.Once,
            "the admission reservation must still be released even when the task fails at the agent-cap check");
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter StartOrchestrationAgentCapTests`
Expected: FAIL (compile error — `StartOrchestrationHandler`'s constructor doesn't accept the 4 new parameters yet).

- [ ] **Step 7: Update `StartOrchestrationHandler` — add the `MaxAgentsPerTask` check and open the tool-call-budget scope**

In `src/OrchestAI.Application/Commands/StartOrchestration/StartOrchestrationHandler.cs`, add 4 new fields/constructor parameters (`ITenantLimitsProvider`, `ICurrentTenantAccessor`, `IRejectionEventRepository`, `ITaskToolCallBudget`) alongside the existing ones from Task 7:

```csharp
    private readonly ITenantLimitsProvider _limitsProvider;
    private readonly ICurrentTenantAccessor _tenantAccessor;
    private readonly IRejectionEventRepository _rejectionEventRepository;
    private readonly ITaskToolCallBudget _toolCallBudget;
```

and add them as constructor parameters (after `reservationRepository`, before `logger`), assigning each in the constructor body the same way the existing fields are assigned.

Immediately after the existing `_logger.LogInformation("Orchestrator selected {AgentCount} agents...")` call inside the `try` block, insert:

```csharp
            var tenantId = _tenantAccessor.TenantId
                ?? throw new InvalidOperationException(
                    $"StartOrchestrationHandler ran with no ambient tenant for task {request.TaskId}.");
            var limits = await _limitsProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

            if (plan.SelectedAgents.Count > limits.MaxAgentsPerTask)
            {
                await FailWithAgentCapRejectionAsync(
                    task, plan.SelectedAgents.Count, limits.MaxAgentsPerTask, cancellationToken).ConfigureAwait(false);
                return new StartOrchestrationResponse(request.TaskId, []);
            }

            using var toolCallScope = _toolCallBudget.BeginScope(limits.MaxToolCallsPerTask);
```

Add a new private method, alongside `RunSubAgentAsync`/`RunSequentialAsync`:

```csharp
    private async Task FailWithAgentCapRejectionAsync(
        OrchestrationTask task, int actualAgentCount, int maxAgentsPerTask, CancellationToken cancellationToken)
    {
        var detailsJson = $$"""{"limit":{{maxAgentsPerTask}},"actual":{{actualAgentCount}}}""";

        try
        {
            var rejectionEvent = RejectionEvent.Create(
                RejectionReason.AgentCapExceeded, requestId: null, traceId: task.TraceId, apiKeyId: null,
                detailsJson: detailsJson);
            await _rejectionEventRepository.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist RejectionEvent for AgentCapExceeded, task {TaskId}", task.Id);
        }

        var errorMessage =
            $"Orchestrator selected {actualAgentCount} agents, exceeding the tenant cap of {maxAgentsPerTask}. " +
            "Task failed without dispatching any agents.";
        task.MarkFailed(errorMessage);
        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(task.Id, new SseEvent(
            "task_failed",
            task.Id,
            new { taskId = task.Id, errorMessage },
            DateTimeOffset.UtcNow));

        _logger.LogWarning(
            "Task {TaskId} failed at planning — agent cap exceeded ({Actual} > {Max})",
            task.Id, actualAgentCount, maxAgentsPerTask);
    }
```

Both this early-return path and the `using var toolCallScope` are inside the `try` block established in Task 7 — the reservation-release `finally` still runs on this path with no additional change needed.

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter StartOrchestrationAgentCapTests`
Expected: PASS (1/1). Re-run `StartOrchestrationReservationReleaseTests` too — its `CreateHandler` helper (Task 7) now needs the 4 new constructor arguments; update it with loose mocks (`Mock.Of<...>()`) for the new dependencies, matching the pattern already used for `ITaskAdmissionReservationRepository` there.

- [ ] **Step 9: Wire `MaxToolCallsPerTask` into `AgentBase` and all 5 concrete agents**

In `src/OrchestAI.Infrastructure/Agents/Base/AgentBase.cs`, add two new `using` statements:

```csharp
using OrchestAI.Domain.Exceptions;
```

(`OrchestAI.Domain.Interfaces` is already imported.) Add two new fields alongside the existing ones:

```csharp
    protected readonly ITaskToolCallBudget _taskToolCallBudget;
    protected readonly IRejectionEventRepository _rejectionEventRepository;
```

Add two new constructor parameters (`ITaskToolCallBudget taskToolCallBudget, IRejectionEventRepository rejectionEventRepository`) right before `ILoggerFactory loggerFactory`, and assign them in the constructor body the same way every other field is assigned.

Replace the start of `InvokeToolAsync` (everything before the existing `_eventBus.Publish(... "tool_started" ...)` call stays unchanged after this insertion — insert this block as the very first thing in the method body, before `var inputJson = ...`):

```csharp
    private async Task<McpToolResult> InvokeToolAsync(
        AgentExecution execution,
        ToolRequest request,
        CancellationToken cancellationToken)
    {
        var budgetCheck = _taskToolCallBudget.TryIncrement();
        if (!budgetCheck.Allowed)
        {
            var detailsJson = $$"""{"limit":{{budgetCheck.MaxToolCalls}},"actual":{{budgetCheck.CurrentCount}}}""";
            try
            {
                var rejectionEvent = RejectionEvent.Create(
                    RejectionReason.AgentCapExceeded, requestId: null, traceId: execution.SpanId, apiKeyId: null,
                    detailsJson: detailsJson);
                await _rejectionEventRepository.AddAsync(rejectionEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to persist RejectionEvent for AgentCapExceeded, execution {ExecutionId}", execution.Id);
            }

            throw new AgentCapExceededException(
                $"Task tool-call cap exceeded: {budgetCheck.CurrentCount} calls attempted, limit is {budgetCheck.MaxToolCalls}.");
        }

        var inputJson = string.IsNullOrWhiteSpace(request.ArgsJson) ? "{}" : request.ArgsJson;
        // ... rest of the existing method body is unchanged from here.
```

This throws from inside the `foreach (var request in turn.ToolRequests)` loop in `ExecuteAsync` — it propagates up unchanged and is caught by `ExecuteAsync`'s existing `catch (Exception ex) { ... return await FinalizeFailureAsync(execution, ex, cancellationToken); }`, producing a normal Failed `AgentExecutionResult` with no other changes needed to `ExecuteAsync` itself.

Now apply the **identical** mechanical change to `ResearchAgent.cs`, `WriterAgent.cs`, `CodeAgent.cs`, `DataAgent.cs`, and `BrowserAgent.cs`: add `ITaskToolCallBudget taskToolCallBudget, IRejectionEventRepository rejectionEventRepository` to each constructor's parameter list (in the same position as `AgentBase`'s new parameters), and pass them straight through to `base(...)`. For `ResearchAgent.cs` specifically, the constructor becomes:

```csharp
    public ResearchAgent(
        ILlmProviderFactory llmProviderFactory,
        IAgentExecutionRepository agentExecutionRepository,
        IAgentMessageRepository agentMessageRepository,
        ICostLedgerRepository costLedgerRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        ITaskCheckpointRepository checkpointRepository,
        IAgentMemoryRepository memoryRepository,
        IAgentRetryAttemptRepository agentRetryAttemptRepository,
        IPiiRedactor piiRedactor,
        IOrchestrationEventBus eventBus,
        IOptions<AgentOptions> agentOptions,
        IModelPricingCache modelPricingCache,
        IOptions<RetryPolicyOptions> retryOptions,
        IToolRegistry toolRegistry,
        ITaskToolCallBudget taskToolCallBudget,
        IRejectionEventRepository rejectionEventRepository,
        ILoggerFactory loggerFactory)
        : base(llmProviderFactory, agentExecutionRepository, agentMessageRepository,
               costLedgerRepository, mcpToolCallRepository, checkpointRepository,
               memoryRepository, agentRetryAttemptRepository, piiRedactor, eventBus, agentOptions, modelPricingCache,
               retryOptions, toolRegistry, taskToolCallBudget, rejectionEventRepository, loggerFactory)
    {
    }
```

Apply the same two-parameter insertion (before `ILoggerFactory loggerFactory`, forwarded in the same position to `base(...)`) to `WriterAgent.cs`, `CodeAgent.cs`, `DataAgent.cs`, and `BrowserAgent.cs` — each file differs only in `AgentType`/`SystemPrompt`/`AvailableToolNames`, not constructor shape, per the investigation note above.

- [ ] **Step 10: Wire DI**

In `src/OrchestAI.Infrastructure/DependencyInjection.cs`, after `services.AddSingleton<ICurrentTenantAccessor, AsyncLocalCurrentTenantAccessor>();`:

```csharp
        services.AddSingleton<ITaskToolCallBudget, AsyncLocalTaskToolCallBudget>();
```

- [ ] **Step 11: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 7's count + 6). Pay particular attention to build errors from the 5 concrete agent files and any existing agent-level tests (e.g. `ResearchAgentTests`-style files, if any exist — grep `tests/OrchestAI.Tests` for each agent class name) that construct agents directly and will need the 2 new constructor arguments (loose mocks are fine there, mirroring the pattern already used elsewhere in this plan).

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat: add orchestrator structural caps (MaxAgentsPerTask pre-dispatch, MaxToolCallsPerTask running counter)"
```

---

### Task 9: Tenant-partitioned rate limiting

**Files:**
- Create: `src/OrchestAI.API/RateLimiting/RateLimiterSetup.cs`
- Modify: `src/OrchestAI.API/Program.cs`
- Test: Create `tests/OrchestAI.Tests/API/RateLimiterPartitioningTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenantAccessor.TenantId`, `ITenantLimitsProvider.GetSnapshot` (Task 1 — the synchronous, cache-only path built specifically for this), `RejectionResponder.RespondToRateLimitAsync` (Task 2).
- Produces: `RateLimiterSetup.AddTenantRateLimiting(IServiceCollection)`, `RateLimiterSetup.BuildGlobalLimiter() : PartitionedRateLimiter<HttpContext>` (exposed `public static` deliberately, so it's directly testable without a `WebApplicationFactory`/`TestServer` — this codebase has no established full-HTTP-pipeline integration-test pattern yet, and the partitioning/exemption logic, not the ASP.NET Core plumbing around it, is what's actually worth proving).

**Design note — token bucket, chosen and why:** `Microsoft.AspNetCore.RateLimiting`/`System.Threading.RateLimiting` ship in the ASP.NET Core 8 shared framework — no new NuGet package. Token bucket (`RateLimitPartition.GetTokenBucketLimiter`) over sliding/fixed window because it allows a legitimate short burst (e.g. a dashboard loading several widgets in one page load) while still enforcing a steady average rate — a fixed/sliding window would reject that same legitimate burst outright. `TokenLimit = TokensPerPeriod = RequestsPerMinute`, `ReplenishmentPeriod = 1 minute`, `QueueLimit = 0` (reject immediately rather than queue — queueing inside the rate limiter itself would add latency this is supposed to prevent, not just cap volume). One `GlobalLimiter` (not per-endpoint `[EnableRateLimiting]` policies) applies uniformly to every `api/v1/*` route except `/health`, `/swagger`, `/api/v1/admin/*`, and any path ending `/stream` (the SSE endpoint — a long-lived connection doesn't fit request-rate semantics). Documented in ADR-015.

- [ ] **Step 1: Write the failing partitioning tests**

Create `tests/OrchestAI.Tests/API/RateLimiterPartitioningTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OrchestAI.API.RateLimiting;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.API;

public sealed class RateLimiterPartitioningTests
{
    private static HttpContext CreateContext(Guid? tenantId, int requestsPerMinute, string path = "/api/v1/tasks")
    {
        var accessorMock = new Mock<ICurrentTenantAccessor>();
        accessorMock.Setup(a => a.TenantId).Returns(tenantId);

        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetSnapshot(It.IsAny<Guid>()))
            .Returns(new ResolvedTenantLimits(requestsPerMinute, 5, 5, 50, 50m, 500m, 100));

        var services = new ServiceCollection();
        services.AddSingleton(accessorMock.Object);
        services.AddSingleton(limitsProviderMock.Object);
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Path = path;
        return context;
    }

    [Fact]
    public void BuildGlobalLimiter_ExemptPath_NeverRejects()
    {
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var context = CreateContext(tenantId: null, requestsPerMinute: 1, path: "/health");

        for (var i = 0; i < 100; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue();
        }
    }

    [Fact]
    public void BuildGlobalLimiter_SingleTenantExceedsBucket_SubsequentRequestRejected()
    {
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var context = CreateContext(Guid.NewGuid(), requestsPerMinute: 3);

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire(context);
            lease.IsAcquired.Should().BeTrue();
        }

        using var overLimitLease = limiter.AttemptAcquire(context);
        overLimitLease.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void BuildGlobalLimiter_TwoDifferentTenants_HaveIndependentBuckets()
    {
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var tenantAContext = CreateContext(Guid.NewGuid(), requestsPerMinute: 1);
        var tenantBContext = CreateContext(Guid.NewGuid(), requestsPerMinute: 1);

        using var firstLeaseA = limiter.AttemptAcquire(tenantAContext);
        firstLeaseA.IsAcquired.Should().BeTrue();
        using var secondLeaseA = limiter.AttemptAcquire(tenantAContext);
        secondLeaseA.IsAcquired.Should().BeFalse("tenant A has exhausted its own bucket");

        using var firstLeaseB = limiter.AttemptAcquire(tenantBContext);
        firstLeaseB.IsAcquired.Should().BeTrue(
            "tenant B's bucket must be completely independent of tenant A's — a shared/global bucket would incorrectly reject this too");
    }

    [Fact]
    public void BuildGlobalLimiter_NoAmbientTenant_NeverRejects()
    {
        // TenantAuthenticationMiddleware always runs before the rate limiter and rejects (401)
        // any request with no resolvable tenant before it ever reaches the limiter — this case
        // is defensive, not expected to occur in the real pipeline.
        var limiter = RateLimiterSetup.BuildGlobalLimiter();
        var context = CreateContext(tenantId: null, requestsPerMinute: 1);

        using var lease = limiter.AttemptAcquire(context);

        lease.IsAcquired.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter RateLimiterPartitioningTests`
Expected: FAIL (compile error — `RateLimiterSetup` doesn't exist yet).

- [ ] **Step 3: Implement `RateLimiterSetup`**

Create `src/OrchestAI.API/RateLimiting/RateLimiterSetup.cs`:

```csharp
using System.Threading.RateLimiting;
using OrchestAI.API.ExceptionHandling;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.API.RateLimiting;

public static class RateLimiterSetup
{
    public static IServiceCollection AddTenantRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = BuildGlobalLimiter();
            options.OnRejected = OnRejectedAsync;
        });

        return services;
    }

    // Exposed public and static deliberately, so the partitioning/exemption logic (the part
    // actually worth proving) is directly testable without a WebApplicationFactory/TestServer —
    // see RateLimiterPartitioningTests.
    public static PartitionedRateLimiter<HttpContext> BuildGlobalLimiter()
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            if (IsExemptPath(httpContext.Request.Path))
                return RateLimitPartition.GetNoLimiter("exempt");

            var tenantAccessor = httpContext.RequestServices.GetRequiredService<ICurrentTenantAccessor>();
            var tenantId = tenantAccessor.TenantId;
            if (tenantId is null)
                // TenantAuthenticationMiddleware (which runs before this limiter — see Program.cs)
                // already rejects any request with no resolvable tenant. Defensive, not expected.
                return RateLimitPartition.GetNoLimiter("no-tenant");

            var limitsProvider = httpContext.RequestServices.GetRequiredService<ITenantLimitsProvider>();
            var limits = limitsProvider.GetSnapshot(tenantId.Value);

            return RateLimitPartition.GetTokenBucketLimiter(tenantId.Value.ToString(), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = limits.RequestsPerMinute,
                TokensPerPeriod = limits.RequestsPerMinute,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfterSeconds = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

        var responder = context.HttpContext.RequestServices.GetRequiredService<RejectionResponder>();
        await responder.RespondToRateLimitAsync(context.HttpContext, retryAfterSeconds, cancellationToken).ConfigureAwait(false);
    }

    public static bool IsExemptPath(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/swagger") ||
        path.StartsWithSegments("/api/v1/admin") ||
        (path.Value?.EndsWith("/stream", StringComparison.OrdinalIgnoreCase) ?? false);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter RateLimiterPartitioningTests`
Expected: PASS (4/4).

- [ ] **Step 5: Wire into `Program.cs`**

Add the using:

```csharp
using OrchestAI.API.RateLimiting;
```

Add after the `builder.Services.AddExceptionHandler<TenantLimitExceededExceptionHandler>();` line Task 2 inserted (i.e., anywhere after `TenantAuthenticationMiddleware`'s own registration, alongside Task 2's other API-layer registrations — exact position among them doesn't matter):

```csharp
    builder.Services.AddTenantRateLimiting();
```

Insert `app.UseRateLimiter();` between the existing `app.UseMiddleware<TenantAuthenticationMiddleware>();` and `app.MapControllers();` lines — ordering matters: the limiter's partition-key resolver reads `ICurrentTenantAccessor.TenantId`, which is only populated once `TenantAuthenticationMiddleware` has run:

```csharp
    app.UseMiddleware<TenantAuthenticationMiddleware>();
    app.UseRateLimiter();
    app.MapControllers();
```

- [ ] **Step 6: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 8's count + 4).

- [ ] **Step 7: Manual smoke check**

Run the API and, using a bootstrapped tenant/API key, call any `api/v1` endpoint (e.g. `GET /api/v1/tasks/{id}`) more than `TenantLimitsDefaults.RequestsPerMinute` (120) times within a minute — confirm the 121st+ call returns `429` with a `Retry-After` header and `{"reason":"RateLimited", ...}` body, and that `GET /api/v1/rejections` (Task 2) then shows the corresponding `RejectionEvent`. Confirm `/health` remains unaffected regardless of call volume.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add tenant-partitioned token-bucket rate limiting"
```

---

### Task 10: Per-tenant queue backpressure for `IEvalRunQueue`

**Files:**
- Modify: `src/OrchestAI.Infrastructure/Eval/InMemoryEvalRunQueue.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/InMemoryEvalRunQueueTests.cs`

**Interfaces:**
- Consumes: `ITenantLimitsProvider.GetAsync` (Task 1), `TenantLimitExceededException` (Task 2).
- No interface signature changes — `IEvalRunQueue.EnqueueAsync`/`DequeueAsync` are unchanged; `EnqueueAsync` simply can now throw `TenantLimitExceededException`. Both existing callers (`RunEvalSuiteHandler`, `RequestPostHocScoringHandler` — confirmed via investigation to share this one queue) already let exceptions from `_queue.EnqueueAsync` propagate uncaught, so this exception reaches the global `TenantLimitExceededExceptionHandler` (Task 2) with **zero controller changes needed** at either call site.

**Design note:** the underlying `Channel<EvalRunQueueItem>` stays one global unbounded FIFO — not replaced with N per-tenant channels. A per-tenant `ConcurrentDictionary<Guid, int>` depth counter is checked before writing and decremented on dequeue (queue depth means "waiting to be picked up," not "in flight" — a distinct concern from `MaxConcurrentTasks`, which governs concurrent task *execution*, an entirely different limit on an entirely different resource). Because the channel itself is one global FIFO, "FIFO within a tenant's own partition" (brainstorming clarification #5) holds automatically — a tenant's own items are a FIFO subsequence of a FIFO sequence, with no reordering possible. The depth check is deliberately check-then-increment, not a single atomic CAS like the admission transaction (Task 6) — a race between two simultaneous `EnqueueAsync` calls for the same tenant at the exact depth boundary could, in the worst case, transiently admit one more item than the configured limit. This is an accepted, documented tradeoff (unlike the budget/concurrency race, which the brief explicitly required to be airtight): the failure direction here is "queue very slightly over capacity for one item," not a security or cost overshoot, and self-corrects on the next dequeue.

- [ ] **Step 1: Write the failing queue tests**

Create `tests/OrchestAI.Tests/Infrastructure/InMemoryEvalRunQueueTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class InMemoryEvalRunQueueTests
{
    private static InMemoryEvalRunQueue CreateQueue(int maxQueueDepth)
    {
        var limitsProviderMock = new Mock<ITenantLimitsProvider>();
        limitsProviderMock.Setup(p => p.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedTenantLimits(120, 5, 5, 50, 50m, 500m, maxQueueDepth));
        return new InMemoryEvalRunQueue(limitsProviderMock.Object);
    }

    [Fact]
    public async Task EnqueueAsync_UnderLimit_Succeeds()
    {
        var queue = CreateQueue(maxQueueDepth: 2);
        var tenantId = Guid.NewGuid();

        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);
        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);
    }

    [Fact]
    public async Task EnqueueAsync_AtLimit_ThrowsTenantLimitExceededExceptionWithQueueBackpressureReason()
    {
        var queue = CreateQueue(maxQueueDepth: 1);
        var tenantId = Guid.NewGuid();
        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        var act = () => queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        var exception = await act.Should().ThrowAsync<TenantLimitExceededException>();
        exception.Which.Reason.Should().Be(RejectionReason.QueueBackpressure);
        exception.Which.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task DequeueAsync_DecrementsDepth_AllowingSubsequentEnqueue()
    {
        var queue = CreateQueue(maxQueueDepth: 1);
        var tenantId = Guid.NewGuid();
        await queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        await queue.DequeueAsync();
        var act = () => queue.EnqueueAsync(Guid.NewGuid(), tenantId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnqueueAsync_TwoTenants_HaveIndependentDepthCounters()
    {
        var queue = CreateQueue(maxQueueDepth: 1);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await queue.EnqueueAsync(Guid.NewGuid(), tenantA);

        var act = () => queue.EnqueueAsync(Guid.NewGuid(), tenantB);

        await act.Should().NotThrowAsync(
            "tenant B's queue depth must be completely independent of tenant A's — a shared/global counter would incorrectly reject this too");
    }

    [Fact]
    public async Task DequeueAsync_ReturnsItemsInFifoOrderAcrossInterleavedTenants()
    {
        var queue = CreateQueue(maxQueueDepth: 10);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        await queue.EnqueueAsync(first, tenantA);
        await queue.EnqueueAsync(second, tenantB);
        await queue.EnqueueAsync(third, tenantA);

        (await queue.DequeueAsync()).EvalRunId.Should().Be(first);
        (await queue.DequeueAsync()).EvalRunId.Should().Be(second);
        (await queue.DequeueAsync()).EvalRunId.Should().Be(third);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter InMemoryEvalRunQueueTests`
Expected: FAIL (compile error — the constructor doesn't accept `ITenantLimitsProvider` yet, and `EnqueueAsync` never throws).

- [ ] **Step 3: Replace `InMemoryEvalRunQueue.cs` in full**

Replace `src/OrchestAI.Infrastructure/Eval/InMemoryEvalRunQueue.cs`:

```csharp
using System.Collections.Concurrent;
using System.Threading.Channels;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Eval;

// Same underlying primitive as before (System.Threading.Channels) — one global unbounded
// channel, shared by live-suite eval runs and post-hoc scoring alike, which is what makes
// per-tenant FIFO ordering automatic (a subsequence of one FIFO sequence is FIFO, with no
// reordering possible). What's new in Week 11 is a per-tenant depth counter: EnqueueAsync
// checks it against TenantLimits.MaxQueueDepth before writing, DequeueAsync decrements it once
// EvalRunBackgroundWorker claims the item — queue depth means "waiting," a different concern
// from MaxConcurrentTasks (concurrent task EXECUTION). See ADR-015 for the accepted
// check-then-increment race tradeoff (not a CAS like the admission transaction — the failure
// direction here is "very slightly over capacity for one item," not a cost/security overshoot).
public sealed class InMemoryEvalRunQueue : IEvalRunQueue
{
    private readonly Channel<EvalRunQueueItem> _channel = Channel.CreateUnbounded<EvalRunQueueItem>();
    private readonly ConcurrentDictionary<Guid, int> _depthByTenant = new();
    private readonly ITenantLimitsProvider _limitsProvider;

    public InMemoryEvalRunQueue(ITenantLimitsProvider limitsProvider) => _limitsProvider = limitsProvider;

    public async Task EnqueueAsync(Guid evalRunId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var limits = await _limitsProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var currentDepth = _depthByTenant.GetOrAdd(tenantId, 0);

        if (currentDepth >= limits.MaxQueueDepth)
        {
            var detailsJson = $$"""{"limit":{{limits.MaxQueueDepth}},"actual":{{currentDepth}},"queueDepth":{{currentDepth}}}""";
            throw new TenantLimitExceededException(
                tenantId, RejectionReason.QueueBackpressure,
                "Tenant background-work queue is at capacity — try again later.",
                retryAfterSeconds: 30, detailsJson: detailsJson);
        }

        _depthByTenant.AddOrUpdate(tenantId, 1, (_, current) => current + 1);
        await _channel.Writer.WriteAsync(new EvalRunQueueItem(evalRunId, tenantId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<EvalRunQueueItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var item = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        _depthByTenant.AddOrUpdate(item.TenantId, 0, (_, current) => Math.Max(0, current - 1));
        return item;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter InMemoryEvalRunQueueTests`
Expected: PASS (5/5).

- [ ] **Step 5: Run full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 9's count + 5). `InMemoryEvalRunQueue`'s DI registration (`services.AddSingleton<IEvalRunQueue, InMemoryEvalRunQueue>();`) needs no change — the constructor now requires `ITenantLimitsProvider`, which is already registered as a singleton (Task 1), so DI resolves it automatically.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add per-tenant queue backpressure to IEvalRunQueue"
```

---

### Task 11: Concurrency-race, TTL-expiry, and cross-tenant proof tests

**Files:**
- Create: `tests/OrchestAI.Tests/Infrastructure/AdmissionConcurrencyRaceTests.cs`
- Create: `tests/OrchestAI.Tests/Infrastructure/IdempotencyRecordExpiryTests.cs`

**Purpose:** Tasks 1-10 build every mechanism; this task is where the two hardest-to-fake guarantees get proven empirically rather than assumed — genuinely concurrent admissions (not a single shared transaction, which trivially serializes and proves nothing about a real race), and TTL-based idempotency-key expiry. This directly answers the brief's "concurrent budget race test," "idempotency TTL expiry," and part of "cross-tenant isolation of new runtime state" requirements — the remaining cross-tenant pieces (rate limiter, queue) were already proven in Tasks 9 and 10 respectively.

**Before writing `AdmissionConcurrencyRaceTests.cs`:** read `tests/OrchestAI.Tests/Infrastructure/CostRollupUniqueIndexIntegrationTests.cs` or `TenantBackfillIntegrationTests.cs` in full first (referenced but not read during this plan's investigation) to confirm this codebase's established "real committed rows + explicit cleanup" integration-test convention, as distinct from `TenantFilterExecuteDeleteTests`' single-rolled-back-transaction pattern (which cannot prove anything about two independent transactions racing each other — they'd trivially serialize on one shared connection). Adjust the pattern below to match if it differs.

**Known, documented gap — not fixed this task:** a full `AgentBase`-level integration test proving `MaxToolCallsPerTask` end-to-end (a fake/real LLM turn requesting more tool calls than the cap, confirming the task fails cleanly) is valuable but requires mocking substantially more of the agent-execution stack (`ILlmProvider`, `IMcpTool`, `ToolRegistry`, retry policy) than this plan's investigation directly read. `MaxToolCallsPerTask` is proven at the unit level by `AsyncLocalTaskToolCallBudgetTests` (Task 8) and the exception-to-failure wiring reuses `AgentBase.ExecuteAsync`'s existing, already-tested catch-all path unchanged. Flag this explicitly as a good follow-up task rather than fabricate an untested mock setup for classes this plan never read in full — matches `DESIGN_PRINCIPLES.md`'s "empirical verification over plausible-sounding" standard: an honest gap beats speculative test code that might not even compile.

- [ ] **Step 1: Write and run the concurrent admission race tests (real Postgres, genuinely parallel)**

Create `tests/OrchestAI.Tests/Infrastructure/AdmissionConcurrencyRaceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Genuinely concurrent — each parallel call opens its OWN transaction against the real local
// dev Postgres, exactly like two simultaneous HTTP requests would in production. Rows are
// committed for real and explicitly cleaned up in a finally (see this task's note on why the
// TenantFilterExecuteDeleteTests-style single-rolled-back-transaction pattern can't be used
// here — it would trivially serialize both "concurrent" calls on one shared connection).
// Requires the local dev Postgres up and migrated through Task 10.
public sealed class AdmissionConcurrencyRaceTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    private static IDbContextFactory<AppDbContext> CreateRealFactory(ICurrentTenantAccessor accessor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(accessor);
        services.AddSingleton<TenantScopingInterceptor>();
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(ConnectionString);
            options.AddInterceptors(sp.GetRequiredService<TenantScopingInterceptor>());
        });
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AppDbContext>>();
    }

    private static async Task<(Tenant Tenant, Guid UserId)> SeedTenantAsync(
        IDbContextFactory<AppDbContext> factory, ICurrentTenantAccessor accessor, string suffix)
    {
        var tenant = Tenant.Create($"Race-{suffix}", $"race-{suffix}");
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
        }

        Guid userId;
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var user = TestUserFactory.Create($"race-{suffix}@test.local");
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            userId = user.Id;
        }

        return (tenant, userId);
    }

    private static async Task CleanUpAsync(IDbContextFactory<AppDbContext> factory, Guid tenantId)
    {
        await using var ctx = await factory.CreateDbContextAsync();
        // Explicit, dependency-order deletes rather than relying on unverified cascade
        // configuration — safe regardless of what each entity's configuration actually specifies.
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"TaskAdmissionReservations\" WHERE \"TenantId\" = {tenantId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"Users\" WHERE \"Id\" IN (SELECT \"UserId\" FROM \"OrchestrationTasks\" WHERE \"TenantId\" = {tenantId})");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"OrchestrationTasks\" WHERE \"TenantId\" = {tenantId}");
        await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Tenants\" WHERE \"Id\" = {tenantId}");
    }

    [Fact]
    public async Task TryAdmitAsync_TwoConcurrentAdmissionsSameTask_ExactlyOneSucceeds()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = CreateRealFactory(accessor);
        var (tenant, userId) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));

        OrchestrationTask task;
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            task = OrchestrationTask.Create(userId, "T", "P", false);
            ctx.OrchestrationTasks.Add(task);
            await ctx.SaveChangesAsync();
        }

        try
        {
            var repository = new OrchestrationAdmissionRepository(factory);

            Task<AdmissionResult> AdmitAsync() => Task.Run(async () =>
            {
                using (accessor.SetTenant(tenant.Id))
                {
                    return await repository.TryAdmitAsync(
                        task.Id, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 1m,
                        dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                        reservationStaleness: TimeSpan.FromMinutes(30));
                }
            });

            var results = await Task.WhenAll(AdmitAsync(), AdmitAsync());

            results.Count(r => r.Admitted).Should().Be(1,
                "two simultaneous admission attempts against the same Pending task must never both succeed — the CAS must serialize them");
            results.Count(r => !r.Admitted && r.FailureReason == AdmissionFailureReason.TaskNotPending).Should().Be(1);
        }
        finally
        {
            await CleanUpAsync(factory, tenant.Id);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_TwoConcurrentAdmissionsWouldTogetherExceedBudget_ExactlyOneSucceeds()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = CreateRealFactory(accessor);
        var (tenant, userId) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));

        OrchestrationTask taskA, taskB;
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            taskA = OrchestrationTask.Create(userId, "A", "P", false);
            taskB = OrchestrationTask.Create(userId, "B", "P", false);
            ctx.OrchestrationTasks.AddRange(taskA, taskB);
            await ctx.SaveChangesAsync();
        }

        try
        {
            var repository = new OrchestrationAdmissionRepository(factory);

            // Each individually fits within a $10 budget (actual spend $0) — but $6 + $6 = $12
            // together exceeds it. A naive read-then-check-then-write would let both pass.
            Task<AdmissionResult> AdmitAsync(Guid taskId) => Task.Run(async () =>
            {
                using (accessor.SetTenant(tenant.Id))
                {
                    return await repository.TryAdmitAsync(
                        taskId, tenant.Id, maxConcurrentTasks: 5, reservationAmountUsd: 6m,
                        dailyBudgetUsd: 10m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                        reservationStaleness: TimeSpan.FromMinutes(30));
                }
            });

            var results = await Task.WhenAll(AdmitAsync(taskA.Id), AdmitAsync(taskB.Id));

            results.Count(r => r.Admitted).Should().Be(1,
                "two simultaneous admissions that individually fit but together exceed the budget must not both be admitted — " +
                "this is the atomic-reservation proof, not just evidence that a single-request check exists");
            results.Count(r => !r.Admitted && r.FailureReason == AdmissionFailureReason.BudgetExceeded).Should().Be(1);
        }
        finally
        {
            await CleanUpAsync(factory, tenant.Id);
        }
    }

    [Fact]
    public async Task TryAdmitAsync_TenantAAtConcurrencyLimit_DoesNotAffectTenantBsAdmission()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = CreateRealFactory(accessor);
        var (tenantA, userIdA) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));
        var (tenantB, userIdB) = await SeedTenantAsync(factory, accessor, Guid.NewGuid().ToString("N"));

        OrchestrationTask taskA, alreadyRunningTaskA, taskB;
        using (accessor.SetTenant(tenantA.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            taskA = OrchestrationTask.Create(userIdA, "A", "P", false);
            alreadyRunningTaskA = OrchestrationTask.Create(userIdA, "A-running", "P", false);
            ctx.OrchestrationTasks.AddRange(taskA, alreadyRunningTaskA);
            await ctx.SaveChangesAsync();
            ctx.TaskAdmissionReservations.Add(TaskAdmissionReservation.Create(alreadyRunningTaskA.Id, 1m));
            await ctx.SaveChangesAsync();
        }
        using (accessor.SetTenant(tenantB.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            taskB = OrchestrationTask.Create(userIdB, "B", "P", false);
            ctx.OrchestrationTasks.Add(taskB);
            await ctx.SaveChangesAsync();
        }

        try
        {
            var repository = new OrchestrationAdmissionRepository(factory);

            AdmissionResult resultA, resultB;
            using (accessor.SetTenant(tenantA.Id))
            {
                resultA = await repository.TryAdmitAsync(
                    taskA.Id, tenantA.Id, maxConcurrentTasks: 1, reservationAmountUsd: 1m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }
            using (accessor.SetTenant(tenantB.Id))
            {
                resultB = await repository.TryAdmitAsync(
                    taskB.Id, tenantB.Id, maxConcurrentTasks: 1, reservationAmountUsd: 1m,
                    dailyBudgetUsd: 50m, actualDailySpendUsd: 0m, monthlyBudgetUsd: 500m, actualMonthlySpendUsd: 0m,
                    reservationStaleness: TimeSpan.FromMinutes(30));
            }

            resultA.Admitted.Should().BeFalse("tenant A is already at its own concurrency limit");
            resultB.Admitted.Should().BeTrue(
                "tenant B has no reservations at all — tenant A's exhausted limit must never leak into tenant B's admission");
        }
        finally
        {
            await CleanUpAsync(factory, tenantA.Id);
            await CleanUpAsync(factory, tenantB.Id);
        }
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter AdmissionConcurrencyRaceTests`
Expected: PASS (3/3) — requires the local dev Postgres running and migrated through Task 10. If any test is flaky (a race that only sometimes reproduces), that itself is a signal to re-examine `OrchestrationAdmissionRepository`'s locking — do not mark it "probably fine" without a real, reproducible pass.

- [ ] **Step 2: Write and run the idempotency-key TTL-expiry tests (InMemory provider — no locking involved, so no real Postgres needed)**

Create `tests/OrchestAI.Tests/Infrastructure/IdempotencyRecordExpiryTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class IdempotencyRecordExpiryTests
{
    private sealed class SingleContextFactory(DbContextOptions<AppDbContext> options, ICurrentTenantAccessor accessor)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options, accessor);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) CreateInMemoryFactory()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return (new SingleContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task GetByKeyAsync_RecordExpired_ReturnsNull()
    {
        var (factory, accessor) = CreateInMemoryFactory();
        var tenant = Tenant.Create("Acme", $"acme-{Guid.NewGuid():N}");
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
        }

        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            // Create's ttl is relative to "now" — a negative TimeSpan produces an already-past ExpiresAt.
            var expired = IdempotencyRecord.Create("key-1", Guid.NewGuid(), "hash", TimeSpan.FromSeconds(-1));
            ctx.IdempotencyRecords.Add(expired);
            await ctx.SaveChangesAsync();
        }

        IdempotencyRecord? found;
        using (accessor.SetTenant(tenant.Id))
        {
            var repository = new IdempotencyRecordRepository(factory);
            found = await repository.GetByKeyAsync("key-1");
        }

        found.Should().BeNull("an expired idempotency key must be treated as absent, freeing it for reuse on a brand-new task");
    }

    [Fact]
    public async Task GetByKeyAsync_RecordNotYetExpired_ReturnsIt()
    {
        var (factory, accessor) = CreateInMemoryFactory();
        var tenant = Tenant.Create("Acme", $"acme-{Guid.NewGuid():N}");
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
        }

        var taskId = Guid.NewGuid();
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var record = IdempotencyRecord.Create("key-1", taskId, "hash", TimeSpan.FromHours(24));
            ctx.IdempotencyRecords.Add(record);
            await ctx.SaveChangesAsync();
        }

        IdempotencyRecord? found;
        using (accessor.SetTenant(tenant.Id))
        {
            var repository = new IdempotencyRecordRepository(factory);
            found = await repository.GetByKeyAsync("key-1");
        }

        found.Should().NotBeNull();
        found!.TaskId.Should().Be(taskId);
    }
}
```

Run: `dotnet test tests/OrchestAI.Tests --filter IdempotencyRecordExpiryTests`
Expected: PASS (2/2).

- [ ] **Step 3: Run the full test suite and build**

Run: `dotnet build && dotnet test`
Expected: 0 warnings, 0 errors, all tests pass (new count = Task 10's count + 5). Confirm the total against the baseline recorded before Task 1 — this is the "keep the 0-warning, all-green bar" checkpoint the brief requires.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: add concurrent admission race, cross-tenant isolation, and idempotency-TTL proof tests"
```

---

### Task 12: ADR-015 documentation

**Files:**
- Modify: `DECISIONS.md` (append `## ADR-015: Abuse and Cost Protection`, matching ADR-014's established structure: numbered confirmations, an "Implementation notes" section for any deviations discovered during Tasks 1-11, and a "Trigger for revisiting" section)

This is a writing task, not a TDD task — no failing test to write first. Every decision below was already made and justified earlier in this plan; ADR-015's job is to record it in `DECISIONS.md` where future weeks look, not to re-derive it. Write each section below, citing the task where the decision was actually implemented.

- [ ] **Step 1: Write ADR-015, covering every point below**

1. **Rate-limiting algorithm and why** — token bucket via `Microsoft.AspNetCore.RateLimiting`, tenant-partitioned, chosen over sliding/fixed window because it allows a legitimate short burst while still enforcing a steady average rate. Cite Task 9's design note verbatim for the exact `TokenLimit`/`ReplenishmentPeriod`/`QueueLimit` reasoning.

2. **Why in-memory/single-instance state is acceptable for now** — the rate limiter's `PartitionedRateLimiter`, the `ITaskToolCallBudget` `AsyncLocal` counter, and `IEvalRunQueue`'s per-tenant depth `ConcurrentDictionary` all live in single-process memory; a horizontally-scaled deployment would need Redis-backed (or similar) shared state for all three. Explicitly out of scope this week per the brief's non-goals — document as a known limitation with "the first time a second API instance is deployed" as the trigger to revisit, not an oversight.

3. **The two-part cost-cap model** — (a) the per-task structural ceiling (`MaxAgentsPerTask`, checked pre-dispatch; `MaxToolCallsPerTask`, a running counter checked per call, since tool calls are never known before they happen — cite Task 8's design note for exactly why these two dimensions are enforced completely differently despite looking like "the same kind of cap"); (b) the tenant-level running budget (Task 6), checked via atomic reservation at admission time against `actual spend (CostRollup + live CostLedger, ADR-011's hybrid pattern) + SUM(active, non-stale TaskAdmissionReservation rows)`.

4. **The atomic-reservation mechanism** — a `SELECT ... FOR UPDATE` row lock on the tenant's own `Tenants` row, serializing concurrent admissions *per tenant* (never cross-tenant), inside one explicit DB transaction that also performs the `Pending -> Running` CAS (`ExecuteUpdateAsync`) and the concurrency/budget checks — all-or-nothing, confirmed by `AdmissionConcurrencyRaceTests` (Task 11) against real concurrent transactions, not a single shared one. Cite Task 6's "why this is one repository method, not a composition of smaller ones" note directly — this is the load-bearing architectural justification.

5. **Reservations vs. the immutable cost ledger** — cross-reference `DESIGN_PRINCIPLES.md`'s new "Operational state vs. audit state" section (added this week, not scoped to Week 11 alone) and apply it concretely here: `TaskAdmissionReservation` is operational state, deleted in full on release, never written back into `CostLedger`/`CostRollup`; those remain the untouched, immutable audit trail exactly as ADR-011 established them. State the crash-recovery TTL mechanism explicitly (`AbuseProtectionOptions.ReservationStalenessMinutes`) as the accepted single-instance-architecture limitation it is — no reconciliation service exists or is planned this week.

6. **The unified response contract** — `429` + `Retry-After` + `{reason, detail, ...}` JSON, one shared builder (`RejectionResponder`) called from two entry points (`OnRejected` for `RateLimited`; `TenantLimitExceededExceptionHandler` for `ConcurrencyExceeded`/`BudgetExceeded`/`QueueBackpressure`). Document explicitly that `AgentCapExceeded` does **not** go through this HTTP contract at all — it happens inside a detached background dispatch with no HTTP response in flight, and is instead surfaced via task status (`Failed`) + SSE + an independently-written `RejectionEvent`. This asymmetry is deliberate, not an inconsistency — explain why (Task 8's design note covers the mechanics; state the product reasoning here: the caller already received their `202` for `/start` before this rejection can even be known).

7. **The reject-vs-truncate decision for orchestrator-level caps** — never silently truncate a plan or drop a tool call's result and pretend success; always fail the task cleanly (`Failed` status, specific error message, `RejectionEvent`). State explicitly why: a partially-executed task that looks `Completed` to the eval/scoring layer is a worse failure mode than a task that's visibly `Failed` — this was the brief's own stated rationale, restate it here since it's the single most important reject-vs-truncate justification in the whole design.

8. **The idempotency-key TTL and behavior** — `POST /tasks` only (not `/start`, which is protected by the now-atomic `Pending -> Running` CAS instead), default 24-hour TTL (`AbuseProtectionOptions.IdempotencyKeyTtlHours`), mismatched-payload reuse of the same key returns `409 Conflict`. State the accepted concurrent-first-use race explicitly (Task 4's design note) — two genuinely simultaneous first-uses of a brand-new key could both pass the "not found" check and race into the unique `(TenantId, IdempotencyKey)` index; documented as acceptable given the failure mode (one extra valid task, not a security/budget problem), unlike the admission race which required true atomicity.

9. **Deliberate deviations from the brief's literal domain-model field list** — `TenantLimits.MaxQueueDepth` was added (the brief required per-tenant queue-depth limits in its enforcement-points list but omitted the corresponding `TenantLimits` field); `TenantLimits.Create(tenantId, ...)` is a third named exception to ADR-014's "no `ITenantScoped` factory takes `TenantId`" rule (same shape as `ApiKey.Create`/`CostRollup.Create` — admin-only, no ambient tenant to bypass); `AbuseProtectionOptions` lives in `Application.Configuration`, not `Infrastructure.Configuration`, to avoid repeating the exact `Application`-depends-on-`Infrastructure` layering violation Week 9's `EvalOptions` already hit once (cite the fix proactively, not reactively, this time).

- [ ] **Step 2: Implementation notes — fill in during/after Tasks 1-11, not before**

Following ADR-014's precedent (its "Implementation notes" section records four real deviations discovered only once the work was actually done), leave a placeholder section header now and fill it in honestly once Tasks 1-11 are actually complete: any place a task's plan turned out to be wrong once real code/tests forced a decision, any test that failed for a reason this plan didn't anticipate, any file that needed to move layers. This section's entire value is that it's written from what actually happened, not what was planned — do not pre-write it or leave it as fabricated-sounding "no surprises" boilerplate if there genuinely were surprises.

- [ ] **Step 3: Trigger for revisiting**

Include at minimum: "the first time a second API instance is deployed (in-memory rate limiter/tool-call-budget/queue-depth state all need to become shared/distributed)"; "the first time `AssumedCostPerToolCallUsd`'s flat-rate estimate proves too conservative or not conservative enough against real usage data — replace `ConservativeBudgetEstimator` with a smarter `IBudgetEstimator` implementation, not a change to admission logic"; "the first time orphaned `TaskAdmissionReservation` rows from crashed processes become numerous enough to matter — build the reconciliation sweep explicitly deferred this week."

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: add ADR-015 for abuse and cost protection"
```

---

## Final verification (after Task 12)

- [ ] Run `dotnet build` — 0 warnings, 0 errors.
- [ ] Run `dotnet test` — confirm the full suite is green and report the final count against the pre-Task-1 baseline.
- [ ] Confirm every one of the brief's required tests (Tests section) maps to an actual test written somewhere in Tasks 1-11: rate limit → `RateLimiterPartitioningTests` (Task 9); concurrency limit → `OrchestrationAdmissionRepositoryTests`/`AdmissionConcurrencyRaceTests` (Tasks 6, 11); budget exceeded + concurrent budget race → `AdmissionConcurrencyRaceTests` (Task 11); idempotency (duplicate + TTL expiry + mismatched payload) → `CreateOrchestrationTaskIdempotencyTests` (Task 4) + `IdempotencyRecordExpiryTests` (Task 11); orchestrator cap (agents) → `StartOrchestrationAgentCapTests` (Task 8); cross-tenant isolation → `RateLimiterPartitioningTests` (Task 9), `InMemoryEvalRunQueueTests` (Task 10), `AdmissionConcurrencyRaceTests` (Task 11); rejections queryable → `GetRejectionsHandlerTests` (Task 2). Note `MaxToolCallsPerTask`'s AgentBase-level integration gap explicitly (Task 11's documented limitation) rather than silently treating it as covered.
- [ ] Confirm `git branch --contains` shows every commit reachable from the working branch tip, per this project's standing subagent-dispatch safeguard.

---

### Task 13 (deferred, scheduled — not part of this week's execution pass): `AgentBase`-level `MaxToolCallsPerTask` integration test

**Status:** explicitly deferred, confirmed with the user (2026-07-14), not silently dropped. Tasks 1-12 leave `MaxToolCallsPerTask` proven only at the unit level (`AsyncLocalTaskToolCallBudgetTests`, Task 8) plus reasoning about how `AgentCapExceededException` propagates through `AgentBase.ExecuteAsync`'s existing catch-all — there is no real end-to-end proof that a sub-agent's tool-call loop actually stops and fails the task cleanly once the cap is hit. This is one of exactly two structural-cap enforcement mechanisms this week exists to deliver (`MaxAgentsPerTask` is the other, and *is* fully integration-tested — Task 8). A cost-protection mechanism with one of its two halves unproven at integration level is not "Week 11 done," it is "Week 11 mostly done" — this task is what closes that gap, and per the user's explicit instruction it must be addressed before Week 11 is treated as fully closed, ahead of starting Week 12 — not bundled into unrelated future work where it is easy to deprioritize.

**Why this wasn't just written into Task 8:** writing a correct `AgentBase`-level integration test requires understanding `ILlmProvider`, `IMcpTool`, and `ToolRegistry` well enough to mock a multi-turn tool-use conversation realistically — none of which this plan's investigation read in full. A rushed mock built on an unverified understanding of that stack risks testing the wrong thing while appearing to pass, which is a worse outcome than an honestly documented gap.

- [ ] **Step 1: Read the agent-execution stack in full before writing anything**

Read, in full, not excerpted: `src/OrchestAI.Domain/Interfaces/ILlmProvider.cs`, `src/OrchestAI.Domain/Interfaces/IMcpTool.cs`, `src/OrchestAI.Domain/Interfaces/IToolRegistry.cs`, `src/OrchestAI.Infrastructure/Tools/ToolRegistry.cs`, and the `AgentConversation`/`AgentTurn`/`ToolRequest`/`ToolResultContent` model types referenced by `AgentBase.ExecuteAsync` (`src/OrchestAI.Domain/Models/`). Confirm exactly what a fake `ILlmProvider.SendAsync` needs to return to make `AgentBase.ExecuteAsync`'s agentic loop request N tool calls across one or more turns — this is the actual mechanism under test.

- [ ] **Step 2: Write a real integration test**

Using a concrete `ResearchAgent` (or another concrete agent — pick whichever has the simplest `AvailableToolNames`) with a fake `ILlmProvider` that returns `turn.StopReason == "tool_use"` with more `ToolRequests` than `MaxToolCallsPerTask` across its configured `MaxAgenticIterations`, and a `TenantLimits`/`ITaskToolCallBudget` scope opened with a small cap (e.g. 2): confirm the agent's `AgentExecutionResult.Success` is `false`, the error reflects the tool-call cap, and — driven through `StartOrchestrationHandler` rather than the agent in isolation — the owning `OrchestrationTask` ends in `Failed` status with a `RejectionEvent` recorded (`Reason == AgentCapExceeded`), never `Completed`.

- [ ] **Step 3: Run and commit**

Run the new test file, confirm the full suite stays green, commit with a message referencing this deferred task and the original Week 11 plan.
