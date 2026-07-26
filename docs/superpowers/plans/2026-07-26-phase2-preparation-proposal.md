# Phase 2 — Preparation Proposal (PROPOSAL — awaiting approval; NOT implementation)

> **Status: proposal only.** No Phase-2 code, no new worktree, no push. R1 is merged to local main
> (`ac803a3`, not pushed). This document is the required Phase-2 preparation deliverable; it stops at
> a proposal and awaits explicit approval before any implementation. The design research below used
> the installed `ui-ux-pro-max` skill (v2.11.0) as an additional capability, not as an override of
> OrchestAI's identity.

## Framing (grounded in the actual current state, not the report)

The current product has **no coherent application shell or content-width system** — verified in code:
- **Shell/root** (`App.jsx:578`): full-bleed, hardcoded mono, a single top header bar with inline nav.
- **Playground**: full-width 2-column grid (`380px 1fr`), no max-width.
- **Evals** (`EvalsPage.jsx:490`): left-aligned `maxWidth: 1100` (no centering).
- **Observability**: mixed — `maxWidth: 480` pickers over full-width charts/graph.
- **Memories**: a raw table.
- The **narrow centered container** the review noticed is the **gallery's own** `maxWidth: 720/760`
  choice (a verification surface) — **not** the product. So the product does *not* today impose one
  constrained centered layout; it imposes *no* system at all. Phase 2's job is to install a
  deliberate one, per-surface — not to inherit or universalize any single existing width.

`ui-ux-pro-max` classifies OrchestAI as **Developer Tool / IDE** → Dark Mode + Minimalism, dashboard
style **Real-Time Monitor + Terminal**, Blue-focus accent, Bento-grid composition. This *is* R1's
identity; Phase 2 sharpens it, it does not restyle toward generic SaaS.

---

## 1. Recommended global application shell

A **persistent left sidebar rail + a slim top context bar + a per-surface content region.**

- **Left sidebar (primary nav):** icon+label items (Playground / Memories / Observability / Evals,
  and future surfaces), collapsible to an icon rail. This is the dev-platform convention (VS Code,
  Grafana, Linear, Vercel) and scales as surfaces grow, where the current top-tab row does not.
- **Top context bar:** current-surface title, and surface-scoped context that's currently crammed
  into the header (task id, running status, total cost, "Change key"). Sticky, does not obscure content.
- **Content region:** owns its own width/density per §2 and §7.

**Tradeoff (stated, not hidden):** a top-nav is simpler and is what exists today; a left sidebar costs
one new shell primitive + a responsive collapse, but is the correct long-term shell for a growing
multi-surface developer platform and matches the dev-tool convention the skill flags. **Recommend the
sidebar**, built as a new shared `AppShell` primitive so every surface inherits it identically.

## 2. Recommended content-width strategy (per-surface, not universal)

Introduce **content-width tokens** and assign each surface deliberately (grounded in the skill's
Container-Width guideline: text 65–75ch; data-dense may go wider):

| Width token | Value | For |
|---|---|---|
| `readable` | ~640–720px (≈65–75ch) | prose, onboarding copy, forms, empty states, single-column reading |
| `standard` | ~1100–1280px | mixed content, settings, forms-with-context |
| `wide` | fluid to ~1600px cap, then centered | data-dense tables, dashboards |
| `full` | 100% of content region (minus shell) | graph canvases, timelines, monitors |

Rule: **text is constrained for legibility; data expands to use the viewport; nothing is centered in a
void just because it looks tidy.** The ultra-wide cap (~1600px) prevents unreadable line lengths on
32" monitors without wasting a 15" laptop.

## 3. Sidebar / navigation strategy

- Migrate the four `NavItem`s from the top header into the sidebar; keep `NavItem`'s API, add an
  optional icon slot and a vertical/rail layout mode (additive — see §10).
- OrchestAI is a **flat 4-surface app** → **no breadcrumbs** (the skill flags breadcrumbs only for 3+
  levels of depth). The sidebar + top-bar surface title is sufficient wayfinding.
- Sidebar collapses to an icon rail at the `tablet` breakpoint and to a drawer/sheet at `mobile`.
- Preserve keyboard operability and `aria-current` (already in `NavItem`).

## 4. Typography-role migration plan (this is the deferred Phase-1 → Phase-2 UI/mono split)

