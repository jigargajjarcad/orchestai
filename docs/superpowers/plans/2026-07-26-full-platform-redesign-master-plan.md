# OrchestAI — Full Platform Redesign Master Plan

> **Status: planning only. Not approved for implementation.** No source/application file will be
> modified while producing this document. No implementation worktree will be created, no
> dependency installed, nothing committed or pushed. `main`/`origin/main` are at `829546e` (the
> merged Observability Vertical Slice). Milestone 3 remains paused and untouched at `b24d5e6`.

**Decision that authorizes this plan:** the Go/No-Go checkpoint resolved to **Option A —
continue the full-platform redesign.** The Observability Vertical Slice validated the new visual
direction technically and visually; the resulting consistency gap between that one polished
surface and the rest of the product confirms the redesign must expand platform-wide. This
document is the master plan for that expansion. It is design/strategy only — the first
implementation milestone is *recommended* at the end (§13) but not started.

---

## 1. Executive Summary & Core Strategy

OrchestAI today is a functionally complete multi-agent orchestration platform whose frontend,
outside the just-shipped Observability Timeline, still reads as an internally-consistent but
under-designed developer tool (established in the competitive-benchmark investigation,
`docs/superpowers/plans/2026-07-24-frontend-design-competitive-benchmark.md`). The Observability
slice proved a more modern visual language is achievable within this codebase's constraints.

**The single biggest risk this plan must manage is the one the slice created: a product where
one surface is dramatically more polished than everything around it.** Left unaddressed, that
gap makes the product feel *less* finished, not more — a half-redesigned app reads as broken.

**Therefore the core strategy is consistency-before-perfection, delivered in two layers:**

- **Layer 1 — Platform-wide visual alignment.** Bring *every* major surface into one coherent
  new visual language (shared tokens, shared primitives, navigation, typography, surfaces,
  status semantics, spacing, controls, interaction patterns) — breadth first, at a deliberately
  bounded depth. The goal of Layer 1 is that no surface looks like it belongs to a different
  product than any other. Observability is already at the target; Layer 1 brings the rest up to
  meet it.
- **Layer 2 — Deep UX refinement.** Only after the whole platform is visually coherent, invest
  in richer, workflow-specific redesigns of the highest-value surfaces (chiefly the
  Playground/execution experience, which the investigation flagged as the second surface needing
  a genuine structural rethink).

This ordering is the plan's central discipline: **Layer 1 across the whole product before any
Layer 2 depth on any single page.** It directly prevents repeating the vertical-slice
consistency problem.

---

## 2. What the Observability Slice Validated — Generalize vs. Keep View-Specific

The slice deliberately built a **scoped** theme (`frontend/src/observability/timelineTheme.js`)
and did **not** touch `frontend/src/theme/tokens.js` or `frontend/src/components/*`, to keep its
experiment isolated. Now that the experiment succeeded, each validated decision must be
explicitly sorted into "promote to the shared platform system" or "stays Observability-specific."
Do **not** blindly copy the scoped theme into the shared system.

### 2.1 — Promote to platform-wide (validated, generalizable)

