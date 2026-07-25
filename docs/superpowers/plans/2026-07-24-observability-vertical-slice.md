# Observability Vertical Slice — Implementation Plan (Planning Only)

> **Status: planning only. Not approved for implementation.** No source/application file was
> modified while producing this document. No implementation worktree was created.
> `@xyflow/react` was not installed in the actual project — its bundle-size figures below come
> from an isolated, throwaway scratch project, not from this repository. The Milestone 3
> worktree (`worktree-milestone3-cross-page-consistency`, commit `b24d5e6`, 6 of 7 tasks
> complete) was not touched, merged, or continued. `main`/`origin/main` remain unchanged at
> `05d4917`.

**This is Option B from `docs/superpowers/plans/2026-07-24-frontend-design-competitive-
benchmark.md`: a bounded validation experiment, not the first step of an already-decided full
redesign.** A successful implementation of this plan does not by itself authorize proceeding to
the full redesign — that is a separate Go/No-Go decision, made after this slice is complete, by
comparing the finished result against the original competitive benchmark (§10 of that document).

---

## 1. Verified Current-State Evidence

All of the following was checked directly against the actual repository this session, not
recalled from memory or from prior planning documents.

**`main`/`origin/main`**: confirmed identical at `05d4917` via fresh `git rev-parse` on both
refs immediately before writing this plan.

**`frontend/src/ObservabilityPage.jsx`** (617 lines) — the file this plan will modify — current
relevant structure, read directly from `main`:
- `buildSpanTree(spans)` (line 131): converts a flat `spans` array (fields confirmed by direct
  reading: `spanId`, `parentSpanId`, `label`, `spanType` — `'ToolCall'` or otherwise treated as
  an agent-execution span — `status`, `startedAt`, `completedAt`, `durationMs`, `costUsd`) into
  a parent→children tree, sorted by start time. **This plan reuses this function unchanged.**