The mono default is anchored at `App.jsx:578`'s shell literal + `index.css`'s `--mono`. Plan:

1. **Invert the shell default:** set the app-shell font-family to the **UI face** (IBM Plex Sans), so
   chrome/prose/labels/nav/headings inherit the proportional face by default.
2. **Explicitly re-assign data content to the mono face**, per element: IDs, timestamps, durations,
   cost figures, code/JSON, span IDs, and numeric table cells → `dataFontFamily`. This is the
   per-element work R1 deliberately deferred.
3. **Resolve `typography.body`'s face** (currently data/mono, a conservative R1 call): recommend
   splitting consumption — UI prose uses a UI-face body role, genuine data uses `dataBody`. Decide
   this explicitly at P2-M2; it is what makes EmptyState/StateText prose read as UI (the R1 M3
   carry-forward).
4. Migrate **shell first**, then surface-by-surface, verifying data text stays mono at each step
   (the exact inverse discipline R1 used to keep data safe).

## 5. Global spacing / layout rules

- Standardize on the existing `spacing` scale (xs→xxxl, 8px-based); one page-padding rule
  (~24px desktop / 16px mobile), consistent section rhythm.
- Compose dashboards as **Bento-style card grids** (skill rec for dev-tool dashboards) using the R1
  `Card` surface/elevation tiers — not endless identical card rows.
- Data-dense surfaces: tighter vertical rhythm; text surfaces: more generous rhythm. Density is a
  per-surface decision (§7), not a global constant.

## 6. Responsive breakpoint strategy

Formalize breakpoint **tokens** (today it's a single ad-hoc `isNarrow` boolean in `App.jsx`):

| Token | Range | Shell behavior |
|---|---|---|
| `mobile` | < 640px | sidebar → drawer; single column; stacked |
| `tablet` | 640–1024px | sidebar → icon rail; content fluid |
| `desktop` | 1024–1440px | full sidebar + content |
| `wide` | > 1440px | content honors width caps (§2); no ultra-wide line lengths |

Mobile-first, no horizontal scroll, no fixed-px container widths (skill Layout priority). Wide
content (tables/graphs) scrolls inside its own `overflow-x:auto` container, never the page body.

## 7. Per-surface layout density (they must NOT share one width)

- **Playground** — *mixed, workflow-dense.* Keep a two-pane shape: a `readable`/`standard` input rail
  + a `wide`/`full` fluid agent-feed/run pane. The primary create→run→watch workflow.
- **Observability** — *maximum data density; the benchmark.* `full` width for the timeline/graph
  canvas and monitor views; `wide` for the tables. **Bring it into the shell but leave its Timeline
  graph patterns alone** — reuse its information-architecture quality as the bar, don't clone its
  graph-specific components into unrelated pages.
- **Evals** — *data-dense, comparative.* `wide` results/regression tables (drill-down + comparative,
  per the Analytics-Dashboard rec); `standard` for the run/config forms.
- **Memories** — *scannable list/table.* `wide` table with good scan affordances; `readable` for any
  empty/detail prose.
- **Onboarding** — *text-first, greenfield.* `readable` centered column. Today there is **no real
  onboarding** (only `ApiKeyPrompt`); Phase 2 designs a genuine first-run/activation flow + the
  empty-state-to-first-success path (the `EmptyState` primitive is built for exactly this).

## 8. Which parts of R1 remain UNCHANGED

- All design **tokens**: colors, `surface`, `elevation`, `focusRing`, `interactiveState`, `semantic`,
  `radii`, `spacing`, and the `typography`/`typeScale` **role definitions** (Phase 2 assigns elements
  to roles; it does not redefine the roles).
- The **palette decision** (6 identity + 5 status + 1 accent; semantic = status aliases).
- The **self-hosted fonts** and their loading.
- The **named rules** (Two-Palette, One-Accent, Two-Face, Disciplined-Elevation) and `DESIGN.md` /
  `.impeccable/design.json` as the source of truth.
- The **Observability Timeline** slice (benchmark; untouched except adopting the shell).

## 9. Shared primitives that need NO changes

`Label`, `StatusBadge`, `StateText`, `Card`, `EmptyState`, `Toggle` — complete as-is; Phase 2 *consumes*
them (esp. EmptyState/Toggle/Card, which no page uses yet). `Panel` is fine functionally (its
`accentStatus` border-left is a documented, deliberate exception — reconsider only as a design choice,
not a bug).

## 10. Primitive API gaps discovered during R1 (additive, non-breaking)

- **`Button` is `width: 100%`-hardcoded** — no inline/auto-width, no size variants, no icon slot.
  A real shell/toolbar needs inline buttons. Add `fullWidth?`, `size?`, `icon?` (additive; default
  preserves current behavior).
- **No shared `Tabs`/`SubNav`** — `EvalsPage` and `ObservabilityPage` each hand-roll their own SubNav
  from `NavItem`. Extract a shared segmented sub-navigation (distinct from the top-level `Toggle`).
- **No shell primitives** — `AppShell`, `Sidebar`/`NavRail`, `PageHeader`/`Toolbar`, `PageContainer`
  (width-token-aware) don't exist. These are the core Phase-2 additions.
- **No `Table`/`DataGrid`** — Memories/Evals/Observability each build tables inline; a token-driven
  data-table primitive would unify the data-dense surfaces.
- **No `Modal`/`Drawer`/`Popover`** — the `overlay` surface + `elevation.overlay` tokens exist but no
  component consumes them yet.
- **`NavItem`** needs an optional icon slot + vertical/rail mode for the sidebar (§3).
- `Toggle` requires a caller-supplied `aria-label` (documented; keep — just enforce in usage).

## 11. How `ui-ux-pro-max` influenced this proposal

- **Confirmed identity, prevented trend-chasing:** Developer-Tool/IDE → Dark + Minimal + Terminal/
  Monitor + Blue focus + Bento. This is R1's identity; it steered the proposal *toward* sharpening the
  instrument-panel aesthetic and *away* from a generic SaaS restyle.
- **Content-width strategy (§2)** is grounded in the skill's Container-Width guideline (text 65–75ch;
  data may go wider) rather than taste.