| Validated decision (Observability) | Promote as platform principle |
|---|---|
| **Proportional UI font for labels/prose + monospace reserved for data** (`typography.uiFontFamily` / `dataFontFamily`) | **Yes — the single most important promotion, BUT it does not land in Phase 1** (see the correction below). The current shared system is mono-*only* (`tokens.js`'s `fontStack` everywhere); the investigation named this a primary "reads as a terminal, not a product" signal. Generalize the two-face split product-wide. (The specific face — a system stack standing in for Inter — and whether to self-host a webfont is its own decision, §3.1.) **Where mono is anchored, and why this is Phase-2 work:** the mono default is *not* driven by the shared components or a single token — it is hardcoded as a literal `fontFamily: '"JetBrains Mono", "Fira Code", monospace'` on the app's top-level shell `<div>` (`App.jsx:578`, inherited by the whole tree) and set again on `body` via `--mono` in `index.css`. Flipping either of those globally would wrongly flip *data* text to the proportional face too. Applying the *split* (UI text → proportional, data text → mono) is therefore inherently **per-element** work across the page files — it belongs to Phase 2's per-surface tasks, not Phase 1's shared-system rebuild. Phase 1 defines the two roles as tokens; Phase 2 is where each element is assigned to the correct role. |
| **Real elevation** (soft, restrained shadows: `elevation.node`/`.panel`) | **Yes.** Replaces the current "Flat-By-Default, zero shadow ever" rule. Generalize as a small, disciplined elevation scale (§3.4). |
| **Surface hierarchy with a raised card tone** (`surface.card` `#232336`, one step above the shared `base`, plus `cardHover`) | **Yes, as a principle** — cards get genuine presence via a raised surface + elevation. The *exact* hex may be re-derived when building the shared ramp (§3.3), not copied verbatim. |
| **Focus ring as a distinct interaction color** (`focusRing` = `traceBlue`, deliberately separate from identity and status palettes) | **Yes.** A visible, consistent keyboard-focus treatment is a platform accessibility requirement, and keeping it a third channel (not identity, not status) is a sound rule to generalize. |
| **Selection ring pattern** (`0 0 0 1px currentColor` driven by status color) | **Yes, as an interaction-state pattern** for selectable items generally. |
| **Agent Identity Palette + Status palette, never blended (Two-Palette Rule)** | **Already shared** (`tokens.js`'s `colors` + the slice re-exported them verbatim). Stays — this is existing, validated, keep it. |
| **Directional, weighted relationship edges; state-overrides-identity for failed paths** | **Principle generalizes** (any future graph viz follows the same identity/state color rules), but the concrete edge geometry is graph-specific (below). |

### 2.2 — Keep Observability-specific (do NOT generalize)

| Observability-scoped artifact | Why it stays view-specific |
|---|---|
| `AgentNode`/`ToolCallNode`/`TraceEdge`/`NodeParts`, `layout.js`, `timelineFocus.css`, the React Flow integration | Graph/trace rendering primitives. No other current surface is a node-edge graph (§6). |
| Node/edge geometry (`node.agentWidth`, `edge.strokeWidth`, arrow size) | Meaningful only for the graph canvas. |
| The span-extent mini-bar and the time axis/ruler | Trace-time-specific data-viz; not a general card element. |
| The `.react-flow__node:focus-visible` scoped CSS override + `--observability-focus-ring` var mechanism | A workaround for React Flow's own CSS; the *platform* focus ring (§3.7) will be a normal, cleaner token-driven treatment, not this override pattern. |
| `GraphHarness`/`DetailPanelHarness`/`sample*Data` | Verification scaffolding, not product code (already confirmed not bundled). |
| The detail-panel-as-drawer/bottom-sheet **content** | Trace-span-specific. The *pattern* (a contextual detail drawer that traps focus and returns it) is generalizable and worth extracting if other surfaces need it — but not urgently. |

**Rule of thumb for the whole redesign:** promote *principles and primitives that any surface
needs* (type, color, surface, elevation, spacing, focus, status); leave *domain-specific
rendering* (graphs, timelines, span bars) in its own module.

---

## 3. Product-Wide Design Principles (the new shared system, defined)

This section defines the target design language. It is a *specification to build toward in Layer
1*, not code — no tokens file is edited here.

### 3.1 — Typography system
- **Two families, by role** (the key change): a **proportional UI face** for headings, labels,
  navigation, prose, empty-state copy; **monospace** (the existing `'JetBrains Mono'` stack) for
  data — IDs, timestamps, durations, cost figures, code/JSON, span IDs. This is the
  Observability-validated split, generalized.
- **DECISION — APPROVED (2026-07-26): Option B — self-hosted IBM Plex Sans (UI) + JetBrains Mono
  (data).** Self-hosted, subsetted WOFF2, `font-display` set appropriately, **no external CDN**.
  **The reasoning for this decision, recorded exactly as approved — and the reasoning it must NOT
  rest on:**
  - **Not justified by** any claim that system fonts inherently look generic, less credible, or
    less commercially polished — that claim is not sufficiently supported and is explicitly not
    the basis for this choice.
  - **Reason 1:** JetBrains Mono is *already* an established part of OrchestAI's visual identity,
    so IBM Plex Sans provides a *coherent companion* (both are engineered for developer tooling)
    rather than introducing an unrelated type system.
  - **Reason 2:** the UI/mono split is necessarily Phase-2 per-element work regardless of which
    UI face is chosen (§2.1), so loading the self-hosted font during Phase 1 carries very little
    additional marginal implementation cost — the font is simply *available* when Phase 2 does
    the element-level split it has to do anyway.
  - **Reason 3:** IBM Plex Sans is a deliberate *fit for OrchestAI's technical/developer-oriented
    product context* — treated as a design and product-positioning choice, **not** as an
    objectively-proven commercial-credibility advantage.
  - **R1 measurement requirement (not an estimate):** before finalizing, verify against this
    repo — the actual font files used, actual weights loaded, actual WOFF2 sizes, actual
    bundle/network impact, loading behavior, licensing/source (IBM Plex Sans is OFL-1.1), and
    whether every loaded weight is genuinely necessary. Do not rely on the prior ~40–80 kB
    estimate; measure the real result.
- A single documented type scale (retire the current mono-only `heading/title/body/label`-only
  set in favor of a scale that spans display → caption, with the proportional/mono roles
  annotated per step).

### 3.2 — Color system
- **Keep** the Catppuccin-derived identity palette and the harder Status palette, and the
  Two-Palette Rule separating them — these are validated and already shared.
- **Reconsider** the strict **One-Accent Rule** (Trace Blue is the *only* interactive color).
  The investigation (§7 there) found single-accent austerity contributes to the
  under-designed read. The redesign should allow a slightly broader, still-disciplined
  interactive/semantic palette — e.g. Trace Blue for primary affordances, a documented
  destructive-action treatment (which Milestone 3 already began, §12), and semantic good/warn/
  critical distinct from the accent — while keeping color scarce enough to stay legible.
- **DECISION — APPROVED direction, breadth NOT locked (2026-07-26):** proceed with **one primary
  interactive accent + a controlled semantic palette** for agent identity, status, and data viz;
  **no decorative color added merely for variety.** The *number* of identity/accent colors is
  **not finalized yet** and must not be, until validated against a **dense** synthetic stress
  fixture (R1's first task — see below), because the existing style-guide fixture is too sparse
  (~10–11 nodes) to reveal color competition/ambiguity/noise at realistic density (§11's
  palette-density gap). The dense stress test must exercise: many agent nodes; multiple instances
  of the same identity; all supported identity colors; all relevant statuses; tool-call nodes;
  successful + failed paths; parallel branches; nested/deep hierarchy; enough simultaneous
  elements to expose color competition. It answers one question: *does the palette stay legible,
  hierarchical, and intentional at high information density?* **Do not expand the palette merely
  because the sparse fixture looks empty; do not keep it restrictive if density evidence shows
  controlled additional semantic differentiation materially improves comprehension.** The final
  palette is decided on stress-test evidence and documented as a design decision. This stress
  test is **design-validation only** — no backend dependency, no production-data-contract change.
- **Neutrals:** define an intentional neutral ramp hue-biased toward the accent (the slice's
  navy-violet surfaces were a step in this direction) rather than the current flat
  crust/base/mantle-only layering.

### 3.3 — Surface hierarchy
- A layered surface model: **canvas** (page) → **surface** (panels/cards, one step up) →
  **raised** (hover/active/selected) → **overlay** (drawers/modals/menus). The current system
  has effectively only two flat layers (crust page, base panel); the new model adds genuine
  surface presence, validated in Observability's `surface.card`/`cardHover`.

### 3.4 — Elevation principles
- Replace "Flat-By-Default, zero shadow ever" with a **small, disciplined elevation scale**
  (roughly: flat resting cards → soft shadow on raised/interactive cards → stronger shadow on
  overlays), matching the slice's restrained `elevation` tokens. Elevation stays *understated* —
  the product remains dense and technical, not a consumer app with dramatic drop-shadows.

### 3.5 — Spacing system
- The existing `spacing` scale (4/8/12/16/24) is sound and can carry forward, likely extended at
  the top end (32/48) for the more generous page-level rhythm a modern product uses. Density
  stays a value; the change is *intentional* rhythm, not uniformly-tight padding.

### 3.6 — Border / radius language
- The existing `radii` (3/4/6/8) is serviceable; the redesign should standardize which radius
  applies to which element class (inputs/buttons vs. cards vs. pills vs. overlays) rather than
  the current ad-hoc per-site choices, and likely add a larger radius for big surfaces
  (the slice used 10–14px on the canvas frame/nodes).

### 3.7 — Interactive states
- Define a **complete, consistent state matrix** for every interactive element class (button,
  input, nav item, toggle, selectable card/row, link): default / hover / active / focus-visible
  / selected / disabled / loading. The slice validated hover-lift + focus-ring + selection-ring;
  generalize as a documented matrix so every control behaves consistently. Keyboard focus-visible
  is mandatory on all of them (accessibility, §3.12).

### 3.8 — Status semantics
- One canonical status vocabulary product-wide: `Pending / Running / Completed / Failed /
  Skipped` (the real backend model — the slice confirmed no `WaitingForApproval` exists in trace
  data) plus the *live* approval state where applicable (Playground/Approvals only). The
  tinted-pill badge (`StatusBadge`) is the validated, keep-it status indicator; generalize its
  correct alpha values (`tokens.js` already has canonical 20%/55% suffixes) everywhere, retiring
  the ad-hoc per-site suffixes Milestone 1–3 were already cleaning up.

### 3.9 — Agent identity semantics
- Keep the fixed Agent Identity Palette (Orchestrator=Beacon Yellow, Research=Trace Blue,
  Writer=Signal Green, Code=Mauve, Data=Sky, Browser=Peach) as a **product-wide** convention:
  wherever an agent appears (Playground cards, Memories rows, Observability, Evals), it wears its
  identity color consistently. This is validated and already partially shared; the redesign makes
  it a universal rule, and pairs each agent color with a documented accessible-contrast text
  treatment.

### 3.10 — Data visualization language
- Establish shared conventions for the product's data surfaces (cost/token figures, timing,
  tables, charts, the trace graph): monospace + tabular-nums for all aligned numerics; a
  consistent chart palette derived from the identity/status systems (not arbitrary); the
  identity/state color rules applied uniformly. The Observability graph is the reference
  implementation of these conventions; other data surfaces (Cost Dashboard, Error Rates, Evals
  results) adopt the same language without necessarily becoming graphs (§6).

### 3.11 — Motion / animation principles
- **Restrained, purposeful, `prefers-reduced-motion`-respecting** (the slice's running-edge
  animation is the model: subtle, meaningful, honoring reduced-motion). Define a small motion
  vocabulary — hover/focus transitions, drawer/panel open/close, status transitions — and
  explicitly forbid decorative/ambient motion. Motion must never be required to understand state.

### 3.12 — Accessibility standards
- Carry forward and generalize the standard the product already commits to (`PRODUCT.md`: WCAG
  AA baseline, fully keyboard-navigable) and that the slice implemented for the graph: visible
  keyboard focus on every interactive element; correct ARIA (the slice's relationship
  descriptions are a high bar to match for other complex surfaces); focus management for
  overlays (focus-in, trap, Escape, focus-return — the slice's DetailPanel is the reference);
  reduced-motion support; sufficient contrast for the (broadened, §3.2) palette. **Accessibility
  is a first-class acceptance criterion in every phase, not a final pass.**

### 3.13 — Responsive behavior
- Reuse the existing, validated `useViewportWidth` hook and ~768px breakpoint precedent
  (Milestone 2 + the slice both use it). Define consistent responsive patterns: two-column →
  stacked, side-drawer → bottom-sheet (the slice's DetailPanel model), header wrap. Phone-width
  (<768px) optimization of dense views can remain a deliberately deferred tier (as A5a was),
  called out per surface.

---

## 4. Current-State Platform Audit

Every major surface, as it exists on `main` at `829546e`, with its current visual state, UX
problems, reuse potential, and whether Layer 1 alignment suffices or Layer 2 structural work is
needed. Citations are to actual current files.

| Surface | Current state | Key UX/visual problems | Reusable today | Layer-1 vs Layer-2 |
|---|---|---|---|---|
| **Application shell + navigation** | `App.jsx` header (`~:577-632`): logo wordmark + four flat text `NavItem`s (Playground / 🧠 Memories / 📊 Observability / 🎯 Evals) + "Change key". Routing via `useRouting` (Milestone 2). | Header is four words in a thin bar; no visual weight, no grouping; emoji-as-icons; hardcoded shell colors still present (`App.jsx:571-588` — Milestone 3 was mid-tokenizing these). | Routing hook, four-area IA, `Nav`/`NavItem`. | **Layer 1** (visual weight, real nav treatment, tokenized shell) — no structural IA change. |
| **Playground / execution experience** | `App.jsx` (`App()` + `AgentCard`/`ToolCallRow`/`ApprovalCard`/`ManagerReviewCard`): form-left, results-right; agents render as a **vertically stacked card list**, no drawn relationships. | The investigation's #1 structural gap: execution *structure* (who called whom, parallelism) is never visualized — a scrolling list of cards, not a graph. Also the least-differentiated screen. | Form controls, SSE handling, the agent/status data model (identical shape to what the graph consumes). | **Layer 1 first** (align cards/form to new system), **Layer 2 later** (the big one: a live execution-graph view, reusing the Observability graph infrastructure — see §6/§7). |
| **Observability** | **Done** — validated reference implementation (this slice). Timeline is a React Flow execution graph; the other 4 sub-views (Summary, Cost Dashboard, Error Rates, Compare) are the *pre-slice* flat style. | The 4 non-Timeline sub-views are now visually *behind* their own page's Timeline tab — an in-page consistency gap. | Everything — this is the reference. | **Layer 1** for the 4 non-Timeline sub-views (bring them up to the Timeline's language). Timeline itself: no further work (except the tracked backend-trace verification, §11). |
| **Memories** | `MemoriesPage` (inline in `App.jsx`, `~:231-309`): a data table (agent/key/value/importance/updated + Delete). | Flat table, minimal visual weight; empty state is one sentence in a void; Delete button palette was mid-fix in Milestone 3. | The table structure is honest for the data — keep it. | **Layer 1** (visual weight, agent-identity row accents, real empty state). No structural change. |
| **Evaluations** | `EvalsPage.jsx` (513 lines, 4 sub-tabs: Suites/Run/Results/Post-Hoc). | Forms-and-tables, flat; empty-suite copy was raw API hinting (fixed in paused Milestone 3); four sub-tabs lack visual hierarchy. | Structure is sound (forms/tables suit evals). | **Layer 1** (visual weight, hierarchy across sub-tabs, adopt the shared data-viz language for results). No structural change. |
| **Approvals / human-in-the-loop** | `ApprovalCard` (in `App.jsx`): approve/reject-with-note flow. Milestone 3 migrated it onto `Panel` + corrected the Reject palette. | Not live-reachable to inspect (no backend); the One-Accent/destructive-color question (Milestone 3 decisions 1/6) is directly implicated by §3.2's broadened palette. | The flow logic + the Milestone 3 `Panel` migration. | **Layer 1** (align to new system + resolve destructive-action color under §3.2). Possibly a Layer-2 refinement of the approval UX later. |
| **Onboarding / API-key** | `ApiKeyPrompt.jsx`: centered card, honest technical copy (Milestone 2's §8a), outer `fontFamily:'sans-serif'` still present. | No product identity/weight — reads as an auth gate; the sans-serif wrapper is a deliberate deferred leftover. | The honest copy voice (keep) + the closed API-key model (unchanged — not reopened). | **Layer 1** (real product identity/weight, adopt the new type system, resolve the sans-serif leftover). Scope stays identity/orientation, not the auth mechanism. |
| **Empty states** | Every surface: one centered sentence in a large void (confirmed live across Memories/Observability/Evals in the investigation). | Afterthoughts; no orientation, no action, no visual weight proportional to the screen. | `StateText`. | **Layer 1** (a real empty-state pattern/component — orientation + optional action + appropriate weight). |
| **Loading states** | Plain `StateText` "Loading…" per site (Milestone 2 improved some copy). | Minimal; no consistent treatment. | `StateText`. | **Layer 1** (a consistent lightweight loading treatment — the plan does not require a skeleton system, but should define one pattern). |
| **Error states** | Red-tinted banners + `StateText tone="error"`. | Reasonably consistent already, but flat and not integrated with the new palette. | `StateText`, the banner pattern. | **Layer 1** (align to new palette/surface). |
| **Settings / configuration** | **None as a distinct surface.** The only config is the API-key entry (onboarding) and per-page controls (date ranges, task pickers). | N/A — no settings surface exists; do not invent one. | N/A | **Out of scope** — no settings surface to redesign; note explicitly (§14). |

---

## 5. Shared Design-System Evolution — Decision

The slice avoided `theme/tokens.js` and `components/*` for isolation. The redesign must now
change them. The decision on *how*:

- **Evolve the existing token module in place, versioned in one coordinated change — do NOT
  spin up a parallel `tokens-v2`.** The current `tokens.js` is small, already the single source
  of truth, and already consumed by all seven shared components. A parallel system would create
  exactly the dual-source drift this token module was created to eliminate. Layer 1's first
  phase (§8, Phase 1) rebuilds `tokens.js` to the §3 spec (adding the UI/mono type split,
  elevation scale, surface ramp, focus/state tokens, broadened palette) and updates the seven
  shared components to consume the new tokens.
- **Migrate the seven shared component files in place** (`Button`, `Input`, `Label`,
  `NavItem` [also exports `Nav`], `Panel`, `StateText`, `StatusBadge` — plus `Input` also exports
  `TextArea`; 7 files, 9 primitives) rather than replacing them — their APIs are sound and every
  page already imports them; changing their *internals* to the new system propagates to every
  consumer at once.
- **Phase 1's propagation reaches further than just the components, via a second, stronger lever:
  direct token-value imports.** The pages don't only use the shared components — they also import
  `colors`/`radii`/`spacing` from `tokens.js` directly and apply them across many inline `style`
  objects (measured on `main`: `App.jsx` 66 direct token refs across 95 inline-style blocks;
  `ObservabilityPage.jsx` 78/116; `EvalsPage.jsx` 50/94). So changing token *values* in Phase 1
  re-colors and re-surfaces even the inline-styled page chrome, not just the components — this is
  actually Phase 1's biggest propagation lever, and the strategy (§7) depends on it as much as on
  the components. **What Phase 1 does NOT propagate, and must be honest about:** (a) the
  proportional/mono **type split** (anchored in the hardcoded `App.jsx:578` shell literal +
  `index.css`'s `--mono` — Phase-2 per-element work, per §2.1); (b) the handful of remaining
  **hardcoded color literals** in the shell (`App.jsx:578`'s `#11111b`/`#cdd6f4`, which are not
  token refs and won't pick up token changes until Phase 2 tokenizes them). So Phase 1 closes a
  large share of the gap product-wide (all token-driven color/surface/elevation), but not the
  whole gap — the type split and the last hardcoded literals resolve in Phase 2.
- **Add new shared primitives the redesign needs but doesn't yet have:** at least an
  `EmptyState` component (§4), a `Toggle`/segmented-control (the slice built one inline in
  `TimelineView`; extract/generalize it), and likely a `Card`/`Surface` wrapper encoding the new
  elevation/surface hierarchy. Extract the `DetailPanel` drawer *pattern* to shared only if/when
  a second surface needs it (YAGNI until then).
- **`DESIGN.md` and `.impeccable/design.json` are updated to describe the new system** as part of
  Phase 1 (they currently describe the old flat/mono-only system and would otherwise contradict
  the code — the impeccable design hook checks against them, so they must move together).

**None of this is done now.** This is the decision recorded for Phase 1 to execute.

---

## 6. React Flow (`@xyflow/react`) — Architectural Role

- **Stays the graph/canvas technology, scoped to genuine node-edge visualizations only.** It is
  currently isolated to Observability (the `frontend/src/observability/*` module) and must not
  be introduced elsewhere *for visual consistency* — a table or a form does not become better by
  being a graph, and React Flow is a large dependency (~+60 kB gzip).
- **One legitimate future expansion:** the **Playground live-execution view** (Layer 2). It
  visualizes the *same* agent-relationship structure as the Observability trace, so it is the
  one other surface where a graph is the *right* representation, not decoration. When Layer 2
  reaches Playground, it should **reuse the Observability graph infrastructure** (`AgentNode`,
  `TraceEdge`, `layout.js`) — adapted for live/streaming SSE data rather than a completed
  historical trace (a real difference: streaming updates + the "running" states become live, not
  stylistic). This reuse is a strong argument for *not* prematurely coupling the graph module to
  Observability specifics during Layer 1.
- **No other current or foreseen surface** (Memories, Evals, Settings, Onboarding) warrants a
  graph. If a future need arises it gets its own justification, like this one did.

---

## 7. Redesign Strategy — Consistency Before Perfection (the anti-vertical-slice-gap design)

The rollout is explicitly sequenced so the product is **never left with one dramatically-more-
polished page for an extended period:**

1. **Phase 1 (design system + shared primitives) touches no page files, yet is visible
   product-wide** the moment the shared components and token *values* change — because every page
   both consumes the shared components AND imports tokens directly into its inline styles (§5),
   so evolving the token values + components propagates a baseline of the new color/surface/
   elevation language everywhere simultaneously. This is the key structural reason to do the
   shared system *first*: it closes a large share of the gap in one coordinated move rather than
   page-by-page. **It does not close the whole gap**: the proportional/mono type split and the
   few remaining hardcoded shell literals (`App.jsx:578`) are anchored outside the tokens/
   components and resolve per-element in Phase 2 (§2.1/§5). So after Phase 1 the product is
   *materially more coherent everywhere* but each page still carries some old-system chrome
   (notably still-mono UI text) until Phase 2 finishes it — an intentional, bounded transient,
   far smaller than the vertical-slice gap and never one-page-vs-rest.
2. **Phase 2 (Layer-1 surface alignment) then finishes the job breadth-first** — shell/nav,
   Playground (cards/form only, not the graph yet), Memories, Evals, Approvals, Onboarding, the
   4 non-Timeline Observability sub-views, and the empty/loading/error patterns — bringing every
   surface fully into the new language at a bounded depth. At the end of Phase 2, **the whole
   product is visually coherent**, Observability included.
3. **Only then Phase 3+ (Layer-2 depth)** invests in the Playground execution-graph and any
   other deep workflow refinement — by which point there is no "one polished page" gap, because
   the whole product is already coherent.

Observability is the **validated reference**, not the next page to re-touch. Its 4 non-Timeline
sub-views are brought up in Phase 2 like any other surface; its Timeline is left alone.

---

## 8. Phased Roadmap & Milestones

Each phase: scope · files/surfaces · dependencies · effort · risk · verification · acceptance ·
review checkpoint. Effort is in focused-work weeks (solo, evenings/weekends basis, same as all
prior estimates); calendar in §10. **Conservative figures are the more likely outcome** (this
project's consistent history), stated per prior discipline.

### Phase 1 — Design-system foundation (Layer 1 core)
- **Scope:** rebuild `theme/tokens.js` to the §3 spec (type split, elevation scale, surface
  ramp, focus/state/broadened-palette tokens); migrate the 7 shared components to consume it;
  add `EmptyState` + `Toggle` (+ likely `Card`) primitives; update `DESIGN.md` +
  `.impeccable/design.json`; resolve the webfont/UI-face decision (§3.1).
- **Files:** `theme/tokens.js`, `components/*` (all 7 + new), `DESIGN.md`, `.impeccable/design.json`,
  possibly `index.css` (font loading). **Not `App.jsx`/pages yet.**
- **Dependencies:** the §3.1 (UI face) and §3.2 (palette breadth) design decisions approved first.
- **Effort:** 1.5–3 wks (best) / 3–5 wks (conservative).
- **Risk:** high-leverage but high-blast-radius — every page consumes these; a regression here
  regresses everywhere. Mitigated by: the components' APIs staying stable (internals-only change),
  and per-component isolated-render verification before the page phases consume them.
- **Verification:** isolated component render (every component × every state, before/after);
  live render of a component gallery; build/lint clean.
- **Acceptance:** all 7 components render correctly in the new system across their full state
  matrix; `DESIGN.md`/`design.json` describe the new system consistently; no page file changed
  yet; the impeccable hook passes against the updated design docs.
- **Checkpoint:** **Go/No-Go A** — review the new shared system in a component gallery *before*
  rolling it across pages. This is the cheapest place to course-correct the visual direction.
  **Stopping-point honesty:** A is a *partial-baseline* stop, not a fully-clean one. If the
  program halts at A after merging Phase 1, every page inherits the new token-driven colors/
  surfaces/elevation and the new components, but each page still carries residual old-system
  chrome — most visibly, UI text is still monospace (the type split is Phase 2) and the shell's
  few hardcoded literals are unchanged. That is a *within-page* new-vs-old transient, materially
  smaller and more uniform than the one-polished-page vertical-slice gap, but it is not a
  pristine stop. **The first fully-clean stopping point is Go/No-Go B** (end of Phase 2), where
  the whole product is coherent with no residual old-system chrome.

### Phase 2 — Platform-wide Layer-1 surface alignment
- **Scope:** bring every surface into the new language at bounded depth: shell/nav visual weight
  + tokenized shell; Playground cards/form; Memories; Evals; Approvals (+ destructive-color
  resolution); Onboarding identity; the 4 non-Timeline Observability sub-views; the empty/
  loading/error patterns via the new `EmptyState`/`StateText`.
- **Files:** `App.jsx` (shell/nav + `AgentCard`/`ToolCallRow`/`ApprovalCard`/`ManagerReviewCard`/
  `MemoriesPage`), `EvalsPage.jsx`, `ObservabilityPage.jsx` (the 4 non-Timeline sub-views only —
  Timeline untouched), `ApiKeyPrompt.jsx`.
- **Dependencies:** Phase 1 complete and approved (Go/No-Go A passed).
- **Effort:** 3–5 wks (best) / 6–9 wks (conservative). Dominant cost of Layer 1 — many surfaces.
- **Risk:** scope creep (the `App.jsx` single-file problem, well-documented from Milestones 2–3 —
  many features in one file); accidentally re-touching the validated Timeline; leaving a surface
  half-done. Mitigated by: per-surface tasks with explicit line-range boundaries and per-task
  diff-scope audits (exactly the discipline used in Milestones 2–3 and the slice).
- **Verification:** live CDP where reachable (shell, nav, Memories/Evals/Onboarding empty states,
  the non-Timeline Observability sub-views' empty states); isolated render for agent-dependent
  states (AgentCard populated, ApprovalCard — same backend-unreachable limitation as always);
  direct code comparison for scope confinement.
- **Acceptance:** **every** surface renders in the new language; no surface visibly "belongs to
  the old product"; Observability Timeline byte-unchanged; accessibility (focus, ARIA, reduced-
  motion) holds on each touched surface; build/lint clean.
- **Checkpoint:** **Go/No-Go B** — the whole product is now visually coherent. Decide: proceed to
  Layer-2 depth, or stop here (a coherent, shipped baseline is a legitimate stopping point).

### Phase 3 — Playground execution-graph (Layer 2, highest-value depth)
- **Scope:** the investigation's #1 structural gap — a live execution-graph view for the
  Playground, reusing the Observability graph infrastructure (`AgentNode`/`TraceEdge`/`layout.js`)
  adapted for live SSE/streaming data (nodes updating as agents run; genuine — not stylistic —
  running states; live approval state). Keep a fallback/simple view like Observability's List
  toggle.
- **Files:** `App.jsx` (Playground portion), new `frontend/src/observability/`-adjacent or a new
  `frontend/src/playground/` module reusing the graph primitives.
- **Dependencies:** Phase 2 complete (Go/No-Go B → proceed); the graph module's Layer-1
  alignment done.
- **Effort:** 2–4 wks (best) / 4–7 wks (conservative). The live/streaming difference makes this
  materially harder than the read-only Observability trace.
- **Risk:** highest-novelty in the whole program — live streaming graph updates are a genuinely
  new interaction problem, not a restyle; and it's the surface most affected by the
  backend-unreachable limitation (a live agent run can't be exercised here at all).
- **Verification:** isolated render + hand-constructed streaming-event fixtures (the same
  synthetic-data discipline the slice used, extended to simulate SSE sequences); live where
  reachable. The genuine-live-run gap is an explicit accepted limitation (compounds §11).
- **Acceptance:** the Playground visualizes execution structure live from (simulated) SSE;
  parallelism/tool-calls/status/failures read correctly; accessibility holds; fallback view works.
- **Checkpoint:** **Go/No-Go C** — reassess before any further Layer-2 work.

### Phase 4 — Remaining Layer-2 refinements + responsive/a11y hardening (optional, demand-driven)
- **Scope:** deeper refinements of any surface the earlier checkpoints flagged as worth it
  (e.g. a richer Approvals UX, Evals results viz); a final cross-platform responsive +
  accessibility hardening pass (the equivalent of the slice's Task 6, product-wide).
- **Dependencies:** Phase 3 (Go/No-Go C → proceed).
- **Effort:** 1–3 wks (best) / 3–5 wks (conservative) — scope-dependent.
- **Risk:** low (polish); main risk is over-investment past the point of diminishing returns.
- **Verification/Acceptance:** per-refinement, same standards.
- **Checkpoint:** **Program close-out review** — declare the redesign done, or scope a follow-on.

---

## 9. Dependency & Risk Map + Milestone-Level Go/No-Go Checkpoints

**Dependency chain (mostly linear by design — consistency-first forces it):**
```
[design decisions §3.1/§3.2 approved]
        └─► Phase 1 (shared system)  ──GoNoGo A──►  Phase 2 (Layer-1 all surfaces)  ──GoNoGo B──►
              Phase 3 (Playground graph, Layer 2)  ──GoNoGo C──►  Phase 4 (optional depth)  ──close-out──►
```
Little parallelism is available (and that's intentional): Phase 1 must precede all surface work
because every surface consumes the shared system; Phase 2 must precede Phase 3 to honor the
consistency-first rule. The only safe parallelism is *within* Phase 2 (independent surfaces can
be separate tasks) — but for a solo developer that's task-sequencing, not true throughput gain
(same finding as the investigation's §13).

**Top risks:**
| Risk | Severity | Mitigation |
|---|---|---|
| **Stalling mid-redesign** (a half-redesigned product is worse than either endpoint) | **Highest** | The phase structure is designed so checkpoints are stopping points, with honest gradations: **A (after Phase 1)** is a *partial-baseline* stop — pages inherit the new token-driven colors/surfaces/components but still carry residual old-system chrome (notably still-mono UI text until the Phase-2 type split), a bounded within-page transient, not a pristine stop; **B (after Phase 2)** is the **first fully-clean stop** — the *whole product* is coherent and shippable, no residual old chrome; **C and later** are purely additive on top of a coherent product. So the program can stop at B (or beyond) with no visual breakage, and even A leaves a far smaller, more uniform gap than the one-polished-page vertical-slice problem. **B being a clean, shippable stop is the single most important anti-stalling property of the plan.** |
| Phase 1 blast radius (regress the shared system → regress everywhere) | High | Component-gallery isolated-render verification before any page consumes it; Go/No-Go A gate. |
| `App.jsx` scope creep (many features, one file) | High | Per-surface tasks with explicit line-range boundaries + per-task diff-scope audits (proven discipline). |
| Accidentally re-touching the validated Observability Timeline | Medium | Timeline is explicitly out of Phase 2/3 scope; every task audits `git diff` confines to intended surfaces; the Timeline's byte-stability is an acceptance criterion. |
| Backend-unreachable verification ceiling (agent-dependent + live-run states) | Medium | Isolated-render + synthetic fixtures throughout; every such gap named as an accepted limitation (§11); never claimed as live-verified. |
| Milestone 3's paused work vs. the new system | **Low (measured, was Medium)** | Measured 2026-07-26: M3 has *zero* git-merge conflict with `main` (no textual collision), but all 5 of its files are in Phase 1/2 rewrite scope, so it is superseded-by-design not colliding. Fully mitigated by NOT resuming/merging M3 (§12); the only "cost" is discarding old-system work, which the plan does deliberately. |
| React Flow bundle cost growing if graph spreads | Low | §6's discipline — graph only where a graph is the right representation. |

**Go/No-Go checkpoints (decision content at each):**
- **A (after Phase 1):** is the new shared visual language right, before it's rolled across the
  product? Cheapest correction point.
- **B (after Phase 2):** the whole product is now coherent — continue to Layer-2 depth, or ship
  this coherent baseline and stop/reprioritize?
- **C (after Phase 3):** was the Playground graph worth it; continue to Phase 4 or close out?
- **Close-out (after Phase 4):** redesign complete or scope a follow-on.

---

## 10. Effort & Calendar Estimate (not compressed)

Consistent with the competitive-benchmark investigation's §13 methodology and figures, **kept
visible and uncompressed**, and reconciled with the fact that the Observability slice (now done)
is subtracted from the remaining work.

**Remaining redesign effort (Phases 1–4, i.e. everything except the already-shipped
Observability slice):**
- **Best case:** ~7.5 weeks focused effort (P1 1.5 + P2 3 + P3 2 + P4 1).
- **Conservative:** ~26 weeks focused effort (P1 5 + P2 9 + P3 7 + P4 5) — **treat this as the
  more likely outcome**, per this project's consistent history of work running past initial
  estimates once started.

This sits within the previously-stated program envelope the user asked to keep visible —
**~7–13+ weeks effort** headline, with the honest conservative ceiling extending toward ~26
weeks (the "13+" open-ended). **Do not plan around the low end.**

**Calendar duration** (effort × this project's established ~1.5×/~1.6× part-time ratio, solo,
evenings/weekends alongside a full-time job — the same derivation the investigation used, where
calendar ≥ effort always for a solo part-time developer):
- **Best case:** ~11 weeks (~2.5 months).
- **Conservative:** ~40+ weeks (~9+ months).

This matches the user's stated **~2.5–9 months calendar** envelope, with the conservative end
being the honest planning number. The phase checkpoints (§9) exist precisely so this long
calendar can be entered incrementally, with real stop points, rather than as one all-or-nothing
commitment.

---

## 11. Preserved Accepted Risk (tracked, not a blocker)

**The Observability Timeline has not yet been validated against a genuine backend-generated
trace** — every verification used synthetic hand-constructed traces against the real integrated
UI, because no backend is reachable in this environment and no real trace exists in the
repo/history (exhaustively confirmed). **Re-verified 2026-07-26 at `main` `829546e`: unchanged —
no real trace was ever captured or committed (no such capture task was ever run), no
`spanId`/`parentSpanId`/`startedAt` payload exists anywhere in the tree or git history, and the
backend remains keyless/unreachable (`appsettings.json` `ApiKey` empty, no `.env`, no
`ANTHROPIC_*` env var). The accepted synthetic-data limitation stands unchanged.** This remains
an explicit, tracked verification item: it must be tested against a real `/tasks/{id}/timeline`
response when backend access becomes available. **Phase 3 compounds this** — the Playground
live-execution graph additionally cannot be exercised against a real live agent run here, so it
inherits the same accepted limitation, extended to streaming data. Neither blocks the redesign;
both are carried forward explicitly and must be closed when backend access exists.

**Palette-density validation gap (new, feeds §3.2's palette-breadth decision — do not lock the
palette yet):** the existing synthetic fixture (`observability/sampleGraphData.js`) *does* cover
the palette's *dimensions* — all 6 agent identities (Orchestrator/Research/Writer/Code/Data/
Browser), all 5 statuses, agent + tool-call node types, completed/running/failed edges, and a
parallel fan-out — but it is a **small, sparse** graph (~10–11 nodes across its two sections),
so it demonstrates the color vocabulary but **cannot validate how the palette behaves under
realistic density** (dozens of concurrent agents/tool-calls, deep/wide trees). Per the explicit
instruction not to expand the palette just because the current mockup looks sparse, the
palette-breadth decision (§3.2) must be validated against a **denser** stress fixture (a small,
backend-independent piece of R1's design phase — build a dense synthetic graph and view the
palette at scale), and against a real trace if/when one exists. Until then, the palette's
*number of identity/accent colors* stays provisional, not locked.

---

## 12. Milestone 3 Reconciliation

Milestone 3 (paused at `b24d5e6`, 6 of 7 tasks complete — cross-page consistency: DESIGN.md
exception docs, Panel accent-alpha decision, `ApprovalCard` `Panel` migration + Reject-palette
correction, `MemoriesPage` Delete-palette correction, header tokenization, EvalsPage copy) was a
consistency pass *within the old visual system*. Under a full redesign that replaces that system:

**Measured reconciliation cost (re-verified against `main` at `829546e`, 2026-07-26 — not an
estimate):** M3 changes exactly 5 files — `DESIGN.md`, `App.jsx`, `EvalsPage.jsx`,
`components/Panel.jsx`, `theme/tokens.js` (+46/−54 lines total). Two concrete, measured facts:
1. **M3 has ZERO git-merge conflict with current `main`** (`git merge-tree` from its base
   `05d4917` against `829546e` produces no conflict markers). This is because M3's 5 files have
   *zero file overlap* with the Observability slice already merged into `main` (the slice touched
   only `ObservabilityPage.jsx` + `observability/*`). So the reconciliation cost is **not** a
   merge-conflict-resolution cost — that cost is measured at zero.
2. **All 5 of 5 M3 files fall inside the redesign's Phase 1/2 rewrite scope** (`tokens.js`,
   `Panel.jsx`, `DESIGN.md` → Phase 1; `App.jsx`, `EvalsPage.jsx` → Phase 2). So the real cost is
   pure **opportunity/wasted-effort**: merging M3 now would land old-system changes that Phase
   1/2 immediately overwrites. M3 is *cleanly mergeable but semantically superseded by design.*

- **Do NOT resume Milestone 3 as-is or merge its branch.** Its `DESIGN.md`/Panel-alpha/One-Accent
  decisions are about the old system and are superseded by §3's new system.
- **Salvage its still-valid, direction-independent pieces into the redesign phases:** the
  `EvalsPage` empty-suite copy fix and the header tokenization are useful regardless of visual
  system and should be re-created (not cherry-picked, to avoid old-system coupling) inside Phase
  2's Evals and shell tasks. The `ApprovalCard`/`MemoriesPage` destructive-action color question
  is *reframed* under §3.2's broadened palette and resolved fresh in Phase 2's Approvals task.
- **Formally close the Milestone 3 worktree** once Phase 1's new design system is approved (Go/
  No-Go A), since by then its premise is definitively superseded. Until then it stays paused,
  untouched — a no-cost parked branch.

---

## 13. Recommended First Implementation Milestone

**Milestone R1 — Design-System Foundation (= Phase 1 above).** Rebuild the shared token system
and the seven shared primitives to the new §3 language, add the `EmptyState`/`Toggle`(/`Card`)
primitives, and update `DESIGN.md`/`.impeccable/design.json` — all verified in an isolated
component gallery, with **no page files touched**, gated by **Go/No-Go A**.

**Why this first, specifically:**
- It closes a **large share of the consistency gap in one coordinated move** — because every page
  both consumes the shared components and imports token *values* directly into its inline styles
  (§5), evolving the tokens + components re-colors and re-surfaces every page's baseline at once,
  without page-by-page work. (It does *not* close the whole gap — the type split and the last
  hardcoded shell literals are Phase-2 per-element work, §2.1/§5 — but it is the most efficient
  possible *first* step toward coherence.)
- It is the **necessary foundation** for every subsequent phase (all surface work consumes it).
- It is the **cheapest place to validate the new direction product-wide** (a component gallery,
  before committing to rolling it across pages) — de-risking the whole program at its start.
- It **keeps the vertical-slice mistake from recurring**: it does not deepen any single page; it
  raises the floor everywhere at once, uniformly, rather than lifting one surface far above the rest.

R1 would follow the exact established discipline: isolated worktree, fresh implementer + reviewer
subagents per task, isolated-component-render verification (backend-independent — this is pure
component/token work, fully verifiable), direct code comparison for scope, no unverified claims,
explicit accepted limitations, review checkpoints. A full R1 task breakdown (à la the slice's §7)
would be produced *after* R1 is approved and the §3.1/§3.2 design decisions are made — not now.

---

## 14. Explicitly NOT Being Redesigned Yet (this plan authorizes none of it)

- **Any source code** — this is planning only.
- **The Observability Timeline** — done and validated; the reference, not re-touched.
- **The four other Observability sub-views** — aligned in Phase 2, not before.
- **The Playground execution graph** — Phase 3 (Layer 2), not Layer 1; not before the whole
  product is coherent.
- **A settings/configuration surface** — none exists; not being invented.
- **The authentication/API-key *model*** — closed to API-key-only per the backlog's locked §0;
  only the onboarding surface's *identity/visual* layer is in scope, not the auth mechanism.
- **Backend/API changes** — none in this redesign; the backend-trace and live-run verification
  gaps (§11) are frontend-verification limitations, not backend work items here.
- **Milestone 3's branch** — not resumed/merged; salvaged-into-phases and then closed (§12).
- **React Flow anywhere beyond Observability (now) / Playground (Phase 3)** — §6's discipline.
- **Phone-width (<768px) deep optimization of dense views** — deferred tier, per surface, as
  established.

---

*Planning only. No source/application files were modified. No implementation worktree was
created. No dependency installed. Nothing committed or pushed. `main`/`origin/main` at `829546e`.
Milestone 3 untouched at `b24d5e6`. The first implementation milestone (R1, §13) is recommended
but NOT started. Awaiting explicit approval — including the §3.1 (UI typeface) and §3.2 (palette
breadth) design decisions — before any implementation begins.*
