# P2-M1 — Application Shell + Width System + Responsive Foundation — Implementation Plan

> **Status: PLAN ONLY. No implementation, no worktree, no source changes, no push.** Local `main` is
> the source of truth at `0500549` (R1 merged at `ac803a3` + the Phase-2 prep proposal). `origin/main`
> unchanged at `829546e`. This plan operationalizes **P2-M1** from the approved
> `2026-07-26-phase2-preparation-proposal.md`, split into **P2-M1A (Shell Prototype)** and
> **P2-M1B (Product Shell Migration)** with a genuine Go/No-Go between them. It reuses R1's existing
> design tokens and primitive APIs; the only additive token gaps are width + breakpoint tokens (§10).

---

## 1. Objective
Establish the platform-wide structural foundation — a persistent, responsive application shell and a
per-density content-width system — **before** any deep page redesign, so that (a) the shell is
validated in isolation before it is applied product-wide, and (b) the whole product becomes
structurally coherent breadth-first, avoiding another vertical-slice inconsistency.

## 2. Current-state findings (verified in the repo, not assumed)
- **Shell/root** `App.jsx:578`: full-bleed root `<div>` with a hardcoded mono `fontFamily`, `#11111b`
  bg, one top header bar (`App.jsx:581–629`) containing title + inline `<Nav>` + context cluster
  (task id / status / cost / "Change key").
- **Routing** `hooks/useRouting.js`: native `history.pushState`/`popstate`, `{ view, navigate }`, flat
  4 views (`playground|memories|observability|evals`). No routing library. Pages mount via conditional
  `view === …` (`App.jsx:631–637`).
- **Responsive** is a single ad-hoc boolean: `const isNarrow = width <= 768` (`App.jsx:329`) where
  `width = useViewportWidth()` (`hooks/useViewportWidth.js` already exists and is reusable).
- **No width or breakpoint tokens** exist in `theme/tokens.js` (grep-confirmed). Per-page widths are
  inconsistent literals: Playground full-width `380px 1fr` grid (`App.jsx:638–642`); Evals left-aligned
  `maxWidth: 1100` (`EvalsPage.jsx:490`); Observability mixes `maxWidth: 480` pickers with full-width
  charts; Memories is a raw `width:100%` table (`App.jsx:275`).
- **Primitives (10)** in `components/`: Button, Input(+TextArea), Label, NavItem(+Nav), Panel,
  StateText, StatusBadge, Card, EmptyState, Toggle. Relevant facts: `Button` hardcodes
  `width: '100%'` (`Button.jsx:90`); `Nav` is horizontal `display:flex; gap:spacing.xs`
  (`NavItem.jsx:68`); `NavItem` padding `5px 12px` → ≈24px tall (below the 44px touch minimum).
- **R1 tokens** available and to be reused: `colors`, `surface`, `elevation`, `focusRing`,
  `interactiveState`, `semantic`, `radii`, `spacing`, `typography`/`typeScale`, `uiFontFamily`,
  `dataFontFamily`. Focus is wired via the `.focus-ring` class in `index.css`.

## 3. Desired end state (of all of P2-M1)
Every product surface renders inside one shared `AppShell`: a collapsible persistent left sidebar
(icon+label ↔ icon-only, tooltips when collapsed), a slim sticky top context bar, and a content region
that selects a width strategy by density. Responsive behavior is driven by named breakpoint tokens,
mobile-first, with the sidebar becoming a drawer/overlay on mobile. Functionality and data flows are
unchanged; no page is deeply redesigned yet.

---

# STAGE P2-M1A — Shell Prototype

## 4. P2-M1A scope
1. **Width + breakpoint tokens** (additive to `tokens.js`): `contentWidth` (`readable`/`standard`/
   `wide`/`full`) and `breakpoints` (`mobile`/`tablet`/`desktop`/`wide`). Plus a `useBreakpoint` hook
   built on the existing `useViewportWidth`.
2. **New shell primitives** (new files; proposed dir `components/shell/` to keep the 10 flat primitives
   uncluttered): `AppShell`, `Sidebar` (+`SidebarItem` or reuse `NavItem` in a rail mode), `TopBar`,
   `PageContainer` (width-token-aware wrapper).
3. **`NavItem` additive API**: optional `icon` slot + a `collapsed`/rail mode (icon-only + accessible
   tooltip) + a vertical `Nav` orientation. API stays backward-compatible (existing horizontal use
   unaffected).