- `SpanRow` (line 149): the current list-based renderer — indentation by depth, a CSS-percentage
  offset/width bar, right-aligned duration/cost/status text. This is being replaced as the
  *primary* view (see §3), not deleted (see §6.5's accessibility architecture).
- `TimelineView` (line 198): owns the `taskId`/`data`/`error` state, fetches
  `GET /tasks/{taskId}/timeline` (returns `{ traceId, spans }`) on task selection, computes
  `traceStart`/`traceDurationMs` from the flat span list, and renders `SpanRow` per root span.
  **This plan reuses its data-fetching and timing-computation logic unchanged** — only its
  render output changes.
- `STATUS_COLORS` (line 23): `{ Pending, Running, Completed, Failed, Skipped }` — **confirmed,
  by direct reading, to contain no `WaitingForApproval` entry.** This is a concrete, material
  finding for scope (§4).
- The other four Observability sub-views (`SummaryView`, `DashboardView`/Cost Dashboard,
  `ErrorRatesView`, `CompareView`) are unrelated to this plan and are not touched.

**Milestone 3 scope isolation, verified via actual `git diff`, not filenames alone:**
```
git -C .claude/worktrees/milestone3-cross-page-consistency diff 05d4917 b24d5e6 --stat
 DESIGN.md                          |  13 +-
 frontend/src/App.jsx               |  33 +-
 frontend/src/EvalsPage.jsx         |   2 +-
 frontend/src/components/Panel.jsx  |   7 +-
 frontend/src/theme/tokens.js       |  45 +-
```
**`frontend/src/ObservabilityPage.jsx` has zero diff between `main` and Milestone 3's paused
worktree.** Milestone 3 does touch `App.jsx` (33 lines: `ApprovalCard` tokenization/migration,
`MemoriesPage` Delete-button correction, header tokenization) and `Panel.jsx` (7 lines,
comment-only — confirmed no functional change, `panelAccentBorderAlphaSuffix` stayed `'40'`).
**This plan is designed so it requires zero changes to `App.jsx`** (§4/§5) — `ObservabilityPage`
takes no props today and this plan doesn't change that, so its call site in `App.jsx`
(`{view === 'observability' ? <ObservabilityPage /> : ...}`) needs no edit. Combined with zero
diff in `ObservabilityPage.jsx` itself, **this plan has no dependency on, and cannot conflict
with, anything in the paused Milestone 3 worktree.**

**Package manager**: `frontend/package-lock.json` exists; no `pnpm-lock.yaml`/`yarn.lock` —
**npm** is this project's package manager, confirmed by direct inspection, not assumption.

---

## 2. React Flow — Verified Bundle Size, Dependency Tree, and Production Impact

Checked directly via `npm view` (real registry data, not recalled) and a real, isolated
build test — **`@xyflow/react` was never installed into this repository; a throwaway scratch
project was used, then deleted.**

**(a) Published package metadata** (`npm view @xyflow/react`):
- Version `12.11.2`, MIT license, **3 direct dependencies**: `classcat` (^5.0.3, zero deps of
  its own), `zustand` (^4.4.0 — a general state-management library this project does not
  currently use anywhere), `@xyflow/system` (0.0.79, 9 further dependencies of its own).
- **Unpacked package size: 1.2 MB.** This is the npm-published tarball's unpacked size
  (includes source, type declarations, etc.) — **not** the browser bundle size; do not conflate
  the two, per the explicit instruction.

**(b) Full dependency tree impact**: installing `@xyflow/react` into a clean Vite+React project
added **21 packages** to `node_modules` (verified via `npm install` output in the isolated
scratch project, not estimated).

**(c) Actual production bundle impact — measured, not estimated**, via a real `vite build` in
an isolated scratch project matching this repo's own build tooling (Vite, same React version):

| | Raw JS | Gzip JS | Raw CSS | Gzip CSS |
|---|---|---|---|---|
| Baseline (React + ReactDOM only) | 190.43 kB | 59.98 kB | — | — |
| + `@xyflow/react` (with `Background`, `Controls`, `MiniMap`, core CSS) | 368.23 kB | 116.95 kB | 15.41 kB | 2.56 kB |
| **Delta (React Flow's actual cost)** | **+177.8 kB** | **+56.97 kB** | **+15.41 kB** | **+2.56 kB** |

**Relative to this project's own current production build** (measured fresh on `main` this
session: `401.63 kB` raw / `118.49 kB` gzip JS, `1.75 kB` raw / `0.82 kB` gzip CSS): adding
React Flow would raise the gzipped JS bundle from **118.49 kB to roughly 175.5 kB — a ~48%
increase.** This is a real, material cost, not a rounding error, and is stated plainly rather
than minimized. It is, to date, the single largest dependency this project would ever add —
categorically larger than `remark-gfm`'s narrow addition in Milestone 2.

This cost is accepted as justified per the investigation's own §14 reasoning (pan/zoom and
accessibility are too costly to hand-roll correctly) and is not re-litigated here — but the
concrete number belongs in this plan, not just the investigation, since it is now a real
implementation-time cost this specific plan will incur.

---

## 3. In Scope / Out of Scope

### In scope
- Replacing `ObservabilityPage.jsx`'s `TimelineView` render output with a React-Flow-based
  execution graph — nodes for agent-execution spans and tool-call spans, edges for
  parent/child relationships, computed from `buildSpanTree`'s existing output plus the existing
  `traceStart`/`traceDurationMs` timing math (both reused unchanged, per §1).
- A new, narrowly-scoped visual system for this view only: typography, color extensions,
  elevation/depth treatment, node/edge styling, a detail panel — new files, not modifications to
  the shared `frontend/src/theme/tokens.js` or `frontend/src/components/*` (Milestone 1's shared
  library stays untouched; this is a deliberate isolation choice, not an oversight).
- Reuse of the existing Agent Identity Palette (`AGENT_COLORS`, already keyed by agent label in
  `ObservabilityPage.jsx`) for node coloring, and the existing `STATUS_COLORS` mapping (extended
  only by adding whatever new keys are needed for the five statuses it already lists — no
  redefinition of what those colors are).
- A "List view / Graph view" toggle within `TimelineView`, keeping the existing `SpanRow`-based
  rendering available and unmodified (architectural reasoning in §6.5).
- Responsive behavior for this view, reusing Milestone 2's existing `useViewportWidth` hook
  unchanged.
- Accessibility work scoped to this view (§6).
- Adding `@xyflow/react` as a new dependency — **planned, not executed**, in a future
  implementation pass only.

### Out of scope (explicit, not silently assumed)
- **`SummaryView`, `DashboardView` (Cost Dashboard), `ErrorRatesView`, `CompareView`** — the
  other four Observability sub-views are untouched.
