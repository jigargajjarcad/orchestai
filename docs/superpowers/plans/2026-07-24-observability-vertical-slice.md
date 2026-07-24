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

**Acceptance criteria:** both node types render correctly at all five `STATUS_COLORS` states;
agent-identity coloring matches `AGENT_COLORS` exactly; both types are keyboard-focusable.

**Verification:** isolated component render (the established `react-dom/server`/Vite
`ssrLoadModule` technique from Milestones 2–3) for static appearance; a live check (this state
*is* reachable without any backend, since it's hand-constructed sample data, not real API data)
for interactive/focus behavior.

### Task 3: Layout engine — span tree → graph positions

**Scope:** the core translation logic: given `buildSpanTree`'s output and the existing
`traceStart`/`traceDurationMs` values, compute `{x, y}` node positions (x from timing, matching
`SpanRow`'s existing offset math; y from tree depth/branch index) and edge connections
(parent→child). No auto-layout library (§3).

**Acceptance criteria:** given a hand-constructed span tree with at least one sequential chain
and one parallel fan-out (two children of the same parent), the computed layout places parallel
siblings at distinct y-positions with no overlap, and sequential spans progress left-to-right
matching their actual start times.

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
render path with the graph view, and add an explicit toggle back to the existing `SpanRow`-based
list rendering (§3/§6 item 5) — both views read from the same `buildSpanTree`/timing data,
already fetched once.

**Acceptance criteria:** `git diff --stat` confined to `ObservabilityPage.jsx` plus the new
files from Tasks 1–4; the other four sub-views (`SummaryView` etc.) have zero diff; `App.jsx`
has zero diff (§1); both List and Graph views render correctly from the same fetched data;
switching between them preserves the selected task.

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
