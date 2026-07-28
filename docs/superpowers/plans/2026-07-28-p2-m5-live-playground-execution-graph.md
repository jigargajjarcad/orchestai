# P2-M5 — Live Playground Execution Graph

> Status: **PLAN — not approved for implementation.** Base: `origin/main = c46da96`.
> Planning-only artifact. No production code changes are part of this document.

## 1. Executive summary

Turn the Playground's flat agent-card feed into a **live, SSE-driven execution graph** that
visually communicates orchestration structure (hierarchy, parallelism, tool calls, manager
review) in real time, while keeping the existing card/List experience as a toggleable fallback
and adding a "View full trace in Observability" handoff.

**Central architectural finding that makes this cheap and low-risk:** the Observability graph is
built by a **pure function** — `computeGraphLayout(spans)` (`frontend/src/observability/layout.js`)
— that consumes a **flat span array** (`{spanId, parentSpanId, spanType, label, status, startedAt,
completedAt, durationMs, costUsd}`) and returns React-Flow `{nodes, edges}`. `AgentNode`,
`ToolCallNode`, `TraceEdge` and `computeGraphLayout` are all **exported**. Therefore the entire
milestone reduces to: **build an equivalent, live-updating span array from the SSE stream the
Playground already consumes, and feed it into the existing, unmodified graph primitives.**

- **No backend changes.** The existing SSE events carry enough to derive the hierarchy.
- **No modification to `observability/*`.** Pure reuse (import) of the validated primitives.
- **Timeline freeze:** not a code blocker (reuse only imports). A one-paragraph governance note
  formally records that the freeze's validation purpose is satisfied (Phase 0).
- **Estimated complexity: Medium.** The novelty is the live-streaming reducer + edge-case handling,
  not the rendering (rendering is validated reuse).

## 2. Current architecture findings (read-only inspection)

- **Playground** lives in `frontend/src/App.jsx` (`App()` component). Execution state:
  - `const [agents, setAgents] = useState({})` (`App.jsx:465`) — map keyed by `agentExecutionId`.
  - `const [agentOrder, setAgentOrder] = useState([])` (`:466`) — insertion order of agent ids.
  - `const [managerReview, setManagerReview] = useState(null)` (`:472`).
  - `const [approvalRequest, setApprovalRequest] = useState(null)` (`:470`).
  - SSE consumed by an inline `switch (data.event)` in `es.onmessage` (`App.jsx:~556-623`), via
    `new EventSource(.../tasks/{id}/stream?ticket=...)`.
  - Reducer helpers: `updateAgent`, `addToolCall`, `completeToolCall`, `appendAgentMessage`
    (`App.jsx:~504-526`) — **`addToolCall`/`completeToolCall` already match repeated same-tool
    started→completed pairs per agent**, the exact behavior the graph needs.
  - Rendered as `AgentCard` list (`App.jsx:854`) + `ManagerReviewCard` (`:861`).
  - Routing: `useRouting()` → `{ view, navigate }` (`App.jsx:473`, `hooks/useRouting.js`),
    top-level only (`playground|memories|observability|evals`), no params.
- **Observability graph** (`ObservabilityPage.jsx`): `viewMode` state defaults `'graph'` (`:221`);
  `graphLayout = useMemo(() => computeGraphLayout(data.spans), [data])` (`:252`); `nodeTypes =
  { agentNode: AgentNode, toolCallNode: ToolCallNode }` (`:259`); `edgeTypes = { traceEdge:
  TraceEdge }` (`:260`); rendered in `<ReactFlowProvider><ReactFlow .../>` (`:414+`); Graph/List
  toggle (`:365-377`). The top-level `ObservabilityPage()` component (`:808+`) manages `taskId`
  via a local `TaskPicker`.
- **Design system:** `theme/tokens.js`, `theme/typography.js`, `observability/timelineTheme.js`
  (`agentColors`, `toolCallColor`) — the primitives already pull identity/status colors from here;
  reuse inherits the validated look for free.

## 3. Existing SSE contract (complete mapping — DO NOT MODIFY)

Each event is a serialized `SseEvent { Event, TaskId, Payload, Timestamp }`; the frontend reads
`data.event` + `data.payload` (and `data.timestamp` is available — server emission time). Sources:
`StartOrchestrationHandler.cs`, `OrchestratorAgent.cs`, `AgentBase.cs`.

