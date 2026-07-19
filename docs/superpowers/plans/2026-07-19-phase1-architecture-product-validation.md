# Phase 1: Architecture & Product Validation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **This is a validation-and-fix pass, not a feature week.** Its own investigation phase (see below) found two previously-undiscovered, live-verified correctness bugs by actually running the app end-to-end against a real isolated instance — not by reading code and reasoning about what "should" work. Do not skip the empirical-verification steps in this plan (actually standing up Postgres + the API, actually opening an SSE connection, actually watching a task's status) in favor of static reasoning, for the same reason Weeks 10-12 kept finding real bugs that way and none by review alone.

**Goal:** Prove the full Weeks 7-12 system actually works end-to-end through its real HTTP/SSE contract (not just via isolated unit tests), fix the two concrete breaks this investigation found, close the one deferred Week 11 test gap, and bring the README and DECISIONS.md in line with what the system actually does and what was explicitly decided about the frontend's future.

**Architecture:** No new architectural concepts. Task 1 adds one small, narrowly-scoped mechanism (a short-lived, single-use, task-and-tenant-bound stream ticket) to work around a hard browser API limitation (`EventSource` cannot set request headers) — following this codebase's existing TTL-bound-ephemeral-artifact pattern (`IdempotencyKeyTtlHours`, admission reservation staleness). Task 2 is a pure bug fix inside an existing handler. Tasks 3-5 are test/documentation work with no production code surface beyond the test itself.