- **`App.jsx`, routing, the header/nav, `MemoriesPage`, `EvalsPage.jsx`, the Playground/
  execution experience** — zero changes, confirmed achievable in §1.
- **The shared component library** (`Panel`, `Button`, `StatusBadge`, `Input`, `NavItem`,
  `StateText`, `Label`) and `theme/tokens.js` — untouched. This is not the product-wide
  design-system rebuild (Phase B of the full-redesign roadmap) — it is a self-contained,
  parallel visual system for one view only.
- **Milestone 3** — stays paused, untouched, unmerged (re-confirmed in §1).
- **"Human approval states" as a distinct rendered node/edge type in this Timeline view** —
  **named, honest limitation, not silently dropped**: `STATUS_COLORS` (§1) confirms the
  backend's historical span data model, as exposed to this endpoint, has no
  `WaitingForApproval` status value. Approval is a live, in-flight Playground concept
  (`ApprovalCard`), never persisted into the historical trace this view reads. This plan cannot
  visualize a state the data it consumes doesn't carry. If this is later found to matter, it is
  a backend/API scope question, not a frontend rendering one, and is out of scope for this plan
  regardless.
- **Live/streaming updates to the graph while a task is still running** — `TimelineView`'s
  existing data model is a one-time fetch on task selection (§1), not an SSE subscription. This
  plan's "animated edges" (§6.4) are a stylistic/motion treatment applied to an already-completed,
  static historical trace — not a claim that this view will update live as a task executes. Real
  live-execution visualization is the Playground surface's own, separate, out-of-scope concern.
- **An auto-layout library** (e.g. `dagre`, `elkjs`) — not needed. Span timing data already
  gives exact X-axis positions (the same math `SpanRow`'s `offsetPct`/`widthPct` already uses);
  tree depth/branch index gives Y-axis grouping. This keeps the new-dependency surface to
  exactly `@xyflow/react`, nothing else.
- **A dev-only mock/seed data path** — not part of this plan, per the investigation's own §8
  decision 5 (deferred). Verification uses isolated component rendering with hand-constructed
  span-tree data, the same method established across Milestones 1–3 for every other
  environment-blocked state.

---

## 3a. Layout Positioning Decision — What "Timeline" Actually Means (durable, required for Task 2/3)

**Added at the Task 1 review checkpoint, in response to a direct question: does horizontal
position represent real time, or is it just incidental hierarchy-driven placement?** This
section is the authoritative answer, recorded here so the decision is durable and not left in
chat history. Task 2 and Task 3 must implement exactly what this section states.

### 3a.1 — The exact data available (verified against the real DTO)

Confirmed by direct reading of the actual backend contract,
`src/OrchestAI.Application/Queries/GetExecutionTimeline/GetExecutionTimelineResponse.cs`, its
`TimelineSpanDto` record — the exact fields the `/tasks/{id}/timeline` endpoint returns:
- `StartedAt` (`DateTimeOffset?`), `CompletedAt` (`DateTimeOffset?`), `DurationMs` (`int?`) —
  **real per-span timing data exists.**
- `SpanId` (`string`), `ParentSpanId` (`string?`) — the parent/child relationship.
- `Status`, `SpanType` (`"AgentExecution" | "ToolCall"`), `Label`, `ErrorCategory`,
  `InputTokens`, `OutputTokens`, `CostUsd`.

The DTO's own code comment states the binding architectural rule (ADR-011): *"The frontend
reconstructs the nested/Gantt tree from SpanId/ParentSpanId — never from timestamp ordering."*

### 3a.2 — The decision: a time-anchored hybrid layout (not purely topological)

**Horizontal (X) position IS derived from real timing data**, not from incidental
hierarchy-driven placement. Specifically, each node's X is computed from its `StartedAt` against
the trace's overall `traceStart`/`traceDurationMs` — the **exact same offset math the current
`SpanRow` already uses** (`ObservabilityPage.jsx`, `SpanRow`: `offsetPct = (new Date(startedAt) −
traceStart) / traceDurationMs`). So a span that started later sits further right; two spans that
started at the same moment sit at the same X. **This makes execution order and parallelism
genuinely visually derived from the timing data**, exactly as the review required: if
Orchestrator launches Research and Code in parallel, they share a start time and therefore share
an X-anchor, appearing as co-started branches *because the timestamps say so*, not because a
layout algorithm happened to place them side by side.

**Vertical (Y) position comes from the parent/child tree** (`ParentSpanId` → depth and
branch index), per ADR-011 — hierarchy is reconstructed from IDs, never inferred from
timestamps. Edges are drawn parent→child from the same ID relationships. There is no conflict
with using timestamps for X: ADR-011 forbids inferring *hierarchy* from time, which this does
not do — hierarchy is IDs, horizontal position is time. The two axes carry two different, real
signals.

**This is therefore not a pure topological graph, and it is not a pure Gantt either — it is a
time-anchored execution graph.** Being precise about what that does and does not claim:

- **What X genuinely represents:** each span's real start time (order, start-offset,
  co-start/parallelism — all time-derived).