- **Navigation (§3):** the skill's "breadcrumbs only for 3+ depth" ruled them out for this flat app.
- **Responsive (§6):** mobile-first breakpoints, no horizontal scroll, avoid fixed-px widths.
- **Composition (§5):** Bento card grids for the dashboard surfaces.
- **Not adopted:** nothing trendy was imported for its own sake; no pattern was added without a
  product-density justification, per the instruction.

## 12. Proposed Phase 2 milestone order

**Layer 1 — breadth-first visual alignment (do first, entire product):**
- **P2-M1 — Application Shell + Content-Width + Breakpoint System.** New `AppShell`/`Sidebar`/
  `PageHeader`/`PageContainer` primitives + width & breakpoint tokens + the additive primitive-API
  gaps (§10) needed by the shell. Every surface mounts inside the new shell. Backend-independent.
- **P2-M2 — Type-Role Migration + Breadth Alignment.** The UI/mono per-element split product-wide
  (§4), plus applying surfaces/spacing/cards/status-semantics/empty-states/toggles across all pages at
  **bounded depth** (no deep redesign yet). Resolve `typography.body`'s face here. End state: the whole
  product speaks the new visual language coherently.

**Layer 2 — deep page redesign (only after Layer 1; per surface):**
- **P2-M3 — Playground** (primary create→run→watch workflow).
- **P2-M4 — Evals + Memories** (data-dense, comparative; shared `Table` primitive).
- **P2-M5 — Onboarding / first-run + activation** (greenfield; `readable` flow + empty-state→first-success).
- **Observability** stays the benchmark: adopts the shell in M1/M2, no deep redesign of its Timeline.

Each milestone follows the R1 discipline: subagent-driven, per-task review + whole-branch review,
live verification, honest claim classification, an explicit checkpoint before merge, no push without
authorization.

---

## Explicitly preserved from R1 (not failures — do not "fix" in a way that regresses them)
- Residual JetBrains Mono in UI/prose (P2-M2 resolves it via the deliberate per-element split).
- The two selected-state treatments (NavItem solid fill vs Toggle traceBlue-alpha) — reconcile toward
  one canonical selected treatment in P2-M1/M2.

## The goal (restated)
A coherent, modern OrchestAI product where **every surface feels like one product**, while **each
surface is optimized for its own information density and workflow** — not "make every page look like
Observability."

*Proposal ends here. No implementation, no worktree, no push. Awaiting explicit approval and any
direction on shell choice, width caps, milestone order, or scope before Phase 2 begins.*
