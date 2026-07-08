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