- **What X does NOT, on its own, represent:** *duration*. React Flow nodes are fixed-size
  cards, not variable-width bars, so — unlike `SpanRow`'s Gantt bar, whose *width* encodes
  `DurationMs` — a node's card width does not encode how long the span ran. Without
  compensation, duration and the *degree* of time-overlap between two parallel branches would be
  lost. This is exactly the gap the review flagged, and it is addressed explicitly next.

### 3a.3 — Compensating mechanism for duration and overlap (required, not optional)

Because fixed-size cards can't encode duration via width, each node **must** carry a real
duration/time-extent indicator so nothing the current Gantt view shows is lost:

1. **An explicit per-node duration figure** in the node's meta-row (already in the Task 1
   visual direction — the `4.2s` / cost row on every agent node). Always present, always the
   real `DurationMs`.
2. **A per-node span-extent mini-bar**: a small horizontal bar rendered inside/under each node
   showing that span's actual start→end position and length against the trace's total duration —
   i.e. the direct per-node equivalent of `SpanRow`'s existing Gantt bar (`offsetPct`/`widthPct`
   math, reused). This is what keeps *duration* and *degree of overlap between parallel branches*
   visible: two parallel siblings at the same X-anchor whose extent bars are different lengths,
   or overlap only partially, show that relationship directly and truthfully.
3. **A lightweight time axis/ruler** across the top of the canvas giving absolute scale (e.g.
   `0s … 6.1s`), so the X-anchoring is legible as time, not just relative ordering.

Items 1–2 are **required acceptance criteria for Task 2/3** (added to those tasks below); item 3
is required for Task 5's integration. Without them, the "Timeline" name would overclaim — with
them, it is accurate.

### 3a.4 — Naming decision

**The surface keeps its existing name, "Timeline"** (the current sub-tab label,
`ObservabilityPage.jsx`'s `SubNav`). This is honest *specifically because* §3a.2 commits to
genuine time-anchored X positioning plus §3a.3's duration/extent indicators — the view really is
a timeline, not a topology diagram wearing a timeline's name.

**Required conditional rename, flagged now rather than decided silently later:** if, during
Task 3's implementation, genuine time-anchored X positioning proves impractical (e.g. real
traces cluster too many near-simultaneous spans to lay out legibly on a time axis) and the
implementation falls back to a purely topological/hierarchy-driven X, **then renaming the
surface to "Execution Graph" becomes a required change** — the name must not imply a
time-position claim the implementation doesn't back up. This is a named branch point for the
Task 3 implementer, not a decision to defer.

---

## 3b. Edge Visual Language — Review-Refined (durable)

Recorded at the Task 1 review checkpoint. The Task 1 first draft's edges were judged too thin
and too quiet for how important the relationship they carry is (this view's whole purpose is
making "who called whom / execution flow / parallel branches / failures" immediately legible).
The refined, approved rule:

- **Heavier stroke:** ~2.5px for completed/idle edges, ~3px for a running edge (up from the
  first draft's 1.5/2px).
- **Explicit directional cue:** a small, restrained arrowhead at the child (target) end,
  conveying call direction (parent → child). Deliberately styled — not React Flow's generic
  default marker — sized to read clearly without becoming busy.
- **Identity/status color rules unchanged from Task 1** (this was an explicit review
  constraint): a completed edge takes its **source node's identity color** at reduced opacity;
  a running edge uses the running-blue with the animated dashed flow; a **failed path is always
  status-failed red and dashed, regardless of identity** — state overrides identity, matching the
  badge system's own Two-Palette behavior. The arrowhead inherits the edge's own color.

The updated style-guide artifact reflects this (§05 edge samples and §06 composed example).
Task 2's acceptance criteria carry it forward into the real components.

## 3c. Interaction Affordances — Confirmed for Task 2 (durable)

The static style guide is intentionally a flat diagram and does not need to show pan/zoom/drag.
**The real implementation will read as an interactive graph, not a static picture** — confirmed
as a Task 2/5 requirement here:

- **Pan/zoom:** the canvas mounts React Flow's `Background` (the dotted grid, already previewed
  in the artifact's composed example) and `Controls` (zoom-in/out/fit-view buttons), so the
  surface visibly affords panning and zooming the moment it renders — a standard, immediately-
  recognizable "this is a canvas you can move" signal. `Controls`' buttons must be keyboard-
  operable (§6 item 3 / Task 6 acceptance criterion — verified, not assumed).
- **Node interactivity:** nodes carry a visible hover state (the elevation/lift previewed in the
  artifact) and a visible selected state (the accent ring), and the cursor changes over them —
  so they read as clickable, not as static labels. Clicking/keyboard-activating opens the detail
  panel (Task 4).
- **Drag:** nodes are positioned by the §3a layout, but React Flow's node-drag remains available
  for a user to nudge the view; this is a low-priority affordance (the layout is the source of
  truth) and is explicitly *not* allowed to persist or feed back into positioning — dragging is
  view-local only, never mutating the timing-derived layout.

These are confirmed as real Task 2/5 deliverables, not just style-guide claims.

---

## 4. Scoped Visual System — What's New, What's Reused

| Element | Decision |
|---|---|
| Typography | New: a readable proportional face for node labels/detail-panel prose, monospace retained for IDs/timestamps/cost figures — testing the investigation's own §7 recommendation (mono-only reconsidered for prose, retained for data) on exactly one surface, as this whole slice is meant to do. |
| Color — agent identity | **Reused**, unchanged: `AGENT_COLORS`/the existing Agent Identity Palette values already in `ObservabilityPage.jsx`. |
| Color — status | **Reused**, unchanged: `STATUS_COLORS`' existing five values; no new status semantics invented. |
| Elevation | New: real depth (shadow or soft-elevation equivalent) for nodes and the detail panel — testing the investigation's §7 "flat-by-default" reconsideration, scoped to this view only. |
| Node/edge visual language | New, this plan's actual deliverable — see §6. |
| Detail panel | New. |

---

## 5. Files Touched (Future Implementation — Not Modified Now)

- `frontend/src/ObservabilityPage.jsx` — `TimelineView`'s render output only; `buildSpanTree`,
  `STATUS_COLORS`, `AGENT_COLORS`, and the other four sub-views' code are not touched beyond
  what `TimelineView` itself already owns.
- New files (exact structure decided at implementation time, indicatively): a scoped
  theme/style module for this view; custom React Flow node components (agent-span, tool-call
  span); a custom edge component; a detail-panel component; a layout/position-computation
  module translating `buildSpanTree`'s tree + timing data into React Flow's node/edge shape.
- `frontend/package.json` / `frontend/package-lock.json` — **only at actual implementation
  time**, to add `@xyflow/react`. Not touched during this planning pass.

**No other file changes.** `App.jsx` is explicitly not on this list (§1/§3).

---

## 6. Accessibility — Specific Supplementary Work Beyond React Flow's Own Baseline

React Flow's documented feature set includes keyboard-focusable nodes/edges and arrow-key
repositioning when a node is focused — **this plan does not independently re-verify that
against a real screen reader in this planning pass; that verification is this plan's own Task 6
(§7), not asserted as already-confirmed here.** Named, concrete supplementary work this plan
commits to regardless of exactly how complete the library's own baseline turns out to be:

1. **ARIA relationship description.** Tabbing between nodes does not inherently convey *why*
   one node follows another (parent dispatched child, sequential vs. parallel). Each node needs
   an `aria-describedby` (or equivalent) pointing to a short generated text description of its
   role in the trace ("Research, child of Orchestrator, ran in parallel with Code").
2. **Focus management for the detail panel.** Opening it (via click or Enter/Space on a
   focused node) must move focus into the panel; closing it must return focus to the
   originating node. Neither is automatic from the library.
3. **Keyboard-operability of `Controls`' zoom/pan buttons** must be explicitly verified, not
   assumed — Task 6's own acceptance criterion, not a given.
4. **Contrast and semantic markup inside custom node content** (status text, cost/duration
   figures) is entirely this plan's own responsibility — the library provides layout, not
   content semantics.
5. **The List-view toggle (§3) is this plan's primary accessibility answer, not a secondary
   nicety.** Canvas-based graph UIs are a known-hard, arguably never-fully-solved accessibility
   problem even in mature libraries — rather than asserting the graph view itself will be fully
   screen-reader-equivalent (a claim this plan is not in a position to make honestly), the
   existing `SpanRow`-based list rendering stays available, unmodified, as a genuinely
   robust, low-cost, already-proven-accessible alternative path satisfying `PRODUCT.md`'s
   existing "fully keyboard-navigable" commitment regardless of how the graph view's own
   accessibility work lands.

---

## 7. Task Breakdown

Each task includes acceptance criteria and an explicit verification method, using this
project's established three-way classification (live / isolated component render / direct code
comparison) — consistent with Milestones 1–3.