| SSE event | Payload fields | Current consumer | Graph effect (new) | State on effect |
|---|---|---|---|---|
| `task_started` | `taskId, status` | `setTaskStatus('Running')` | create **Orchestrator** root node | Running |
| `orchestrator_plan` | `plan, executionMode, selectedAgents[], executionOrder[], agentPrompts{}` | (not consumed in Playground today) | confirm root; optionally pre-create planned agent nodes | Orchestrator → Completed (plan done) |
| `approval_required` | `taskId, plan, selectedAgents, agentPrompts, executionMode` | `setApprovalRequest` | (no graph node; existing ApprovalCard) | — |
| `task_approved` / `task_rejected` | `taskId` | clear approval | — | — |
| `agent_started` | `agentExecutionId, agentType, status` | `updateAgent` + `agentOrder` | create **agent node** id=`agentExecutionId`, parent=`"orchestrator"` | Running |
| `message_written` | `agentExecutionId, agentType, contentPreview` | `appendAgentMessage` | (List/card only; no graph change) | — |
| `tool_started` | `agentExecutionId, agentType, toolName, inputParameters` | `addToolCall` | create **tool node**, parent=`agentExecutionId` | Running |
| `tool_completed` | `agentExecutionId, agentType, toolName, success, durationMs, outputPreview` | `completeToolCall` | complete matching tool node | Completed / Failed(!success) |
| `agent_completed` | `agentExecutionId, agentType, inputTokens, outputTokens, costUsd` | `updateAgent` | agent node → Completed; set cost | Completed |
| `agent_failed` | `agentExecutionId, agentType, …` | `updateAgent` | agent node → Failed | Failed |
| `agent_retry` | `agentType, attempt, delayMs, reason` | (not consumed) | optional badge; else ignore | — |
| `checkpoint_saved` | `taskId, agentType, agentExecutionId` | (not consumed) | ignore | — |
| `manager_review_started` | `taskId` | `setManagerReview({status:'running'})` | create **manager-review** node, parent=`"orchestrator"` | Running |
| `manager_review_completed` | `taskId, result` | `setManagerReview({status:'completed', result})` | review node → Completed; carry `result` | Completed |
| `task_completed` | `taskId, totalCostUsd, agentCount` | `setTaskStatus('Completed')` + `fetchFinalResult` | reveal handoff; orchestrator → Completed | Completed |
| `task_failed` | `taskId, errorMessage` | `setTaskStatus('Failed')` | orchestrator → Failed | Failed |

## 4. Observability graph-primitive reuse analysis

- `computeGraphLayout(spans, options)` — **pure, exported** (`layout.js:183`). Input is a flat span
  array; output `{nodes, edges}`. Re-runnable on every state change (idempotent). **Reuse as-is.**
- Node `data` shape it emits (consumed by the node components): `{label, agentName, status,
  durationMs, costUsd, extentPct:{offset,width}}` (`layout.js:215-222`). Our reducer must produce
  spans whose `computeGraphLayout` output matches this — automatic, since we call the real function.
- `AgentNode` (`:143`), `ToolCallNode` (`:103`), `TraceEdge` (`:51`) — **exported**, render purely
  from `data`. **Reuse as-is** via `nodeTypes`/`edgeTypes`.
- **Decision E: DIRECT REUSE. Zero modification to `observability/*`.** No extraction, no Phase-1
  primitive refactor. The Playground imports `computeGraphLayout`, `AgentNode`, `ToolCallNode`,
  `TraceEdge`, `agentColors`, `toolCallColor` and builds its own graph container.
- **Live suitability:** the primitives were built for a completed static trace, but nothing in them
  assumes completeness — they render whatever `status`/`extentPct` they receive. Re-running
  `computeGraphLayout` on the growing span array and re-rendering React Flow is sufficient for live
  updates (≤~30 nodes → negligible cost).

## 5. Timeline freeze governance / lift plan (Phase 0 — documentation only)

- The freeze is **governance, not code** — there is no code flag. It is enforced by (a) the
  recurring "protected areas" task instructions and (b) the master plan's Timeline byte-stability
  acceptance criteria.
