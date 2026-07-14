# OrchestAI — Observability Architecture

One-page reference for how execution visibility works end-to-end. For the reasoning behind
these choices, see ADR-011 in `DECISIONS.md`.

## The four stages

```
1. CAPTURE           2. AGGREGATE              3. QUERY                4. UI
─────────────        ──────────────            ─────────────           ────────────
AgentBase writes      Background job            Query handlers          React views
AgentExecution/       rolls raw CostLedger       read raw tables for     read from the
McpToolCall/          rows into daily            "right now" (timeline,  API, nothing
CostLedger rows        CostRollups every          summary card) and       talks to Postgres
as agents run,        5 min. Raw tables          CostRollups for         directly.
tagged with           are never deleted or        "over time" (cost
TraceId/SpanId.       summarized-in-place.       dashboard beyond
                                                   today).
```

## 1. Capture

Nothing new is logged specifically "for observability" — the same writes `AgentBase` already
makes to run the system (persisting `AgentExecution`, `AgentMessage`, `McpToolCall`,
`CostLedger` rows) are the observability data. Three things were added on top of the existing
writes:

- **`TraceId`** — generated once per `OrchestrationTask` at task-start
  (`ActivityTraceId.CreateRandom()`), stored on the task row.
- **`SpanId` / `ParentSpanId`** — generated per `AgentExecution` and per `McpToolCall`
  (`ActivitySpanId.CreateRandom()`). An agent's parent is the Orchestrator's planning span; a
  tool call's parent is the agent span that invoked it. This is what makes the timeline a
  tree, not a flat list sorted by timestamp.
- **`ErrorCategory`** — classified at the exact point a failure is caught (same
  transient-vs-not logic the retry policy already uses), instead of trying to parse a category
  back out of a free-text error message later.

`AgentRetryAttempt` rows are written alongside the existing `agent_retry` SSE event — the SSE
event is for live clients watching a run in progress; the DB row is what lets the error
monitoring view answer "how many retries did this agent need" after the fact.

**Cost accuracy guarantee:** `CostLedger.CostUsd`, `AgentExecution.CostUsd`, and
`OrchestrationTask.TotalCostUsd` are computed once at write time
(`AgentBase.CalculateCostAsync` → `FinalizeSuccessAsync`) using whichever `ModelPricing` the
cache holds at that moment, then persisted as plain decimals — never re-derived from current
pricing on read. A price change (or a correction) after the fact cannot retroactively alter
what a past run is reported to have cost. Proven by
`AgentBaseProviderTests.ExecuteAsync_ModelPricingChangesAfterExecution_HistoricalCostRemainsUnchanged`.

## 2. Aggregate

`CostRollupBackgroundService` (a `BackgroundService`) wakes up every 5 minutes, and for a
trailing window since its last successful run (re-rolling the last 2 days each pass to catch
late-arriving writes, capped at 30 days of catch-up), upserts one `CostRollups` row per
(date, user, agent type, model) by summing the matching `CostLedger` rows. This is the only
place aggregation happens — there is no on-the-fly `GROUP BY` over the full `CostLedger` table
at query time for anything beyond today.

Rollups are eventually consistent (up to 5 minutes stale) by design. Raw tables are never
consistent-with-a-lag — they're written synchronously in the same transaction as the agent
execution they describe, so a run's own timeline is queryable the moment it finishes.

## 2a. Eval cost segregation

Week 8 added a `CostSource` discriminator (`Production`/`Eval`) to `CostLedger`, plus a nullable
`EvalRunId` on both `CostLedger` and `AgentExecution`. Every eval-case invocation the
`EvalRunBackgroundWorker` runs — and every `LlmJudgeScorer` judge call it triggers — writes its
`CostLedger` row tagged `Source=Eval`. Production traffic (`StartOrchestrationHandler`,
`ResumeOrchestrationTaskHandler`) never sets `EvalRunId`, so those rows default to
`Source=Production`.

**Where it's filtered:** `ICostLedgerRepository.GetDailyAggregatesAsync` — the single method
behind both `CostRollupBackgroundService` (the 5-minute rollup job) and `GetCostDashboardHandler`'s
live "today" branch — adds `Where(c => c.Source == CostSource.Production)`. Nothing downstream of
that method ever sees eval cost; there's no second filter to remember elsewhere. See
`CostLedgerRepositoryEvalFilterTests` for the test proving a mixed batch of production and eval
rows aggregates to production-only totals.

Full reasoning: ADR-012 in `DECISIONS.md`.

## 2b. Post-hoc scoring

Week 9 added `EvalRun.Source` (`LiveSuite`/`PostHoc`). `EvalResult` rows now originate from two
sources, told apart by `eval_run.source` (via the `EvalRunId` FK) together with
`EvalResult.EvalCaseId`:

- **Live suite execution** (Week 8): `EvalRun.Source = LiveSuite`, `EvalResult.EvalCaseId` is a
  real `EvalCase` FK, `EvalResult.Rubric` is null.
- **Post-hoc scoring** (Week 9): `EvalRun.Source = PostHoc`, `EvalResult.EvalCaseId` is null,
  `EvalResult.Rubric` holds the free-text judge rubric applied to that run's traces.

Post-hoc scoring never re-invokes an agent — `EvalRunBackgroundWorker.ProcessPostHocRunAsync`
reads `AgentExecution.OutputResult` directly from rows that already exist (production traffic or
past eval runs) and scores them with `LlmJudgeScorer` against a rubric instead of a predefined
`EvalCase`. Judge cost still flows through the same `Source=Eval`/`EvalRunId`-tagged `CostLedger`
path as Week 8 — no second cost-tagging code path was introduced.

Re-running a post-hoc request against an already-scored trace is a no-op by default (tracked in
`EvalRun.SkippedAlreadyScoredCount`). `EvalRun.ForceRescore` is the deliberate override — it
supersedes (delete-then-insert) rather than appends, so a trace never accumulates more than one
current post-hoc result.

Full reasoning: ADR-013 in `DECISIONS.md`.

## 2c. Tenant isolation

Every table in sections 1-2b above is now tenant-scoped (see ADR-014) — `AgentExecution`,
`McpToolCall`, `CostLedger`, `EvalRun`, `EvalResult`, and everything else holding task/execution/
cost/eval data carries a `TenantId`, enforced by an EF Core global query filter (reads) and a
`SaveChanges` interceptor (writes), and backed by a real foreign key to `Tenants` (`ON DELETE
RESTRICT`) rather than just an index. Every query in this document's sections 1-4 is automatically
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
is its entire purpose. `CostRollups` rows still carry `TenantId` in their grouping key (also part
of their unique index, after a Task 12 fix closed a cross-tenant collision risk), so the
resulting aggregates remain per-tenant even though the job that produces them reads across all of
them at once.

**Known limitation**: the write-side interceptor's tamper check cannot detect a changed
`TenantId` on an entity updated through this codebase's disconnected-`Update()` repository
pattern (fetch in one `DbContext`, `Update()` in a fresh one) — only for an entity still tracked
in the same `DbContext` it was fetched in. This is accepted as safe today because `TenantId` has
no public setter anywhere and no code path mutates it post-creation; see ADR-014 and the code
comments on `TenantScopingInterceptor.cs`/`ITenantScoped.cs` for the full reasoning.

Full reasoning: ADR-014 in `DECISIONS.md`.

## 3. Query

Five MediatR query handlers, each reading whichever layer answers the question fastest:

| Query | Reads |
|---|---|
| Execution timeline | Raw `AgentExecution` + `McpToolCall` for one task, assembled into a span tree via `SpanId`/`ParentSpanId` |
| Execution summary card | `AgentExecution`/`McpToolCall` for the task, plus `CostLedger` (provider/model actually used) and `AgentRetryAttempts` for that task's executions |
| Cost dashboard | `CostRollups` for any date range excluding today, blended with a live `CostLedger` aggregate for today |
| Error rate monitoring | Raw `AgentExecution`/`McpToolCall` failure rows + `AgentRetryAttempts`, grouped by `ErrorCategory` |
| Run comparison | Two raw-table lookups side by side — no aggregation involved |

If a rollup for a requested day genuinely hasn't run yet (fresh deploy, job briefly down), that
day is simply absent from the cost dashboard's breakdown rather than showing a wrong number —
the handler never crashes on a missing rollup, it just doesn't fabricate one.

## 4. UI

The React frontend never queries Postgres directly — every view is a thin client over the API
query handlers above. The trace viewer is the only view with real UI complexity: spans nest
by `ParentSpanId`, are collapsible, and are color-coded by `AgentType`, matching the shape
LangSmith/Datadog trace viewers use because that shape is what makes a Gantt-style multi-agent
run legible at a glance.

## Why this holds up as the system grows

The design goal was "don't build a bespoke event-sourcing system when the entities you already
write are the event log." Nothing here changes what `AgentBase` was already going to write —
it adds correlation IDs to writes that were happening anyway, and reads them back through a
rollup layer sized for dashboard queries instead of forcing every dashboard load to scan raw
history. The trace/span ID scheme is the one piece that would be expensive to redo later
(everything downstream — a future OTLP exporter to Grafana or Azure Monitor — assumes IDs in
this shape), which is why it was decided deliberately in Week 7 instead of drifting into
existence by accident.