### Task 1: Scoped visual-system foundation + early style-guide checkpoint

**Scope:** build the new typography/elevation/node-edge-base-style tokens (§4) in a new, scoped
file. Produce a static style-guide preview (a plain HTML/CSS page or artifact, not wired into
React Flow yet) showing: node appearance at each of the five statuses, edge appearance, detail
panel appearance, at both a wide and narrow viewport.

**Why this is first, deliberately:** per the investigation's own §13 finding that
design-taste-convergence is the highest-uncertainty part of any of this work, this task exists
to surface "does this feel right" *before* the harder graph-wiring work (Tasks 2–5) is built on
top of it — cheaper to redo a style guide than to redo styled, already-wired components.

**Acceptance criteria:** a reviewable static artifact exists showing every planned visual state;
no React Flow dependency, no `ObservabilityPage.jsx` changes yet.

**Verification:** live (a static HTML/CSS artifact is directly viewable).

### Task 2: Custom React Flow node types

**Scope:** build the agent-span and tool-call-span custom node components against Task 1's
visual system, using representative hand-constructed data (not yet wired to real span data).
Includes the per-node duration/time-extent indicators required by §3a.3 (the explicit duration
figure in the meta-row, and the per-node span-extent mini-bar) — these are node-component
concerns, built here, even though the trace-relative values that drive the mini-bar are supplied
by Task 3's layout math.

**Acceptance criteria:** both node types render correctly at all five `STATUS_COLORS` states;
agent-identity coloring matches `AGENT_COLORS` exactly (identity color never changes with status,
per the Task-1-approved rule); both node types show the §3a.3 duration figure and a span-extent
mini-bar; both types are keyboard-focusable. Edges follow the review-refined visual language
(§3b): heavier stroke than the Task 1 first draft, an explicit directional arrowhead, and the
existing identity/status color rules unchanged (source-identity color for completed at reduced
opacity; status-failed red + dashed for failed paths, overriding identity).

**Verification:** isolated component render (the established `react-dom/server`/Vite
`ssrLoadModule` technique from Milestones 2–3) for static appearance; a live check (this state
*is* reachable without any backend, since it's hand-constructed sample data, not real API data)
for interactive/focus behavior.

### Task 3: Layout engine — span tree → graph positions

**Scope:** the core translation logic, implementing §3a's time-anchored hybrid layout exactly:
given `buildSpanTree`'s output and the existing `traceStart`/`traceDurationMs` values, compute
`{x, y}` node positions where **x is derived from each span's real `StartedAt`** (the same
offset math `SpanRow` already uses, §3a.2), **y from tree depth/branch index** (from
`ParentSpanId`, per ADR-011), plus edge connections (parent→child). Also compute the
trace-relative start-offset and extent values each node's §3a.3 span-extent mini-bar needs. No
auto-layout library (§3). **If genuine time-anchored X proves impractical on real traces, invoke
§3a.4's required conditional rename to "Execution Graph" rather than silently shipping a
topological layout under the "Timeline" name.**