- Because P2-M5 is **pure reuse** (imports only; no `observability/*` edit), it is freeze-compliant
  **even without lifting the freeze**. The lift is therefore a **status recording**, recommended
  for clarity, not a prerequisite.
- **Exact minimal change (to be executed only within the approved implementation, not now):**
  - File: `docs/superpowers/plans/2026-07-26-full-platform-redesign-master-plan.md`.
  - Section: §11 (backend-trace verification) and the Phase-2/§7–§9 freeze acceptance criteria.
  - Wording intent: "The Timeline freeze's validation purpose is satisfied as of the genuine keyed
    verification (P2-M4, taskId `f2da574c-8f47-4fee-a954-b8962f243e83`, traceId `2c5143e5…`, all 5
    criteria passed). The freeze is formally lifted: `observability/*` and the Timeline block cease
    to be do-not-modify. The Timeline remains a **validated reference** — future changes go through
    normal review + the deterministic tests below, not a hard freeze."
  - No `DESIGN.md` change required. Preserve reference status by keeping the Timeline's existing
    tests green (Phase 7 adds a `computeGraphLayout` regression test as the guard that replaces the
    freeze).

## 6. Proposed architecture

New module **`frontend/src/playground/`** (no observability edit):
- `usePlaygroundExecutionGraph.js` — a reducer hook: consumes normalized SSE events, maintains an
  ordered **span array** (`liveSpans`), returns `{ liveSpans, reset }`.
- `PlaygroundExecutionGraph.jsx` — presentational: `computeGraphLayout(liveSpans)` →
  `<ReactFlowProvider><ReactFlow nodeTypes edgeTypes .../>`; mirrors ObservabilityPage's graph
  container (fitView, Background, Controls) but is its own component.
- `playgroundSpanModel.js` — pure helpers: synthetic-id constants, `eventToSpanMutation(event,
  state)` mapping table (§8), status mapping.

`App.jsx` change: the existing `es.onmessage` switch **additionally** dispatches each event into
the graph reducer (the card state is untouched — both derive from the same events). Add a
Graph/List `viewMode` state in the Playground result pane; render `PlaygroundExecutionGraph` (graph)
or the existing card list (List). Add the handoff button on completion.

## 7. Data flow: SSE → reducer → span array → React Flow

```
EventSource → es.onmessage
  → (existing) card reducer (setAgents/…) — UNCHANGED
  → (new) dispatchGraphEvent(event)         — usePlaygroundExecutionGraph
        → playgroundSpanModel.eventToSpanMutation(event, liveSpans)
        → liveSpans (append/update)          [stable, ordered]
  → PlaygroundExecutionGraph
        → computeGraphLayout(liveSpans)       [reused, pure]
        → <ReactFlow nodes edges nodeTypes={agentNode,toolCallNode} edgeTypes={traceEdge}/>
```

Timing: `startedAt` = the `*_started` event's timestamp (or client receipt `Date.now()`);
`completedAt` = the `*_completed` event's timestamp; `durationMs = completedAt - startedAt`. This
feeds `computeGraphLayout`'s existing offset/width math (X = time, Y = hierarchy).

## 8. Node identity & parent-child model

Synthetic span objects (shape = `TimelineSpanDto` subset):

| Node | `spanId` | `parentSpanId` | Created by | `label`/`spanType` |
|---|---|---|---|---|
| Orchestrator (root) | `"orchestrator"` (const) | `null` | `task_started` (or first event) | `"Orchestrator"` / `Agent` |
| Agent | `agentExecutionId` (backend id) | `"orchestrator"` | `agent_started` | `agentType` / `Agent` |
| Tool call | `` `${agentExecutionId}:${toolName}:${n}` `` (n = per-(agent,tool) occurrence counter) | `agentExecutionId` | `tool_started` | `toolName` / `ToolCall` |
| Manager review | `"manager-review"` (const) | `"orchestrator"` | `manager_review_started` | `"Orchestrator"`/`"Manager Review"` / `Agent` |

This exactly reproduces the genuine P2-M4 trace hierarchy (Orchestrator → Data/Research/review;
tools nested under their agent). **Tool identity** for repeated same-tool calls (Research × 13
perplexity) is synthesized client-side via an occurrence counter; `tool_completed` completes the
oldest still-Running tool of that `(agentExecutionId, toolName)` — the exact match logic the
existing `completeToolCall` already uses.