4. **A Shell Demo review artifact** — a **separate Vite HTML entry** (`shell-demo.html` +
   `src/shell-demo/*`), mounting the REAL `AppShell` with representative-but-synthetic content panes
   for each surface *type* (data-dense table, wide dashboard, readable prose/form, empty state, long
   scrolling content). This demonstrates the shell **in context**, not just isolated components — it is
   the visual-review artifact (§12/§28). Backend-independent; touches no product page.
5. Sidebar behaviors (§8), top bar (§9), width system (§10), responsive/mobile drawer (§11, §15).

## 5. P2-M1A out-of-scope
- No product page touched (`App.jsx`, `MemoriesPage`, `ObservabilityPage`, `EvalsPage`,
  `ApiKeyPrompt` unchanged in M1A).
- No type-role (UI/mono) migration — that is **P2-M2**.
- No `Button`/`Tabs`/`Table`/`Modal` work (see §16/§17).
- No deep visual redesign of any surface; the demo content is structural placeholder, not final design.
- No backend/API/data-model/dependency changes.

## 6. P2-M1A files likely to change (all NEW/additive)
- NEW `frontend/src/theme/tokens.js` **additive exports only**: `contentWidth`, `breakpoints`
  (no existing export modified).
- NEW `frontend/src/hooks/useBreakpoint.js` (wraps `useViewportWidth`).
- NEW `frontend/src/components/shell/AppShell.jsx`, `Sidebar.jsx`, `TopBar.jsx`, `PageContainer.jsx`.
- EDIT `frontend/src/components/NavItem.jsx` — **additive** props only (`icon`, `collapsed`,
  orientation); existing behavior/tests unchanged. (This is the one existing-primitive edit in M1A;
  justified because the sidebar is a nav surface and duplicating NavItem would fragment the system.)
- NEW `frontend/shell-demo.html`, `frontend/src/shell-demo/shellDemoMain.jsx`,
  `frontend/src/shell-demo/ShellDemo.jsx` (+ synthetic content stubs) — verification-only, separate
  entry, zero product blast-radius (mirrors the R1 gallery mechanism).
- POSSIBLE additive `frontend/src/index.css` rule for a tooltip or drawer scrim if not expressible
  inline (documented if used).
- `DESIGN.md` / `.impeccable/design.json`: add a "Layout & Shell" section documenting the width +
  breakpoint tokens and shell rules (kept consistent with tokens, as R1 did).

## 7. Proposed AppShell architecture
```
<AppShell>                      // owns the grid + breakpoint state (useBreakpoint)
  ├─ <Sidebar collapsed active navigate/>   // desktop: fixed rail; mobile: drawer/overlay
  ├─ <TopBar>{contextSlot}</TopBar>         // sticky; surface title + context cluster
  └─ <main>                                 // the content region
       <PageContainer width="wide|standard|readable|full"> {page} </PageContainer>
```
- **Desktop/tablet:** CSS grid `[sidebar] [main]`; sidebar width toggles between expanded
  (~240px) and collapsed (~56px icon rail). Content region fills the remaining width; `PageContainer`
  applies the per-surface width strategy inside it.
- **Mobile:** single column; sidebar unmounts from the grid and is presented as a drawer/overlay
  toggled from a TopBar menu button (§15).
- `AppShell` is presentation-only and stateless about *which* page renders — it takes `active` + a
  `navigate` callback (wrapping the existing `useRouting`) and a content child, so App.jsx keeps owning
  routing. Sidebar collapsed-state persists (localStorage or state; decision in §J).

## 8. Sidebar behavior
- **Expanded:** icon + label per view; clear **active** state (reuse R1 `NavItem` active fill +
  `aria-current="page"`); hover fill (R1 `interactiveState.hoverAlphaSuffix`); visible focus via
  `.focus-ring`.
- **Collapsed:** icon-only rail; each item shows an **accessible tooltip** with the label (title +
  `aria-label`, and a visible tooltip on hover/focus, not title-attribute-only) so the label is
  reachable by keyboard and pointer.
- **Collapse control:** a toggle button; state persists across reloads.
- **Touch:** on touch/mobile, nav items meet the **44×44px** minimum (skill Touch-Target, High) with
  ≥8px spacing — the current `NavItem` 24px height is raised via the rail/drawer mode, not globally.
- **Keyboard:** Tab reaches the collapse toggle + each item in visual order; Enter/Space activates;
  `aria-current` marks active; no keyboard trap.