**Acceptance criteria:** given a hand-constructed span tree with at least one sequential chain
and one parallel fan-out (two children of the same parent with overlapping start times), the
computed layout: (a) places the parallel siblings at the **same or near-same X** because their
`StartedAt` values match — demonstrating parallelism is time-derived, not hierarchy-incidental
(§3a.2); (b) separates them on Y with no overlap; (c) progresses sequential spans left-to-right
by real start time; (d) produces per-node extent values that correctly reflect each span's
`DurationMs` relative to the trace, so duration and degree-of-overlap are recoverable (§3a.3).

**Verification:** direct code comparison / unit-level check of the position-computation
function against hand-constructed input/output pairs — this is pure data transformation, testable
without rendering anything.

### Task 4: Detail panel + node-click interaction

**Scope:** clicking (or keyboard-activating) a node opens a panel showing the span's full
detail (timing, cost, tool-call input/output if applicable, error message if failed); closing
returns focus per §6 item 2.

**Acceptance criteria:** panel shows correct content for at least one example of each span type
(agent span, tool-call span, failed span); focus moves into the panel on open and back to the
node on close.

**Verification:** isolated component render for content correctness; live for the focus-
management behavior (reachable without a backend, since it's driven by hand-constructed sample
data through Task 2/3's already-built pieces).

### Task 5: Integrate into `TimelineView`, with the List/Graph toggle

**Scope:** wire Tasks 2–4 into `ObservabilityPage.jsx`'s `TimelineView`, replacing its default
render path with the graph view, mounting React Flow's `Background`/`Controls` for the pan/zoom
affordances (§3c), and adding the §3a.3 time-axis/ruler across the top of the canvas. Add an
explicit toggle back to the existing `SpanRow`-based list rendering (§3/§6 item 5) — both views
read from the same `buildSpanTree`/timing data, already fetched once.

**Acceptance criteria:** `git diff --stat` confined to `ObservabilityPage.jsx` plus the new
files from Tasks 1–4; the other four sub-views (`SummaryView` etc.) have zero diff; `App.jsx`
has zero diff (§1); both List and Graph views render correctly from the same fetched data;
switching between them preserves the selected task; the Graph view shows the §3a.3 time axis and
visibly affords pan/zoom (§3c); the surface's label follows §3a.4 (kept "Timeline" unless the
conditional rename was triggered in Task 3).

**Verification:** code comparison for scope/diff confinement; live for the actual toggle
behavior, to whatever extent real or hand-constructed data can reach it in this environment
(§1's own established limitation — no backend is currently reachable here, so this uses the
same hand-constructed-data-driven live check as Tasks 2–4, not a real API-backed trace).

### Task 6: Accessibility + responsive pass

**Scope:** implement §6's five supplementary items; wire `useViewportWidth` (Milestone 2's
existing hook, unchanged) to adjust layout/detail-panel presentation at narrow widths.

**Acceptance criteria:** keyboard-only navigation reaches every node, opens/closes the detail
panel correctly, and reaches the List/Graph toggle; `Controls`' zoom/pan buttons are confirmed
keyboard-operable (not assumed); the view is usable (not just "doesn't overflow") at a narrow
viewport, matching Milestone 2's own 768px verification precedent.