## 9. State transition model

Per node: `planned? → Running → (Completed | Failed)`.
- Orchestrator: Running on `task_started` → Completed on `orchestrator_plan`/first `agent_started`
  (its planning phase ends there); the review is a separate node. Root stays visible throughout.
- Agent: Running on `agent_started` → Completed on `agent_completed` / Failed on `agent_failed`.
- Tool: Running on `tool_started` → Completed/Failed on `tool_completed.success`.
- Review: Running on `manager_review_started` → Completed on `manager_review_completed`.

**Robust edge-case handling (simplest correct behavior):**
- **Duplicate events:** upsert by `spanId` (agent) or by matching oldest-Running (tool) — idempotent;
  a duplicate `*_completed` on an already-completed node is a no-op.
- **Out-of-order / `*_completed` before `*_started`:** create-on-first-reference — if a
  `tool_completed`/`agent_completed` references an unknown node, create it in the terminal state.
- **Missing/unknown parent:** if a child's parent id isn't present, **attach to the `"orchestrator"`
  root** (never drop the node; never throw). Guarantees `computeGraphLayout` always gets a valid
  tree (its `buildSpanTree` already treats unknown parents as roots — belt-and-suspenders).
- **Reconnect/replayed SSE:** the reducer is a pure fold over events keyed by id → replaying the
  same events converges to the same state (idempotent). No dedupe table needed beyond id upsert.
- **No over-engineering:** no event log, no reconciliation with `/timeline` during the run (the
  handoff fetches the authoritative trace post-completion).

## 10. Parallel-execution visualization

`computeGraphLayout` places X by **start time** and Y by **hierarchy traversal row** (unique per
node). Two siblings that start together (Data & Research — same `agent_started` instant) get the
**same X** on **different rows**, both edged from Orchestrator — reproducing exactly the validated
P2-M4 render where parallelism was VISUAL-PASS. Reference scenario the graph must reproduce live:
Orchestrator → (Data ‖ Research start together) → db_query under Data + N perplexity under Research
→ Manager Review. No new layout logic required.

## 11. Graph/List toggle

- New `playgroundViewMode` state (`'graph' | 'list'`), **default `'graph'`** (the differentiator);
  List = the current `AgentCard` feed (unchanged).
- **No persistence** (session-simple, matches ObservabilityPage). Toggle is view-only: both views
  read the **same** underlying state, so **switching never loses state** and works identically
  while running and after completion.
- Placed in the Playground result pane header next to the trace/status line.

## 12. Manager Review integration

- The graph shows a **Manager Review node** (§8). The **existing `ManagerReviewCard`** remains the
  place the *result text* renders, shown in the **List view** (unchanged) — **no duplicate result
  rendering** in the graph.
- Optional (low-priority, in-scope-if-cheap): clicking the review node surfaces the existing
  `review.result` via the same `ManagerReviewCard` in a side panel. If not trivial, defer — the
  List view already renders it.

## 13. Observability handoff

- On `task_completed`, reveal a **"View full trace in Observability"** button.
- Mechanism: `navigate('observability')` (existing router) **plus** pass the completed `taskId` so
  Observability pre-selects it. Minimal implementation: App lifts a `handoffTaskId` and passes it
  as a new optional prop `initialTaskId` to `ObservabilityPage` (top-level component `:808`, **not**
  the frozen Timeline block) which seeds its `taskId` useState. If `initialTaskId` is absent,
  behavior is unchanged.
- **If trace unavailable** (e.g., Observability tasks-list fetch fails): the existing T5a error
  state handles it; the completed task is also the most-recent in the list as a fallback.
- Button visible only after `task_completed` (not during run, not on failure).

## 14. Detailed implementation phases

### Phase 0 — Governance / freeze-lift (documentation only)
- **File:** `docs/superpowers/plans/2026-07-26-full-platform-redesign-master-plan.md` (§11 + freeze
  acceptance criteria). **Intent:** record freeze validated/lifted per §5.
- **Tests:** none. **Acceptance:** wording present; no code touched. **Risk:** none (doc).

### Phase 1 — Shared primitive reuse/extraction — **NOT REQUIRED**
- Investigation (§4) proved direct reuse. **No `observability/*` change.** If, during
  implementation, a live prop is genuinely needed on a primitive, STOP and re-scope (see §19).