**Tech Stack:** No new dependencies. Task 1 uses `Microsoft.Extensions.Caching.Memory.IMemoryCache` (already transitively available via ASP.NET Core's default hosting, not currently used elsewhere in this codebase for ephemeral state — confirmed via grep — but the standard, dependency-free choice for single-instance, short-TTL, non-durable state, consistent with the existing single-instance assumption `InMemoryOrchestrationEventBus` already makes).

## Global Constraints

- Every new cross-cutting interface follows the existing Domain-defines/Infrastructure-implements split (`ICurrentTenantAccessor`, `ITenantLimitsProvider`, etc.).
- `LayeringTests` must keep passing untouched — no new `OrchestAI.Infrastructure` type may reference `Microsoft.AspNetCore.Mvc`/`Microsoft.AspNetCore.Http`.
- No PR-based workflow; `main` is merged-to-locally-and-pushed as in Weeks 1-12 (see `CONTRIBUTING.md`). CI (`.github/workflows/ci.yml`) already runs on push — nothing about that changes here.
- The frontend-investment decision (see below) is **already made** — do not revisit it mid-execution. It governs only how far Task 6 (frontend labeling) goes; it does not gate Tasks 1-2 (those are correctness fixes needed regardless of the frontend's product status).
- Do not touch `ADR-011` through `ADR-016`'s existing content — this plan only appends a new confirmation to `ADR-014` (Task 5) and documents new implementation notes as its own entries, per the existing convention of never rewriting a shipped ADR's history.

---

## Investigation summary (already done — do not re-derive)

Performed against a real, isolated, live instance (separate Postgres container, port 5434; API on port 5090 with `Anthropic:ApiKey` set to a syntactically-valid placeholder; a real tenant + API key minted via the admin bootstrap endpoint) — not static code reading:

- **Confirmed via `git log --follow -- frontend/src`:** the frontend was NOT frozen since Week 6 as originally assumed. It received feature work through Week 9 (evals tab `9e7fcc1`, observability tab `a76c1ed`, post-hoc scoring `9c4b45a`) and an explicit auth-flow commit during Week 11 (`2bd8c29`, 2026-07-14, "feat: add temporary in-memory API key auth flow to the frontend"). That commit correctly added `Authorization: Bearer` headers to every plain `fetch()` call (`frontend/src/apiKey.js`'s `authenticatedFetch`) but did not — and structurally could not, without the fix below — cover the one `EventSource` call in `App.jsx:442`, since browsers' native `EventSource` API has no mechanism to set custom request headers at all.
- **Bug A — SSE stream unconditionally 401s for any real browser client.** `TenantAuthenticationMiddleware.IsExemptPath` (`src/OrchestAI.API/Middleware/TenantAuthenticationMiddleware.cs:112-115`) exempts only `/health`, `/swagger`, `/api/v1/admin`. `RateLimiterSetup.IsExemptPath` already has a `.EndsWith("/stream")` exemption for the *rate limiter* (line 66) but the *auth* middleware has no equivalent. Live-verified: `curl` to `GET /api/v1/tasks/{id}/stream` with no `Authorization` header → immediate `401`; the identical request with a valid Bearer token → live SSE events (`task_started`, `agent_started`, `agent_failed`) streamed correctly in real time. `App.jsx`'s `new EventSource(...)` call sends no header by construction, so this is not a config gap — it is a structural mismatch between a Week-10 auth requirement and a JS platform limitation the auth work never accounted for.
- **Bug B — a task whose Orchestrator planning call fails is stuck in `Running` forever, with no `task_failed` ever published.** Live-verified: ran a real task through admission → dispatch → orchestrator LLM call (rejected by Anthropic due to the placeholder key) → confirmed via repeated `GET /tasks/{id}` polling that `status` stayed `Running` (not `Failed`) 15+ seconds after the sole `AgentExecution` had already recorded `Failed`. Root cause, confirmed by reading `OrchestratorAgent.PlanAsync` (`src/OrchestAI.Infrastructure/Agents/OrchestratorAgent.cs:100-160`) and `StartOrchestrationHandler.Handle` (`src/OrchestAI.Application/Commands/StartOrchestration/StartOrchestrationHandler.cs`) in full: `PlanAsync`'s own `catch` block records the agent failure and **re-throws** (line 159, deliberate — it needs the caller to know planning didn't produce a usable plan). `Handle` has only a `try/finally` around the whole orchestration body (finally releases the admission reservation) — there is no `catch` around the `PlanAsync` call specifically, so the exception propagates out of the `mediator.Send(...)` call entirely, past the `task.MarkFailed(...)`/`task_failed`-publish branch that exists later in the same method (which correctly handles *sub-agent* failures, at line ~196-209) and is only ever reached if `PlanAsync` returns successfully. It is caught only by `TasksController.StartAsync`'s background-dispatch generic `catch (Exception ex) { _logger.LogError(...) }`, which logs and does nothing else. This reproduces for *any* Orchestrator-planning-level failure (LLM outage, rate limit, malformed JSON surviving the one built-in retry) — not just a deliberately bad key.
  - Investigated and ruled out as a related risk: `OrchestratorAgent.ReviewAsync`'s failure path does **not** rethrow (its own `catch` returns a failed `AgentExecutionResult` via `FinalizeFailureAsync`, no `throw`), and `Handle` already has a documented, deliberate fallback for it (`synthesizedOutput = reviewResult.Success ? reviewResult.Output : aggregatedOutput`, task still marked `Completed` using the raw aggregated sub-agent output). This is intentional graceful degradation, not a second instance of Bug B — confirmed by full reading, not assumed.
- **Week 11 Task 13 (ADR-015) — read the required stack in full** (`ILlmProvider`, `IMcpTool`, `IToolRegistry`, `ToolRegistry.cs`, `AgentBase.ExecuteAsync`/`InvokeToolAsync`, `OrchestratorAgent`). Zero test files anywhere in `tests/OrchestAI.Tests` reference `AgentCapExceededException` (confirmed via grep) — the gap is unchanged since Week 11, not silently closed. From the full reading: `InvokeToolAsync` throws `AgentCapExceededException` on cap breach (`AgentBase.cs:600`) *inside* the same `try` block that `ExecuteAsync`'s own `catch (Exception ex)` (line 203) wraps — unlike `OrchestratorAgent.PlanAsync`, `AgentBase.ExecuteAsync`'s catch does **not** rethrow; it returns a failed `AgentExecutionResult` via `FinalizeFailureAsync`. That flows into `StartOrchestrationHandler`'s `results`/`failedResults` aggregation exactly like any other sub-agent failure, and correctly reaches the `task.MarkFailed`/`task_failed`-publish branch. This is a reasoned expectation from code reading, not a proof — the actual integration test (Task 3 below) still needs to exist to prove it, especially since a *concurrent* multi-agent scenario (`Task.WhenAll` in parallel mode) against the single shared `AsyncLocal` budget scope is the one part not obviously safe from reading alone.
- **README audit:** the test-count badge (`413 passing`) is correct — confirmed via `dotnet test tests/OrchestAI.Tests --list-tests | grep -c ...` → 413 — but the "Running Tests" section 90 lines later in the *same file* still shows the stale `"59 xUnit tests"` / `Passed: 59` output, a direct self-contradiction. The documented `GET /health` endpoint matches neither real health surface (`GET /api/v1/health` in `TasksController`, or the `/health/live`+`/health/ready` split `railway.json`'s `healthcheckPath` actually points at). Zero mention anywhere in the README of tenant auth, admin bootstrap, rate limiting, abuse/cost protection, observability, evals, post-hoc scoring, or CI/CD — the whole document reads as a frozen Week 5-6 snapshot plus one badge edit. The Quick Start / API Reference's example `POST /tasks` curl has no `Authorization` header — following it verbatim today produces a `401` with no explanation of why.
- **Frontend-investment decision (item 4), confirmed by the project owner (2026-07-19):** the frontend stays an internal/demo-only surface. No further product investment (proper session auth, self-serve tenant/API-key signup, UX redesign) is scheduled for it. This does **not** change the scope of Bugs A/B above — those are baseline correctness fixes needed for the demo to actually demo anything, independent of its product status.

---

## Blocking-confirmation status

No item in this investigation required halting for a scope-changing decision — both bugs found are small, single-handler/single-endpoint fixes, not architectural problems, and the one open judgment call (item 4) has already been resolved by the project owner per the summary above. Proceeding directly to the task list.

---

## Task 1: Fix SSE stream — browser-`EventSource`-compatible auth via a short-lived stream ticket

**Problem:** `EventSource` cannot send an `Authorization` header. The stream endpoint currently requires one (via `TenantAuthenticationMiddleware`), so it 401s for every real browser client. The fix must not weaken auth for the endpoint's data (which is scoped to one specific task, owned by one specific tenant) — a ticket must be bound to exactly one `(tenantId, taskId)` pair, single-use, and short-lived, so it cannot be replayed against a different task or reused after the stream opens.

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/ITaskStreamTicketIssuer.cs`
- Create: `src/OrchestAI.Infrastructure/Tenancy/InMemoryTaskStreamTicketIssuer.cs`
- Modify: `src/OrchestAI.API/Middleware/TenantAuthenticationMiddleware.cs` (add `/stream`-suffix exemption, mirroring `RateLimiterSetup.IsExemptPath`'s existing pattern)
- Modify: `src/OrchestAI.API/Controllers/TasksController.cs` (new `POST {id}/stream-ticket` action; `StreamAsync` validates a `?ticket=` query param instead of relying on the auth middleware)
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (register the new service, singleton — it owns its own in-memory ticket store)
- Modify: `frontend/src/App.jsx` (mint a ticket via `authenticatedFetch` immediately before opening `EventSource`, append it as a query parameter)
- Test: `tests/OrchestAI.Tests/Infrastructure/InMemoryTaskStreamTicketIssuerTests.cs`
- Test: `tests/OrchestAI.Tests/API/TaskStreamTicketAuthorizationTests.cs`

**Interfaces:**
```csharp
namespace OrchestAI.Domain.Interfaces;

public interface ITaskStreamTicketIssuer
{
    // Mints a single-use ticket bound to exactly this (tenantId, taskId) pair.
    string Issue(Guid tenantId, Guid taskId);

    // Consumes (and invalidates) the ticket if — and only if — it exists, has not
    // expired, and was minted for this exact taskId. Returns the bound tenantId on
    // success so the caller can set ambient tenant context, same as the normal
    // Bearer-auth path does.
    bool TryConsume(string ticket, Guid taskId, out Guid tenantId);
}
```

- [ ] **Step 1: Write the failing tests**
  - `InMemoryTaskStreamTicketIssuerTests`: `Issue` then immediate `TryConsume` with the correct `taskId` succeeds and returns the right `tenantId`; a second `TryConsume` with the same ticket fails (single-use); `TryConsume` with the *wrong* `taskId` (ticket minted for task X, presented for task Y) fails even before expiry; a ticket presented after its TTL has elapsed fails (use a short TTL, e.g. 1 second, and `Thread.Sleep`/`Task.Delay` past it in the test, or inject a fake clock if `IMemoryCache`'s absolute-expiration testing needs one — check `TenantLimitsCacheWarmUpIntegrationTests` for this codebase's existing pattern for time-based cache tests before adding a new one).
  - `TaskStreamTicketAuthorizationTests` (API-layer, following the existing pattern in `tests/OrchestAI.Tests/API/TenantAuthenticationMiddlewareTests.cs`): a `GET {id}/stream` request with **no** `ticket` query param and **no** `Authorization` header → `401`; with a valid ticket for a *different* task ID → `401`/`404`; with a valid, correctly-scoped ticket → the request is allowed through to the SSE handler (assert on reaching the controller action, not necessarily a full live stream in this test tier).

- [ ] **Step 2: Implement `InMemoryTaskStreamTicketIssuer`**
  - Backed by `IMemoryCache`, key = ticket string (cryptographically random, e.g. `Convert.ToHexString(RandomNumberGenerator.GetBytes(32))`), value = `(TenantId, TaskId)`, `AbsoluteExpirationRelativeToNow` of 60 seconds (long enough to cover the round-trip from minting the ticket to the browser opening the `EventSource` connection; short enough that a leaked ticket in a browser history/log is useless within a minute). `TryConsume` must remove the entry on any successful lookup (`IMemoryCache.TryGetValue` + explicit `Remove`) so it cannot be reused even within the TTL window.

- [ ] **Step 3: Add the ticket-minting endpoint to `TasksController`**
  - `POST {id}/stream-ticket`, behind the normal `TenantAuthenticationMiddleware` (no exemption needed here — this call carries a normal Bearer header from `authenticatedFetch` like every other frontend call). Load the task via the existing tenant-scoped repository/query path (reuse `GetOrchestrationTaskQuery` or an existence check that already respects the tenant global-query-filter from ADR-014 — do not write a new unscoped lookup), 404 if not found or not owned by the current tenant, otherwise `_ticketIssuer.Issue(tenantId, id)` and return `{ ticket, expiresInSeconds: 60 }`.

- [ ] **Step 4: Wire ticket validation into `StreamAsync` and exempt `/stream` from the Bearer requirement**
  - Add `.EndsWith("/stream", StringComparison.OrdinalIgnoreCase)` to `TenantAuthenticationMiddleware.IsExemptPath`, matching `RateLimiterSetup.IsExemptPath`'s existing exemption verbatim (keep the two exemption lists' `/stream` clause textually identical — add a comment on each cross-referencing the other, since a future change to one without the other reintroduces exactly this bug in reverse).
  - In `TasksController.StreamAsync`, accept `[FromQuery] string? ticket`. If `_ticketIssuer.TryConsume(ticket, id, out var tenantId)` fails, return `401` immediately before touching the event bus. On success, wrap the existing `await foreach (var evt in _eventBus.SubscribeAsync(...))` loop in `using (_tenantAccessor.SetTenant(tenantId))` — mirroring what `TenantAuthenticationMiddleware` does for every other request — since downstream code may read ambient tenant context during the subscription's lifetime.

- [ ] **Step 5: Update the frontend**
  - In `App.jsx`'s `handleSubmit`, after `POST /tasks` succeeds and before opening `EventSource`, call `authenticatedFetch(`${API_BASE}/tasks/${id}/stream-ticket`, { method: 'POST' })`, read `{ ticket }` from the response, and open `new EventSource(`${API_BASE}/tasks/${id}/stream?ticket=${encodeURIComponent(ticket)}`)`. Update `apiKey.js`'s file-level comment if it references the old assumption that all requests carry a Bearer header (it currently documents that pattern as universal; note the one deliberate exception and why).

- [ ] **Step 6: Run and verify live, not just via unit tests**
  - Repeat this investigation's manual verification method (isolated Postgres + API instance, minted tenant/key) end-to-end: create a task, call `/start`, mint a stream ticket, open the stream with `curl` using **only** the ticket query param (no `Authorization` header at all, exactly matching what a real `EventSource` sends) and confirm live events arrive. Then confirm the *same* ticket reused a second time is rejected, and a ticket minted for a different task ID is rejected against this task's stream endpoint.
  - Run `dotnet test tests/OrchestAI.Tests` — full suite must stay green, growing from 413.

---

## Task 2: Fix stuck-`Running` task when Orchestrator planning fails

**Files:**
- Modify: `src/OrchestAI.Application/Commands/StartOrchestration/StartOrchestrationHandler.cs`
- Test: `tests/OrchestAI.Tests/Application/StartOrchestrationPlanningFailureTests.cs`

- [ ] **Step 1: Write the failing test first**
  - Following the existing pattern in `StartOrchestrationHandlerTests.cs`/`StartOrchestrationAgentCapTests.cs` (mocked `IOrchestratorAgent`, real or mocked `IOrchestrationTaskRepository`/`IOrchestrationEventBus` per that file's existing conventions): configure the mocked `IOrchestratorAgent.PlanAsync(...)` to throw (any `Exception`, matching what a real LLM-provider failure surviving `OrchestratorAgent.PlanAsync`'s own retry/rethrow produces). Assert that after `Handle` runs (letting the exception be caught, not propagate — see Step 2): the `OrchestrationTask`'s status was updated to `Failed` (via `_taskRepository.UpdateAsync` being called with a task in `Failed` state), a `task_failed` event was published via the event bus mock, and — critically — the admission reservation release (`_reservationRepository.ReleaseAsync`) still happened exactly once (the existing `finally` block's guarantee must not regress).

- [ ] **Step 2: Wrap the `PlanAsync` call and its immediate consumers in a catch that mirrors the existing failure branch**
  - Add a `catch (Exception ex)` around the orchestration body's planning phase (the `var plan = await _orchestratorAgent.PlanAsync(...)` call and, practically, everything through the point a `plan` is required to continue — the agent-cap check at line 99 also depends on `plan`, so the catch boundary should cover from just after `task_started` is published through the cap check) that performs exactly the same three actions as the existing sub-agent-failure branch at the end of `Handle` (`task.MarkFailed(...)`, `_taskRepository.UpdateAsync(...)`, publish `task_failed`), then returns early — do not duplicate that logic as a second implementation; consider extracting a small private `FailTaskAsync(task, errorMessage, cancellationToken)` helper used by both this new catch and the existing failure branch (and by `FailWithAgentCapRejectionAsync`, which currently duplicates the same three steps a third time) so there is exactly one place that marks a task failed and publishes `task_failed`.
  - Preserve the existing `finally` block (reservation release) exactly as-is — the new `catch` must not swallow the exception in a way that skips it; structure as `try { ... } catch (Exception ex) { await FailTaskAsync(...); return ...; } finally { ...release... }`.

- [ ] **Step 3: Run and verify live**
  - Repeat this investigation's manual reproduction: real isolated instance, placeholder Anthropic key, create + start a task, poll `GET /tasks/{id}` — confirm `status` reaches `Failed` (not stuck at `Running`) within a few seconds, and that the SSE stream (using Task 1's ticket mechanism) delivers a `task_failed` event.
  - Run `dotnet test tests/OrchestAI.Tests` — full suite must stay green.

---

## Task 3: Close the Week 11 deferred `AgentBase`-level `MaxToolCallsPerTask` integration test (ADR-015 Task 13)

**Files:**
- Test: `tests/OrchestAI.Tests/Application/AgentToolCallCapIntegrationTests.cs`

**Investigation already done — do not re-read the stack again:** see this plan's Investigation summary above; `ILlmProvider`, `IMcpTool`, `IToolRegistry`, `ToolRegistry.cs`, and `AgentBase.ExecuteAsync`/`InvokeToolAsync` have already been read in full for this plan.

- [ ] **Step 1: Write the test exactly as ADR-015 Task 13 specified**
  - Use `ResearchAgent` (simplest concrete agent per the original task's own suggestion — confirm its `AvailableToolNames` list before writing the fake tool responses). Fake `ILlmProviderFactory`/`ILlmProvider.SendAsync` returning an `AgentTurn` with `StopReason == "tool_use"` and enough `ToolRequests` across successive turns (bounded by `MaxAgenticIterations = 10`, confirmed in `AgentBase.cs`) to exceed a small configured cap.
  - Open a real `ITaskToolCallBudget.BeginScope(maxToolCalls: 2)` (using the real `AsyncLocalTaskToolCallBudget`, not a mock — consistent with how `AgentBaseProviderTests.cs` already constructs its test subject) around driving the agent **through `StartOrchestrationHandler`**, not `ResearchAgent.ExecuteAsync` directly, per the original task's explicit instruction (this is what proves the cap survives the full dispatch path, not just the agent in isolation).
  - Assert: the `OrchestrationTask` ends in `Failed` (never `Completed`), a `RejectionEvent` with `Reason == AgentCapExceeded` was persisted, and — since `AgentBase.ExecuteAsync`'s catch does not rethrow — the returned `AgentExecutionResult.Success` is `false` with an error message reflecting the tool-call cap (confirmed exact wording in `AgentBase.cs:601`: `"Task tool-call cap exceeded: {CurrentCount} calls attempted, limit is {MaxToolCalls}."`).

- [ ] **Step 2: Also test the parallel-execution concurrency case**
  - The Investigation summary above specifically flagged this as the one part not obviously safe from reading alone: configure a plan with `ExecutionMode.Parallel` and multiple agents sharing one `ITaskToolCallBudget` scope (mirroring `Task.WhenAll` in `StartOrchestrationHandler.Handle`), each attempting more tool calls than half the cap. Assert the shared `AsyncLocal` counter is enforced correctly across concurrent sub-agents (no double-counting bypass, no race allowing more than the cap through) — this extends, rather than duplicates, the existing `AsyncLocalTaskToolCallBudgetTests.TryIncrement_ConcurrentIncrementsWithinOneScope_NeverExceedsCapAcrossParallelCallers` unit test by proving the same guarantee holds when driven through real agent execution, not just direct `TryIncrement()` calls.

- [ ] **Step 3: Run and update ADR-015 / the memory record**
  - Run `dotnet test tests/OrchestAI.Tests` — full suite must stay green, growing from 413.
  - Add a short "Implementation notes" addendum to ADR-015 (`DECISIONS.md`) marking Task 13 closed, with the commit reference, following the same style as this project's other closed-task addenda.

---

## Task 4: Rewrite README to match the actual current feature set

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Fix the self-contradiction and re-verify the real number**
  - Re-run `dotnet test tests/OrchestAI.Tests --list-tests | grep -c "^\s\+OrchestAI.Tests\."` at execution time (the count will have grown from 413 once Tasks 1-3's new tests land) and use that exact number consistently in both the badge and the "Running Tests" section — do not leave two different counts in the same file again.

- [ ] **Step 2: Add the missing feature documentation**
  - New sections (or substantial rewrites of existing ones) covering: tenant/API-key model and the admin bootstrap flow (`POST /api/v1/admin/tenants`, `POST /api/v1/admin/api-keys`, `X-Admin-Secret`) with a concrete example; rate limiting and abuse/cost protection (per-tenant `RequestsPerMinute`/`MaxConcurrentTasks`/`MaxAgentsPerTask`/`MaxToolCallsPerTask`/cost budgets, 429 behavior); observability (timeline/summary/cost-dashboard/error-rates/compare endpoints); evals and post-hoc scoring; the CI/CD pipeline (4 GitHub Actions jobs) and the `/health/live`+`/health/ready` split (correcting the currently-wrong documented `GET /health` path).
  - **Resolved (checked, not assumed):** `TasksController.Health()` (`GET /api/v1/health`, `TasksController.cs:340-345`) has zero callers anywhere in the repo — confirmed via grep across `frontend/src`, `tests/OrchestAI.Tests` (the two `/health`-string hits found are unrelated middleware-prefix-exemption tests, not calls to this action), and every top-level `.md` doc. It is Week-1-era dead code, fully superseded by Week 12's `/health/live`+`/health/ready` split, which is what `railway.json`'s `healthcheckPath` actually points at. **Delete the action from `TasksController` (do not document it)** as part of this task, and document only the real `/health/live`/`/health/ready` endpoints.

- [ ] **Step 3: Fix Quick Start and the API Reference example so they actually work as written**
  - Quick Start must include, in order: start Postgres, set `Anthropic__ApiKey` and `Admin__BootstrapSecret`, run the API, bootstrap a tenant + API key via the admin endpoints (with a concrete `curl` example), *then* set `VITE_API_URL` and run the frontend, which will prompt for the API key on first load.
  - The `POST /tasks` example under "API Reference" must include the `Authorization: Bearer <key>` header.

- [ ] **Step 4: Label the frontend per the confirmed decision (item 4)**
  - Add an explicit, visible note (in the "What It Does" or "Architecture" section) stating the React UI is an internal development/demo playground, not a production frontend — no self-serve tenant signup, no production-grade session auth (the in-memory pasted-API-key flow is deliberate and documented as such in `apiKey.js`), and it is not planned to become a public-facing product surface at this time. Cross-reference `DECISIONS.md`'s ADR-014 confirmation #11 (Task 5).

---

## Task 5: Record the frontend-investment decision in writing

**Files:**
- Modify: `DECISIONS.md` (append a new confirmation to ADR-014, not a new ADR number — this is a product/scope confirmation building directly on ADR-014's existing confirmation #10 about the frontend's temporary auth design, not a new architectural decision)

- [ ] **Step 1: Add "ADR-014 Confirmation #11: Frontend remains internal/demo-only (Phase 1, 2026-07-19)"**
  - State the decision plainly: the frontend stays an internal development/demo surface; no further product investment (production session auth, self-serve tenant onboarding, UX redesign) is scheduled; Bugs A/B from this plan's investigation were fixed regardless, since they block the demo from functioning at all, independent of its product status; revisit only if a future milestone explicitly calls for a public-facing demo.
  - Reference this plan document and the investigation findings (Bug A/B) as the basis for the decision, per this project's existing convention of citing the specific evidence behind each confirmation rather than asserting it bare.

---

## Task 6: Final live re-verification of the full user journey

**Files:** none (verification only)

- [ ] **Step 1: Re-run this plan's own investigation method end-to-end after Tasks 1-2 land**
  - Isolated Postgres + API instance (same method as this plan's Investigation summary), bootstrap a tenant + API key, create a task, start it, mint a stream ticket, open the stream with no `Authorization` header (exactly matching a real browser `EventSource`) and confirm events arrive live; confirm a task whose Orchestrator planning fails now reaches `Failed` with a `task_failed` event, not stuck `Running`.
  - **Explicit limitation to carry forward, not silently drop:** no browser-automation tool is available in this environment, so this step exercises the exact HTTP/SSE contract the frontend issues rather than literally driving a browser. If a browser tool becomes available before this task executes, prefer it for a literal frontend walkthrough (npm run dev, open the UI, submit a task, watch it render); otherwise this HTTP-contract-level verification is the documented, deliberate substitute, not something to silently upgrade to "verified in the browser" without actually doing it.
  - **Also explicit and not resolved by this plan:** a real Anthropic (and optionally Firecrawl/Perplexity) API key is needed to verify the *success* path (an actual completed task with real agent output, real observability data, a real eval run against real traces) — this plan's investigation only had credentials to verify the *failure* paths (Bugs A/B are both failure-path bugs, found without needing a real LLM response). If real credentials are available when this task executes, use them for one full success-path run; if not, document that the success path remains verified only by earlier weeks' own test suites (413+ passing tests), not by this phase's live check, and say so plainly rather than implying full coverage.

- [ ] **Step 2: Run the full test suite one final time**
  - `dotnet test tests/OrchestAI.Tests` — confirm 0 failed, 0 skipped, count grown from 413 by exactly the tests added in Tasks 1-3.