**Carry-forward requirement from Task 2's review (specific, must not be lost):** a keyboard-
focused node must show a **visible** focus indicator. Task 2 established that nodes are
structurally focusable (React Flow's node wrapper sets `tabIndex=0` on itself), BUT: (a) React
Flow's own shipped CSS sets `outline: none` on `.react-flow__node:focus`/`:focus-visible`, and
(b) the `AgentNode`/`ToolCallNode` components' own `onFocus`/`onBlur` "lift" handlers are on an
inner child `<div>`, which the wrapper-level focus can never reach (focus events bubble to
ancestors, not descendants) — so as of Task 2, a real Tab keypress lands on the node but produces
**no visible feedback**. Task 6 must fix this concretely: add a scoped `.react-flow__node:focus-
visible` focus-ring treatment (in this view's own scoped styling, not the shared system), AND
reconcile the now-dead inner-`div` `onFocus`/`onBlur` handlers (either remove them or re-wire
focus visibility to the element that actually receives focus). This is the single named
functional gap carried out of Task 2.

**Verification:** live, via the established CDP-driven headless-Chrome technique, including an
actual keyboard-only Tab-through (matching Milestone 3 Task 6's precedent of using real
simulated Tab key presses rather than synthetic `.focus()` calls, which gives false negatives
for `:focus-visible` checks).

### Task 7: Final verification, accepted-limitations write-up, diff-scope audit

**Scope:** no new feature work — confirm the whole slice against this plan's own acceptance
criteria end-to-end, and write the completion report's honest live/isolated-render/code-
comparison classification for every claim, per this project's established standard.

**Acceptance criteria:** every prior task's acceptance criteria independently re-confirmed;
`git diff --stat` against `main` shows only `ObservabilityPage.jsx` and the new files listed in
§5, plus `package.json`/`package-lock.json` for the one new dependency; the "human approval
states" and "no live-streaming" limitations (§3) are stated explicitly in the final report, not
glossed over.

**Verification:** code comparison (the diff-scope audit itself) plus a final live pass repeating
the highest-value checks from Tasks 2–6.

---

## 8. Honest Effort and Calendar Estimate

Same basis as `docs/product-completion-backlog.md`'s §4 and the competitive-benchmark
investigation's §13: solo developer, evenings/weekends, full-time job continuing in parallel;
conservative figures treated as the more likely real outcome, not the floor.

| Task | Effort (best) | Effort (conservative) |
|---|---|---|
| 1 — Visual foundation + style-guide checkpoint | 2–3 days | 5–7 days |
| 2 — Custom node types | 2–3 days | 4–6 days |
| 3 — Layout engine | 3–5 days | 7–10 days |
| 4 — Detail panel | 1–2 days | 3–4 days |
| 5 — Integration + toggle | 1–2 days | 3–4 days |
| 6 — Accessibility + responsive | 2–3 days | 5–7 days |
| 7 — Final verification + audit | 1 day | 2–3 days |
| **Total (effort)** | **12–19 days (~2.4–3.8 weeks)** | **29–41 days (~5.8–8.2 weeks)** |

**Calendar (effort × this project's own established part-time ratio, ~1.5× best / ~1.6×
conservative, matching the investigation's §13 methodology exactly):**
**~3.6–5.7 weeks (~1–1.5 months) best case, ~9.3–13.1 weeks (~2–3 months) conservative.**

**Reconciliation with the investigation's own earlier, rougher vertical-slice estimate**
(`2026-07-24-frontend-design-competitive-benchmark.md` §15: ~3.5–6 weeks effort / ~5–9 weeks
best-case calendar, ~9–14 weeks effort / ~14–22 weeks conservative calendar): this plan's
figures land at or slightly below that range, not above it. Two concrete reasons, not just
optimism: (1) this plan explicitly excludes an auto-layout library (§3), which the earlier
estimate's "Observability build" figure may have implicitly priced in as an open question; (2)
this plan is scoped strictly to the Timeline sub-view, not "Observability" as a whole surface —
narrower than the earlier estimate's own framing in places. Per the same conservative-estimation
discipline this document is following, **treat this reconciliation as informative, not as
license to plan around the lower end** — the conservative column above remains the more likely
real outcome.

---

## 9. Summary

| Question | Answer |
|---|---|
| Does this plan depend on Milestone 3 in any way? | No — verified via direct `git diff`, zero overlap, zero shared file dependency. |
| Does this plan touch `App.jsx` or any other page? | No — by design, confirmed achievable. |
| Real bundle-size cost? | +56.97 kB gzip JS / +2.56 kB gzip CSS, measured; raises this project's total gzipped JS by ~48%. |
| Graph tech | `@xyflow/react` v12.11.2, 21-package dependency tree, per the investigation's own recommendation — not re-litigated here. |
| Auto-layout library needed? | No — span timing data + tree depth suffice. |
| Biggest named limitation | Historical "human approval" states cannot be shown — the backend's span data model doesn't carry that status. |
| Accessibility answer | React Flow's own baseline (not independently re-verified yet) *plus* five named supplementary items *plus* a preserved, unmodified List-view fallback as the primary accessibility guarantee. |
| Effort/calendar | ~2.4–3.8 weeks effort / ~1–1.5 months calendar best case; ~5.8–8.2 weeks effort / ~2–3 months calendar conservative. |

---

*Planning only. No source/application files were modified. `@xyflow/react` was not installed
in this repository — bundle figures came from an isolated, deleted scratch project. No
implementation worktree was created. The Milestone 3 worktree remains untouched at `b24d5e6`.
`main`/`origin/main` remain unchanged at `05d4917`. Nothing was committed or pushed. Awaiting
explicit approval before any implementation begins.*