### Phase 2 — Playground graph state/reducer
- **Files (new):** `frontend/src/playground/usePlaygroundExecutionGraph.js`,
  `frontend/src/playground/playgroundSpanModel.js`.
- **Symbols:** `usePlaygroundExecutionGraph()` → `{ liveSpans, dispatchGraphEvent, reset }`;
  `eventToSpanMutation(event, state)`, `ORCHESTRATOR_ID`, `MANAGER_REVIEW_ID`, `mapStatus()`.
- **Intent:** pure fold of SSE events → ordered span array per §8/§9. No rendering.
- **Tests:** unit tests on the reducer (§16 T1–T10). **Acceptance:** given a recorded event
  sequence (the P2-M4 shape), `liveSpans` matches the expected 18-span tree.
- **Risk:** low; pure function, fully unit-testable. Rollback: delete the module.

### Phase 3 — Graph rendering
- **File (new):** `frontend/src/playground/PlaygroundExecutionGraph.jsx`.
- **Symbols:** `PlaygroundExecutionGraph({ spans })` → `computeGraphLayout(spans)` →
  `<ReactFlowProvider><ReactFlow nodes edges nodeTypes={{agentNode:AgentNode,toolCallNode:ToolCallNode}}
  edgeTypes={{traceEdge:TraceEdge}} fitView><Background/><Controls/></ReactFlow>`.
- **Intent:** presentational reuse of the validated primitives; empty state when `spans` is empty.
- **Tests:** render test with a static span array asserts node/edge counts + node types.
- **Acceptance:** renders `.react-flow__node` per span; no observability import is modified.
- **Risk:** low (reuse). Rollback: delete the component.

### Phase 4 — Graph/List toggle + fallback preservation
- **File:** `frontend/src/App.jsx` (Playground result pane only).
- **Symbols:** add `playgroundViewMode` state + toggle buttons; render `PlaygroundExecutionGraph`
  (graph) vs. the existing `AgentCard` list (List); wire `dispatchGraphEvent(data)` into the
  existing `es.onmessage` switch (additive — card reducer untouched); call `reset()` in the
  existing run-reset path (`App.jsx:543-550`).
- **Tests:** toggle test; assert card path unchanged (existing App/Playground tests stay green).
- **Acceptance:** both views work; existing Playground behavior (submit/approve/reject/final)
  unchanged. **Risk:** medium (touches App.jsx SSE handler — additive only, no reducer extraction).
  Rollback: revert App.jsx.

### Phase 5 — Manager Review + final result integration
- **File:** `App.jsx`. **Intent:** ensure review node appears (Phase 2 handles it); List view keeps
  `ManagerReviewCard`; no duplicate result render (§12). **Tests:** review-node presence + no
  duplicate. **Acceptance:** review visible in graph, result in card. **Risk:** low.

### Phase 6 — Observability trace handoff
- **Files:** `App.jsx` (handoff button + `handoffTaskId`), `ObservabilityPage.jsx` (top-level
  component `:808` only — add optional `initialTaskId` prop seeding `taskId` useState).
- **Intent:** §13. **Tests:** handoff-visibility test; ObservabilityPage `initialTaskId` pre-select
  test (mock fetch). **Acceptance:** button navigates + pre-selects; absent prop = unchanged.
- **Risk:** low-medium (touches ObservabilityPage top-level, NOT the Timeline block). Rollback:
  revert both.

### Phase 7 — Deterministic tests + CI
- **Files (new):** `frontend/src/playground/usePlaygroundExecutionGraph.test.jsx`,
  `frontend/src/playground/PlaygroundExecutionGraph.test.jsx`, and a **reuse-regression** test
  `frontend/src/observability/layout.test.js` (guards `computeGraphLayout` output shape — the guard
  that replaces the freeze). **Intent:** §16. **Acceptance:** all green in the existing frontend CI
  job (P2-M3 T8). **Risk:** low.

### Phase 8 — Genuine live verification (paid — one run)
- Per §17. **Risk:** medium (paid); mitigate by reusing the P2-M4 harness + a single run.

### Phase 9 — Visual verification
- Per §18 (headless Playwright, as used for the P2-M4 Timeline pass). **Risk:** low.