## 9. Top context bar behavior
- Sticky top; **compensate its height** so it never obscures content (skill Sticky-Nav). Contains the
  current-surface title (sequential heading level, skill Heading-Hierarchy) and a context slot for
  surface-scoped info/controls (today: task id / status / cost / "Change key"; future: run controls).
- On mobile: hosts the drawer menu button; collapses non-essential context gracefully.

## 10. Width-system architecture (additive tokens — the concrete, evidence-based gap)
New `tokens.js` exports (values are the plan's proposal; exact numbers a §J decision):
- `breakpoints = { mobile: 640, tablet: 1024, desktop: 1440, wide: 1920 }` (px thresholds).
- `contentWidth = { readable: '68ch', standard: '1440px', wide: '1600px', full: '100%' }` where
  `standard`/`wide` are **soft caps** (max-width; content fills up to them then centers within the
  content region), and `full` fills the region. Data-dense surfaces use `wide`/`full`; text uses
  `readable`; the soft ~1600px cap prevents unreadable line lengths on ≥1920px without stranding data.
- `PageContainer` consumes these: `width="readable|standard|wide|full"` → applies `max-width` + inline
  padding from `spacing`. Pages request a width by intent; no page hardcodes a literal.
- Grounded in the skill's Container-Width guideline (text 65–75ch; data may go wider). Reuses R1
  `spacing` for gutters — no spacing redesign.

## 11. Responsive breakpoint strategy
- **Mobile-first** (skill Mobile-First, default styles + `min-width` breakpoints, not desktop-first
  `max-width`). A `useBreakpoint()` hook returns the current class from `breakpoints` (replacing the
  ad-hoc `isNarrow`, which M1A leaves in place in App.jsx until M1B).
- Behavior per class: `mobile` → drawer nav, single column, `full`/`readable` content;
  `tablet` → collapsed icon rail, fluid content; `desktop` → expanded sidebar + content honoring
  width tokens; `wide (≥1920)` → content honors soft caps, sidebar unchanged, no ultra-wide dead-zone
  on data surfaces (they may use `wide`/`full`).
- No horizontal page scroll; wide content (tables/graphs) scrolls inside its own `overflow-x:auto`
  container.

## 12. Viewport validation matrix (P2-M1A visual review — mandatory checkpoint)
Validated live (CDP) on the **shell demo** at each viewport. For each: what to inspect.

| Viewport | Shell expectation | Inspect |
|---|---|---|
| **390px** | mobile drawer nav; single column; no h-scroll; 44px touch targets | drawer open/close + focus trap + Escape; content readable; no squeezed desktop UI |
| **768px** | transition point; icon rail or drawer | sidebar mode correct; content reflow; top bar context collapse |
| **1024px** | collapsed→expandable sidebar; content fluid | expanded + collapsed states; active/hover/focus; data table fits |
| **1440px** | strong primary desktop; expanded sidebar | width tokens (readable vs wide) visibly distinct; spacing rhythm |
| **1920px** | naturally spacious, no excess dead space | soft caps engage; data surfaces use space; not a tiny centered site |
| **2560px** | intentional, not a small site in a void | data-dense demo uses horizontal space; readable content capped; shell feels deliberate |

Each viewport captured expanded **and** collapsed sidebar; plus the mobile drawer at 390/768.

## 13. Accessibility requirements (P2-M1A)
- Visible keyboard focus on every interactive shell element (reuse `.focus-ring`; skill Focus-States).
- Sidebar/drawer fully keyboard operable; tab order matches visual order; no traps (skill Keyboard-Nav).
- Collapsed-rail items expose their label to AT (accessible name via `aria-label` + visible tooltip on
  focus, not `title` alone).
- Drawer is a dialog: focus moves in on open, trap while open, Escape closes, focus returns; scrim +
  clear close affordance (skill Modal-Escape).
- Touch targets ≥44×44px with ≥8px spacing on touch (skill Touch-Target/Spacing).
- Sequential heading hierarchy in the shell + demo (skill Heading-Hierarchy).
- Sidebar landmark `role="navigation"`/`<nav aria-label>`; main `role="main"`.

## 14. Keyboard navigation behavior
- Tab order: skip-to-content link → sidebar collapse toggle → nav items (visual order) → top bar
  controls → main content. Enter/Space activate nav; `aria-current` on active.
- Drawer: opening moves focus to the drawer; Tab cycles within; Escape closes and restores focus to
  the menu button.

## 15. Mobile navigation behavior
- Approved direction: sidebar → **drawer/overlay** on mobile (menu button in top bar), NOT the desktop
  sidebar squeezed in. Drawer = accessible dialog (§13). No horizontal scroll at 390px.
- **Documented conflict / decision point (§J-1):** the skill's `app-interface` data recommends a
  **bottom tab bar for ≤5 primary items** on mobile; OrchestAI has exactly 4. A bottom tab bar is more
  thumb-reachable for a flat 4-view app. Presented as a decision, not silently substituted — the
  approved drawer stands unless you choose the bottom-tab alternative.

## 16. Primitive API changes REQUIRED for P2-M1A
- `NavItem` (additive): `icon`, `collapsed` (icon-only + tooltip), vertical `Nav` orientation.
  Backward-compatible; existing horizontal top-nav callers unaffected.
- No other existing primitive is modified in M1A. New shell primitives are additive.

## 17. Primitive API changes explicitly DEFERRED
- **`Button` inline/`size`/`icon` + `fullWidth` API → P2-M1B** (needed when migrating real toolbars,
  not by the prototype).
- **Shared `Tabs`/`SubNav`** (Evals/Observability hand-roll their own) → **P2-M2** (breadth alignment),
  unless M1B migration proves it blocking (then escalate, don't silently pull in).
- **`Table`/`DataGrid`** → Layer-2 page redesigns (Evals/Memories/Observability).
- **`Modal`/`Drawer` as a general primitive** → the mobile-nav drawer in M1A is shell-internal; a
  reusable `Modal`/`Drawer` primitive is deferred to Layer-2 unless a surface needs it sooner.
- Rationale: keep M1 tight; build abstractions only when a concrete consumer exists (no speculative
  primitives).

---

# STAGE P2-M1B — Product Shell Migration (only after P2-M1A Go)

## 18. P2-M1B scope
- Mount **all four** product surfaces inside `AppShell` (breadth-first, together — not one at a time):
  move the top header/nav out of `App.jsx` into the shell; wrap Playground/Memories/Observability/Evals
  in `PageContainer` with their approved width (§20/§7-proposal).
- Replace the ad-hoc `isNarrow` with `useBreakpoint`.
- Apply the width strategy per surface; remove per-page inconsistent shell/container literals.
- Add the deferred **`Button`** inline/size/icon API and adopt it where the shell/top-bar/toolbars need
  inline buttons (e.g. "Change key").
- Preserve all page functionality, routing, data flows, and the Observability Timeline internals.

## 19. P2-M1B out-of-scope
- **No deep page redesign** — this is structural/width alignment only; content, components inside pages,
  and interactions stay as-is (aside from being wrapped/re-parented and re-widthed).
- No type-role (UI/mono) migration (P2-M2). No new features. No backend/API/data changes.
- No redesign of Observability's Timeline graph; it is wrapped in the shell, internals untouched.
- Milestone 3 not resumed.

## 20. Page migration strategy (breadth-first, consistency-before-perfection)
- Migrate **all four surfaces in the same stage** so the product is uniformly on the new shell at the
  end of M1B — explicitly avoiding a one-page-migrated-rest-inconsistent slice.
- Per surface: (1) remove its bespoke outer container/header coupling; (2) render it inside
  `AppShell`→`PageContainer` at its density width — Playground `full`/workspace, Observability
  `full`, Evals `wide`, Memories `wide`; (3) verify functionality parity (routing, forms, fetches,
  tables, the Timeline graph) unchanged; (4) verify no visual regression beyond the intended shell/width
  change.
- App.jsx becomes: shell + routing glue; page components stay responsible for their own internals.

## 21. Protected surfaces / regression boundaries
- **Never in P2-M1 (A or B):** backend, API contracts, data models, `hooks/useRouting` behavior
  (navigation must stay identical), the Observability Timeline graph internals
  (`observability/*` components), Milestone-3 work, product features.
- **P2-M1A:** all product pages byte-unchanged (shell built in isolation + demo entry).
- **P2-M1B:** `App.jsx` + the four pages ARE edited (structural re-parent + width), but only for shell
  mounting/width — functionality preserved, no deep redesign. `ApiKeyPrompt` gate behavior preserved.
- Regression guard each stage: full diff-scope audit + functional live check of every route.

## 22. Dependency map
- **No new npm dependencies.** Shell is plain React + CSS grid/flex + R1 tokens + existing
  `useViewportWidth`/`useRouting`. (If a focus-trap proves non-trivial, prefer a ~30-line local hook
  over adding a library; a dependency would be a §J decision, not a default.)
- P2-M1A depends on: R1 tokens/primitives (merged). P2-M1B depends on: P2-M1A **approved**.
- P2-M2 (type migration) depends on P2-M1B. Layer-2 depends on P2-M2.

## 23. Risk map
| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Shell looks wrong at ultra-wide/mobile before product-wide use | Med | High | **M1A validates at all 6 viewports before M1B** (the whole point of the split) |
| Migrating pages regresses functionality (routing/forms/fetch/Timeline) | Med | High | breadth-first + per-route functional live check + diff-scope audit in M1B |
| Vertical-slice inconsistency (one page migrated, rest not) | Low | High | M1B migrates all four together; M1A demo is synthetic, not a half-migrated page |
| Scope creep (Tabs/Table/Modal pulled into M1) | Med | Med | §17 defers them; escalate if blocking, don't silently expand |
| NavItem additive API breaks existing top-nav | Low | Med | additive-only props + isolated-render before/after; existing callers unchanged |
| Sticky top bar obscures content / CLS | Low | Med | height compensation (skill), reserve space |
| Drawer a11y (trap/Escape/return) done wrong | Med | Med | explicit a11y acceptance (§13) + live keyboard CDP checks |

## 24. `ui-ux-pro-max` research findings (used critically)
- **Identity validated:** Developer Tool/IDE → Dark + Minimal + Terminal/Monitor + Blue focus + Bento.
  Reinforces a restrained instrument-panel shell; steers away from generic SaaS chrome. (Confirms
  direction — not a change.)
- **Container width:** text 65–75ch; data-dense may go wider → the `contentWidth` token set (§10).
- **Navigation:** flat app → **no breadcrumbs**; **sticky nav must compensate height**; **preserve
  history** (already via `useRouting`).
- **Responsive:** **mobile-first** breakpoints; no horizontal scroll; avoid fixed-px container widths →
  soft-cap tokens, not hard universal max-width.
- **A11y:** visible focus (have it), **44×44 touch + 8px spacing** (current NavItem too small on touch →
  rail/drawer sizing), keyboard order = visual order, drawer needs Modal-Escape semantics, sequential
  headings.
- **CONFLICT (documented, §15/§J-1):** skill recommends **bottom tab bar for ≤5 mobile items**;
  approved decision is a **drawer**. Raised as a decision point; not silently applied.
- **Not adopted:** no trend imported without a product-density justification.

## 25. Verification strategy
- Per-task **subagent-driven** execution (fresh implementer + reviewer per task), a **whole-branch
  review** at each stage end, and honest claim classification (live / isolated-render / code-comparison
  / not-verified) — the R1 discipline.
- M1A gate = the viewport matrix (§12) + build/lint + a11y checks. M1B gate = §30.

## 26. CDP / live-browser verification plan
- Serve the **shell-demo** entry (separate Vite HTML entry, like the R1 gallery) via vite; drive
  headless Chrome over CDP (existing `cdp.mjs` driver).
- Capture screenshots at all six viewports (390/768/1024/1440/1920/2560), expanded + collapsed sidebar,
  plus the mobile drawer open (390/768). Run live keyboard checks (Tab order, drawer focus-trap +
  Escape, `aria-current`) and computed-style assertions (touch-target ≥44px on mobile, focus ring
  present).
- **M1B:** additionally serve the real merged app and live-verify each route still functions (navigate,
  submit a Playground task path against the synthetic/gated flow, open Observability graph, Evals tabs,
  Memories table) — functional parity, not just pixels.

## 27. Build / lint verification
- `npm run build` must succeed each stage (report gzip; shell adds no deps so JS bundle grows only by
  the shell code). `npm run lint` must stay at exactly the 5 established baseline warnings, zero new.

## 28. Visual review procedure (the checkpoint you asked for)
- The review artifact is the **shell-demo app** (not a component gallery of isolated parts): it mounts
  the real `AppShell` with representative content per surface type so the shell is judged **in context**.
- It must demonstrate: expanded sidebar, collapsed sidebar, active nav, hover state, focus state, top
  context bar, main content area, long/scrolling content, data-dense content (a wide table), an
  `EmptyState`, responsive behavior across the six viewports, and the mobile navigation drawer.
- Delivered as: a served URL you can open, plus a CDP screenshot set per viewport/state. You review and
  give **Go/No-Go** before any product migration (M1B).

## 29. P2-M1A Go/No-Go criteria
GO requires ALL of:
1. Shell renders correctly at all six viewports (matrix §12), expanded + collapsed + mobile drawer.
2. Ultra-wide (1920/2560) feels intentional — data-dense demo uses horizontal space; readable content
   soft-capped; no tiny-site-in-a-void.
3. Mobile (390) is a real mobile pattern (drawer), no horizontal scroll, 44px touch targets.
4. A11y: keyboard-operable sidebar + drawer (trap/Escape/return), visible focus, accessible collapsed
   labels, correct landmarks/headings — all live-verified.
5. Width + breakpoint tokens are additive, documented in DESIGN.md/design.json, consistent.
6. `NavItem` additive API is backward-compatible (existing horizontal nav unaffected).
7. Build passes; lint = 5 baseline warnings, zero new.
8. No product page/backend/data touched in M1A; shell-demo is a separate entry with zero product
   blast-radius.
9. Whole-branch review: no Critical/Important open.
10. The §J decisions (drawer vs bottom-tabs, exact width caps, primitive dir, persistence) are resolved
    by you.

## 30. P2-M1B acceptance criteria
1. All four surfaces render inside `AppShell` with their approved density width; the old per-page
   header/container inconsistencies are gone.
2. **Functional parity**: routing (all 4 views + back/forward), Playground submit flow, Observability
   graph + sub-views, Evals tabs/tables, Memories table, ApiKey gate — all still work (live-verified).
3. `isNarrow` replaced by `useBreakpoint`; responsive behavior correct at all six viewports.
4. `Button` inline/size/icon API added (additive) and used where inline buttons are needed; existing
   full-width usages unchanged.
5. No deep redesign; Observability Timeline internals untouched; no backend/data/API/dep change.
6. Diff-scope audit: changes confined to App.jsx shell glue + page container/width edits + the shell
   primitives + Button; protected boundaries (§21) intact.
7. Build passes; lint = 5 baseline warnings, zero new. Whole-branch review clean.
8. Product-wide coherence: the whole app is uniformly on the shell (no half-migrated surface).

## 31. Estimated engineering effort
- **P2-M1A:** ~6–9 subagent tasks (tokens+hook; AppShell; Sidebar+NavItem rail; TopBar; PageContainer;
  shell-demo; a11y/responsive pass; viewport verification; report). ~1.5–3 focused working sessions.
- **P2-M1B:** ~5–7 tasks (Button API; migrate each of 4 surfaces; isNarrow→useBreakpoint; functional
  regression audit; report). ~1.5–3 sessions.
- Estimates are effort bands, not commitments; each task is small and reviewed.

## 32. Expected calendar time
- Dependent on session cadence, not wall-clock. Rough band: **P2-M1A ~2–4 days**, **Go/No-Go review**,
  **P2-M1B ~2–4 days** — assuming the R1 subagent cadence and no major re-scoping at the Go/No-Go.

## 33. Stopping points (hard stops)
- **After this plan** (now) — await approval to start P2-M1A.
- **After P2-M1A** — mandatory Go/No-Go visual review; do not begin M1B without explicit approval.
- **After P2-M1B** — stop before P2-M2 (type migration); await approval.
- No push at any point without explicit authorization.

## 34. Rollback / recovery strategy
- Each stage runs on its **own worktree/branch** off current `main`; nothing merges until its Go/No-Go.
- M1A is purely additive (new files + additive NavItem props + additive tokens) → rollback = discard the
  branch; product `main` is untouched.
- M1B edits pages → if the Go/No-Go fails, discard the branch (main unaffected). Because M1A merged
  first only after approval, a bad M1B never contaminates the validated shell.
- Progress tracked in a `.superpowers/sdd/progress.md` ledger per stage (survives compaction); commits
  are the recovery map.

---

## §J — Unresolved decisions requiring your approval (before P2-M1A starts)
1. **Mobile nav pattern:** approved **drawer** vs the skill's **bottom-tab-bar** (≤5 items) alternative.
   Recommend keeping the drawer for consistency with the desktop sidebar identity; flagging the conflict.
2. **Exact width caps:** proposed `readable 68ch`, `standard 1440px`, `wide 1600px`, `full 100%`, and
   `breakpoints 640/1024/1440/1920`. Approve or adjust the numbers.
3. **Shell primitive location:** proposed `components/shell/` subdir vs the flat `components/` dir.
4. **Sidebar collapsed-state persistence:** localStorage (remembered) vs session/in-memory.
5. **Button API timing:** deferring the `Button` inline/size/icon API to **P2-M1B** (recommended) vs
   pulling it into M1A.

*Plan ends. No implementation, no worktree, no source changes, no push. Awaiting explicit approval.*