### Phase 10 — Final scope audit + integration
- Diff-scope audit (only `frontend/src/playground/*` new, `App.jsx` + `ObservabilityPage.jsx`
  top-level + the master-plan doc changed; **Timeline block byte-identical**, backend untouched);
  independent review; merge to a branch/worktree; push per separate authorization.

## 15. Exact file-by-file change list (implementation)

**New:**
- `frontend/src/playground/usePlaygroundExecutionGraph.js`
- `frontend/src/playground/playgroundSpanModel.js`
- `frontend/src/playground/PlaygroundExecutionGraph.jsx`
- `frontend/src/playground/usePlaygroundExecutionGraph.test.jsx`
- `frontend/src/playground/PlaygroundExecutionGraph.test.jsx`
- `frontend/src/observability/layout.test.js` (regression guard — no `layout.js` change)

**Modified (bounded):**
- `frontend/src/App.jsx` — Playground result pane: `playgroundViewMode` + toggle, dispatch events
  into the graph reducer, render graph vs. card list, handoff button, `reset()` wiring. *(No change
  to submit/approve/reject/SSE-contract/card reducer semantics.)*
- `frontend/src/ObservabilityPage.jsx` — **top-level `ObservabilityPage()` only** (`:808`): optional
  `initialTaskId` prop. **Timeline block (`buildSpanTree`/`SpanRow`/`TimelineView`, ~L148–444) and
  `observability/*` remain byte-identical.**
- `docs/superpowers/plans/2026-07-26-full-platform-redesign-master-plan.md` — freeze status note.

**Protected — MUST NOT change:** `observability/AgentNode.jsx`, `ToolCallNode.jsx`, `TraceEdge.jsx`,
`NodeParts.jsx`, `layout.js`, `format.js`, `timelineTheme.js`, `timelineFocus.css`, `DetailPanel.jsx`,
`sampleGraphData.js`, `sampleDetailData.js`, `DensePaletteHarness.jsx`, `densePaletteFixture.js`;
the Timeline block in `ObservabilityPage.jsx`; `DESIGN.md`; `theme/tokens.js`/`typography.js`;
`.github/workflows/*`; all backend `src/**`; migrations/schema; the SSE contract.

## 16. Deterministic test plan

Reducer/model (`usePlaygroundExecutionGraph`):
- **T1** initial state = empty spans.
- **T2** `agent_started` → agent span, parent `"orchestrator"`, Running.
- **T3** `tool_started` → tool span, parent = agentExecutionId, Running.
- **T4** parent/child: db_query under Data; perplexity under Research.
- **T5** parallel siblings: two `agent_started` (Data, Research) → both parent `"orchestrator"`,
  distinct rows via `computeGraphLayout` (assert same-start X, different Y).
- **T6** completion: `agent_completed`/`tool_completed(success)` → Completed.
- **T7** failure: `agent_failed`/`tool_completed(!success)` → Failed.
- **T8** duplicate `*_completed` → idempotent (no double node, stays Completed).
- **T9** out-of-order: `tool_completed` before `tool_started` → node created terminal.
- **T10** unknown parent → attached to `"orchestrator"`, never dropped.
- **T11** repeated same tool (×3 perplexity) → 3 distinct tool nodes matched in arrival order.

Rendering (`PlaygroundExecutionGraph`): node count/type from a static span array; empty state.
Toggle/handoff (App): default graph; toggle preserves state; handoff button appears only on
`task_completed`; ManagerReviewCard result not duplicated.
Regression (`layout.test.js`): `computeGraphLayout` given the recorded 18-span P2-M4 shape yields
the expected nodes/edges/parents (locks the reused contract — replaces the freeze).

## 17. Genuine live verification plan (one paid run)

- **Task:** the pre-registered Sports task (runbook §1, Kawhi Leonard Dec 12 2017–Jan 13 2018).
- **Infra:** the P2-M4 harness — hardening/main code, `.env.local` sourced (never printed),
  `Kestrel__Endpoints__Http__Url=http://localhost:5199`, ephemeral `Admin__BootstrapSecret`, tenant
  + key bootstrap, submit + start.
- **Required trace characteristics:** Orchestrator → parallel Data + Research → ≥1 Data `db_query`
  → multiple Research `perplexity_search` → Manager Review → Completed with `finalResult`.
- **Observe (via the running frontend graph):** graph builds live; Data & Research appear as
  overlapping siblings; tool nodes nest live; running→completed transitions visible; review node
  appears; handoff works.
- **PASS:** task Completed, graph rendered all nodes with correct hierarchy live, parallel visible,
  no stuck Running, ManagerReviewCard populated (opportunistic T1 visual).
- **FAIL:** crash/stuck/incorrect hierarchy/graph desync from the authoritative `/timeline`.
- **Avoid paid retries:** the P2-M4 fixes make success expected; do **one** run; if it fails,
  capture evidence and STOP (do not re-roll) — treat as a finding, not a retry loop.

## 18. Visual verification plan (headless Playwright, no code change to app)

Reuse the P2-M4 pattern (scratch-dir Playwright driving the real frontend). Verify: (1) graph
renders during live execution; (2) parallel Data/Research distinguishable; (3) tools nested under
agents; (4) running/completed/failed states clear; (5) Manager Review visible; (6) Graph/List
toggle; (7) List fallback intact; (8) final result intact; (9) Observability handoff pre-selects
the task; (10) Observability **Timeline** unchanged (screenshot diff vs. the P2-M4 baseline).

## 19. Protected-area & scope-integrity checks

- Before/after: `git diff --stat 829546e HEAD -- frontend/src/observability/` shows only the 2
  pre-existing harness files (Timeline renderer byte-identical).
- `ObservabilityPage.jsx` hunks confined to the top-level component (>~L800); **no hunk in
  L148–444** (Timeline block).
- No backend/`src/**`, migrations, CI, tokens, DESIGN.md, SSE-contract change.
- **Hard STOP** if any primitive genuinely needs a live-data prop change → re-scope as an explicit
  (small, tested, behavior-proving) observability change with sign-off (§ "last resort" in the
  protected-area rule), not a silent edit.

## 20. Risks & mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Live streaming reducer edge cases (dup/out-of-order/unknown parent) | Med | §9 tolerant fold + T8–T11 tests |
| App.jsx SSE handler is inline (no reducer seam) — change risk | Med | Additive dispatch only; do NOT extract the switch (P2-M3 lesson); card reducer untouched; existing tests guard |
| A primitive needs a live prop → touches frozen code | Med | §4 proves not needed; if hit, STOP + re-scope with sign-off |
| Graph/axis alignment (known minor) surfaces here | Low | Out of scope unless trivial (List/graph both legible; P2-M4 accepted it) |
| Paid run cost/flakiness | Med | One run; P2-M4 fixes make success expected; no retries |
| Handoff prop on ObservabilityPage | Low | Top-level component only; optional prop; Timeline untouched |

## 21. Explicit out-of-scope

Backend/endpoints/DB/migrations/SSE-contract; auth; Observability **Timeline** redesign/behavior;
`observability/*` edits (reuse only); T6; A12; docs website; deployment/packaging; onboarding
redesign; ADR-014 #11; graph-axis refinement (unless trivial); any reducer/SSE-architecture
extraction/refactor.

## 22. Definition of Done

Live Playground execution graph shipped behind a Graph/List toggle with the card List fallback
intact; run→Observability handoff; **new `playground/*` module + bounded `App.jsx`/`ObservabilityPage.jsx`
(top-level) changes + the freeze doc note — nothing else**; deterministic tests (T1–T11 +
rendering + toggle/handoff + `layout.js` regression guard) green in frontend CI; **one** genuine
live-run verification captured (with opportunistic ManagerReviewCard visual); headless visual pass
incl. Timeline-unchanged diff; scope audit proves Timeline block + `observability/*` byte-identical
and backend untouched; independent whole-branch review; merged to a branch/worktree and pushed per
separate authorization. No paid retries; no protected-area edits; no secrets exposed.

## 23. Recommended implementation order

Phase 0 (freeze doc) → Phase 2 (reducer + tests) → Phase 3 (render + test) → Phase 4 (toggle/App
wiring) → Phase 5 (review) → Phase 6 (handoff) → Phase 7 (full tests + CI) → **checkpoint** →
Phase 8 (one paid live run) → Phase 9 (visual) → Phase 10 (audit + review + integrate). Phase 1 is
skipped unless a primitive live-prop need is discovered (then STOP + re-scope).
