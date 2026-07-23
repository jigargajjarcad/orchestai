# Milestone 1 — Frontend Foundation (Design Tokens + Reusable Component System)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking. **All §8 decisions are approved as final — see §8 for
> the exact approved answers, including one deferred design decision explicitly carried forward
> to Milestone 2/3, not resolved here.**

**Goal:** Extract the already-decided Trace Console / Catppuccin Mocha visual system
(`DESIGN.md`, `.impeccable/design.json`) out of ~2,100 lines of copy-pasted inline `style={{}}`
objects across three page files, into (1) one real token source of truth and (2) a small,
deliberately minimal set of reusable presentational components — with **zero** intended visual
or behavioral change, except a small number of explicitly-flagged micro-inconsistencies that
need your decision (§8).

**Architecture:** Pure frontend refactor. No backend, API, or data-flow changes. No new
dependencies (§6). No routing, responsiveness, onboarding-copy, auth, or feature-behavior
changes — those are Milestone 2/3 per the approved Product Completion Backlog
(`docs/product-completion-backlog.md`, items A1/A2 only for this milestone).

**Tech Stack:** Existing stack only — plain React function components + inline styles (the
codebase's current, only styling mechanism), plain JS constants for tokens. No CSS Modules,
CSS-in-JS library, or CSS framework introduced.

**Scope: exactly `docs/product-completion-backlog.md`'s A1 and A2.** Nothing else from that
backlog is in scope here.

---

## 0. Where this plan lives, and why

Per repository convention, dated, task-by-task implementation plans live under
`docs/superpowers/plans/<date>-<slug>.md` (see `docs/superpowers/plans/2026-07-21-phase3-sports-mvp.md`
and seven others in the same directory) and are executed via
`superpowers:subagent-driven-development`/`superpowers:executing-plans`. This is one level
*below* the durable backlog document (`docs/product-completion-backlog.md`), which is itself
explicitly not a task-by-task plan — this file is the first such plan the backlog calls for.

**Repository fact, re-confirmed this session:** the repo is unchanged since the backlog was
approved — `git log -1` is still `c3e6cef`; `git status --short` shows only this new plan file
as untracked. Frontend file line counts (`App.jsx` 804, `ObservabilityPage.jsx` 601,
`EvalsPage.jsx` 475, `ApiKeyPrompt.jsx` 45, `apiKey.js` 48, `App.css` 11, `index.css` 111) match
the original investigation exactly — no drift to account for.

---

## 1. Current-State Evidence

**[Fact]** `DESIGN.md:100-269` and `.impeccable/design.json:6-79` fully specify the token set
(colors with named roles, a 4-step typography scale, a 4-step radius scale, a 5-step spacing
scale) and 4 pre-written component CSS blocks (`.ds-status-badge`, `.ds-btn-primary`,
`.ds-panel`, `.ds-status-card`, plus a `.ds-input`) — none of these classes are actually
imported or used anywhere in `frontend/src/`; every value is instead retyped as a literal hex
string or px number in each JSX file.

**[Fact]** `frontend/src/index.css` (111 lines) is the unmodified Vite React template's CSS —
light/dark purple-accent theme variables, `#root { width: 1126px; ...; text-align: center }` —
none of it is referenced by the actual dark-only, grid-laid-out UI that `App.jsx` renders. It
is dead code today, confirmed by the fact that none of its selectors/variables appear anywhere
in `App.jsx`/`ObservabilityPage.jsx`/`EvalsPage.jsx`.

**[Fact]** `frontend/src/App.css` (11 lines) is a minimal, still-relevant global reset (box-
sizing, body background/color/font-family) — this one *is* live and load-bearing.

**[Fact]** `frontend/package.json` — dependencies are `react`, `react-dom`, `react-markdown`
only; dev dependencies are Vite/React-plugin/oxlint/type-stubs. No CSS framework, no CSS-in-JS
library, no component library, no test framework (`grep -i test package.json` returns nothing).

**[Fact]** `frontend/index.html:7` still reads `<title>frontend</title>` (Vite default) and
`frontend/vite.config.js` has no path aliases configured. Neither is in scope for this
milestone (title/shell polish is A3, Milestone 2) — noted only so it isn't mistaken for an A1/A2
item later.

**[Fact]** No webfont is loaded anywhere (`index.html` has no font `<link>`, `public/` contains
only `favicon.svg`/`icons.svg`). The font-stack `'JetBrains Mono', 'Fira Code', monospace`
(`DESIGN.md:157-158`) silently falls back to the browser's default monospace font on any machine
without those fonts installed — meaning the "real," currently-seen visual identity for most
visitors is *generic monospace*, not necessarily JetBrains Mono. This is a genuine ambiguity
about what "preserve the visual identity exactly" means for typography (see §8, decision 5).

### Exact migration surface

| File | Lines | Token-eligible patterns found |
|---|---|---|
| `App.jsx` | 804 | `STATUS_COLORS` map (12-18, exact match to DESIGN.md's Status palette); `ToolCallRow` (20-53); `AgentCard` incl. its embedded status badge (55-127, badge at 76-85 is a byte-for-byte match to `.impeccable/design.json`'s `.ds-status-badge`); `ApprovalCard` incl. 4 button variants + a textarea (129-218); `ManagerReviewCard` (220-253, a second, narrower status-accented card); `MemoriesPage`'s eyebrow label + table (255-333); main header nav — 4 copy-pasted button blocks (610-651); a second, independently-styled task-status badge (656-671); the input form (686-729, incl. a primary button matching `.ds-btn-primary` exactly); the final-result panel + `ReactMarkdown` color overrides (763-799) |
| `ObservabilityPage.jsx` | 601 | `AGENT_COLORS`/`STATUS_COLORS` maps (7-22, match DESIGN.md's Tertiary/Status palettes); shared `panelStyle`/`labelStyle`/`selectStyle` constants (44-67) — reused across 5 sub-views; `SubNav` tab buttons (69-94, a second copy of the nav-item pattern); `TaskPicker`/`DateRangePicker` (96-125); status badges in `SummaryView` (277-282) and `ComparisonSide` (513-517); bespoke, **not** token-only, visualization internals in `SpanRow` (147-194, Gantt bar positioning math) and `BarChart` (327-356) |
| `EvalsPage.jsx` | 475 | Its own, independently-declared `panelStyle`/`labelStyle`/`selectStyle`/`buttonStyle` constants (7-41) — near-identical to, but a separate copy from, `ObservabilityPage.jsx`'s; `SubNav` (43-63, a third copy of the nav-item pattern); status-colored pass/fail text (244-246, 281-283) |
| `ApiKeyPrompt.jsx` | 45 | One `<input>`, one `<button>` — see §5 for whether these are in scope |
| `App.css` | 11 | Live global reset — left alone |
| `index.css` | 111 | Dead Vite-template CSS vars — removal/replacement is part of this milestone |

**Concrete evidence of drift from copy-paste-without-a-shared-source (the actual justification
for this milestone, not a hypothetical one):**
- `ObservabilityPage.jsx:51-57`'s and `EvalsPage.jsx:14-20`'s `labelStyle` constants are
  identical to each other, but `App.jsx`'s equivalent usages (e.g. line 284's `MemoriesPage`
  eyebrow) are ad hoc inline objects with a different `marginBottom` (16 vs. 8) — same intended
  pattern, three independent hand-typed copies.
- `App.jsx:657`'s task-status badge uses a `${statusColor}18` alpha suffix, while `AgentCard`'s
  badge at `App.jsx:77` (the same semantic component) uses `${statusColor}20` — both fall inside
  `DESIGN.md:210`'s stated "~18–20% alpha" range, so neither is "wrong," but they are two
  different literal values for what should be one pattern.
- `ApprovalCard`'s Approve/Reject buttons (`App.jsx:154-176`, green fill / red-ghost-then-red-
  fill) are not one of `DESIGN.md`'s two documented button variants (Primary/trace-blue,
  Secondary-Ghost) — and `DESIGN.md:151-153`'s own "One Accent Rule" states Trace Blue is "the
  only color used for interactive affordances." The shipped UI already deviates from this
  written rule for these two buttons. This is real, pre-existing behavior, not something this
  milestone introduces — left untouched and unresolved in Milestone 1 (§8, decision 2), with the
  resolution explicitly deferred to Milestone 2/3.

---

## 2. Goals and Non-Goals

**Goals**
- Establish one real, importable source of truth for every color/typography/radius/spacing
  value the current UI already uses, matching `DESIGN.md`/`.impeccable/design.json` exactly.
- Build the smallest set of reusable components that removes the copy-paste drift documented
  above, without inventing new visual language.
- Leave the rendered UI visually and behaviorally identical to today, except where §8's
  decisions explicitly authorize a change.

**Non-Goals** (explicitly deferred to later milestones per the approved backlog)
- Client-side routing, responsive design, onboarding copy/behavior, accessibility work, error/
  empty-state copy rewrites, table/chart componentization — all later A-items (A3–A26).
- Any change to the API-key authentication model.
- Any new orchestration-engine capability or new product feature.
- Any new dependency added "for architectural fashion" (CSS framework, CSS-in-JS library,
  state-management/data-fetching library) — none is justified by this milestone's scope.

---

## 3. Proposed Token Architecture

**[Recommendation]** A single plain-JS module, `frontend/src/theme/tokens.js`, exporting the
color/typography/radius/spacing values as plain constants (objects/strings), imported directly
into each component's existing inline `style={{}}` objects. This is deliberately the *smallest*
change consistent with "extract tokens," not a rearchitecture of how styling is authored:

- Matches the codebase's current and only styling mechanism (100% inline styles) — no new
  authoring pattern for any developer to learn.
- Requires zero new dependency and zero build-tooling change.
- Directly usable for the alpha-suffix pattern the codebase already relies on everywhere
  (`` `${color}20` ``) — a plain hex string constant supports that; a CSS custom property does
  not, without `color-mix()` and a broader styling-pattern change.

**[Alternative — considered, not chosen]** Real CSS custom properties in `index.css`
(replacing its current dead Vite-template variables) consumed via `var(--token)` from CSS
classes, closer to what `.impeccable/design.json`'s pre-written `.ds-*` classes anticipate. This
was rejected for Milestone 1 (§8, decision 1) as a larger, genuine architecture change — every
inline `style={{}}` object would need to become a CSS class, or a hybrid — bigger than
"extraction," and arguably overlapping with A2's component work in an entangled way rather than
a clean two-step build.

Token file contents (regardless of which option is approved) are a direct, value-for-value
transcription from `DESIGN.md:100-269`/`.impeccable/design.json:6-79` — colors (crust through
status-warning), the 4 typography styles (heading/title/body/label), the 4 radii
(sm/md/lg/xl), the 5 spacing steps (xs/sm/md/lg/xl), and the two documented alpha conventions
(20% badge background / 50–60% badge border) as precomputed hex+alpha-suffix constants matching
the codebase's existing 2-digit-hex-alpha-suffix technique exactly (no new technique
introduced).

`index.css`'s dead custom properties are removed either way — they are unused today regardless
of which token option is chosen, so their removal isn't contingent on the decision in §8.

---

## 4. Proposed Component Architecture

**[Recommendation]** New directory `frontend/src/components/`, one file per component, each a
plain function component consuming `theme/tokens.js`. Deliberately **not** building a Table,
chart, or markdown-theme component in this milestone — see the "explicitly left alone" list
below.

| Component | Responsibility | Rough props | Replaces |
|---|---|---|---|
| `Label` | 11px/700/uppercase eyebrow text | `children` | `App.jsx:284` ad hoc object; `ObservabilityPage.jsx:51-57`/`EvalsPage.jsx:14-20` `labelStyle` constants |
| `StatusBadge` | Tinted-pill status indicator | `status`, optional `label` override | `App.jsx:76-85`, `App.jsx:656-671` (resolves the 18%/20% drift — see §8 decision 3), `ObservabilityPage.jsx:277-282,513-517` |
| `Panel` | Base card surface; optional `accentStatus` prop adds the 3px status-tinted left border | `children`, `accentStatus?` | `.ds-panel`/`.ds-status-card` usages: `ObservabilityPage.jsx`'s `panelStyle`, `EvalsPage.jsx`'s `panelStyle`, `AgentCard`/`ApprovalCard`/`ManagerReviewCard`'s outer wrappers in `App.jsx` |
| `Button` | Primary (trace-blue) and Ghost/Secondary variants, matching `.ds-btn-primary` exactly | `variant`, `disabled`, `onClick`, `type`, `children` | `App.jsx:716-728` (submit), `ObservabilityPage.jsx`/`EvalsPage.jsx`'s `buttonStyle` constant, `ApprovalCard`'s ghost/cancel buttons |
| `Input` / `TextArea` | Text field styled per `.ds-input`, `Label` rendered above per `DESIGN.md:227-228`'s own rule ("always the Label typography style directly above the field, never inline placeholder-only") | `label`, `value`, `onChange`, rest passthrough | `App.jsx:687-707`, `ApprovalCard`'s textarea, `ObservabilityPage.jsx`'s `selectStyle`/date inputs, `EvalsPage.jsx`'s form fields |
| `NavItem` (+ thin `Nav` row wrapper) | Flat text nav button, active-state background fill | `active`, `onClick`, `children` | `App.jsx:610-651` (4x copy-pasted), `ObservabilityPage.jsx:69-94`'s `SubNav`, `EvalsPage.jsx:43-63`'s `SubNav` — three independent copies of the same pattern today |
| Minimal state-text primitive (naming TBD, e.g. `Muted`/`InlineError`) | Style-only wrapper for loading/empty/error copy — **content/copy itself is explicitly not touched**, only the repeated inline color/typography values | `children`, `tone` (`muted`/`error`) | The many independent `color: '#585b70'`/`color: '#f38ba8'` one-off text spans across all three files |

**Explicitly left alone in this milestone** (assessed and deliberately deferred, not
overlooked):
- **Table markup** (`MemoriesPage`, `DashboardView`'s breakdown table, `ErrorRateTable`,
  `EvalsPage`'s results/regression tables) — four independent hand-rolled tables exist; building
  a generic `Table` component now, before Milestone 3 touches these pages' actual content/UX,
  risks guessing at the wrong abstraction. Left as current inline styles for now, revisited in
  Milestone 3 (A17/A21).
- **`SpanRow`'s Gantt positioning math and `BarChart`'s bar rendering** — bespoke visualization
  logic, not a generic UI pattern; only their color/typography values become token references,
  their structure is untouched.
- **`ReactMarkdown`'s `components` prop overrides** (`App.jsx:776-787`) — single call site; its
  color values move to token references, but it does not become a shared component.

**As-is vs. refactor vs. extract, per instruction 8:**
- **Extract as-is (behavior/markup unchanged, only styling source changes):** `ToolCallRow`,
  `AgentCard`, `ManagerReviewCard`, `MemoriesPage`'s table wrapper — these are already
  well-formed, single-purpose components; they gain `Panel`/`StatusBadge`/`Label` as children,
  nothing about their own logic changes.
- **Refactor (visible markup consolidates, behavior unchanged):** the header nav (4 copy-pasted
  buttons → `Nav`/`NavItem`), all three files' `SubNav` (→ `NavItem`), all three files'
  `panelStyle`/`labelStyle`/`selectStyle`/`buttonStyle` constants (→ imports from the new
  components instead of locally-declared style objects).
- **Leave alone:** `SpanRow`, `BarChart`, all table markup, the `ReactMarkdown` override map
  (only color values touched) — per the "explicitly left alone" list above.

---

## 5. `ApiKeyPrompt.jsx` — explicit assessment (instruction 9)

**[Recommendation]** Partial inclusion, narrowly scoped: swap its one `<input>` and one
`<button>` onto the new `Input`/`Button` components (mechanical substitution only — same
behavior, same copy, same layout position). This is low-risk (proves the new primitives work in
the one file that currently deviates most from the design system) and slightly reduces
Milestone 2/A7's later workload.

**Explicitly not touched here** (stays Milestone 2/A7 work): the component's plain-prose copy,
its `fontFamily: 'sans-serif'` override (the one place in the whole frontend breaking the
mono-only rule), its layout/centering, and any "what is OrchestAI / what is this key for"
framing — all of that is A7's first-run/product-entry redesign, not a foundation concern.

This narrow inclusion (input/button only, nothing else) is approved as final in §8, decision 4.

---

## 6. Dependency Changes

**[Recommendation] None.** No new npm package is required or proposed for either A1 or A2 — the
token module is plain JS constants, and every component is a plain React function component
using the existing inline-style pattern. This directly satisfies instruction 11 (no CSS
framework, design-system library, or state-management/data-fetching library "for architectural
fashion").

---

## 7. Migration Order and Rationale

- [ ] **Step 1 — Token module.** Build `frontend/src/theme/tokens.js` (and, only if decision 1
  in §8 chooses the CSS-custom-property option, a matching `tokens.css`), transcribing
  `DESIGN.md`/`.impeccable/design.json` value-for-value. Resolve the flagged micro-
  inconsistencies (§8 decisions 2/3/5) as directed by your answers, not by default choice.
- [ ] **Step 2 — Remove dead CSS.** Strip `index.css`'s unused Vite-template variables/rules
  (confirmed dead regardless of decision 1's outcome).
- [ ] **Step 3 — Build the component set** in `frontend/src/components/`: `Label`,
  `StatusBadge`, `Panel`, `Button`, `Input`/`TextArea`, `NavItem`/`Nav`, and the minimal
  state-text primitive — each consuming Step 1's token module, each covering the exact prop
  surface in §4's table.
- [ ] **Step 4 — Migrate `App.jsx`** onto the Step 3 components first — it has the widest
  variety of shapes (nav, form fields, all three status-card types, tool-call rows, the
  markdown color overrides), so migrating it first proves the component APIs against the
  broadest real usage before the narrower `ObservabilityPage.jsx`/`EvalsPage.jsx` migrations.
- [ ] **Step 5 — Migrate `ObservabilityPage.jsx`** onto the same components (its
  `panelStyle`/`labelStyle`/`selectStyle` constants and `SubNav` are direct replacements); leave
  `SpanRow`/`BarChart` internals untouched except color/typography token references.
- [ ] **Step 6 — Migrate `EvalsPage.jsx`** the same way (`panelStyle`/`labelStyle`/
  `selectStyle`/`buttonStyle`, `SubNav`).
- [ ] **Step 7 — Migrate `ApiKeyPrompt.jsx`'s input/button only** (approved, §8 decision 4).
- [ ] **Step 8 — Verification pass** (see §9).
- [ ] **Step 9 — Final sweep:** grep the three page files for any remaining literal hex value
  from `DESIGN.md`'s palette that should have moved to the token module but didn't.

---

## 8. Decisions — Final (approved)

All five decisions below are approved as final for Milestone 1. None is reopened here.

1. **Token architecture — APPROVED.** Plain JS constants module at
   `frontend/src/theme/tokens.js`. No CSS custom properties, CSS Modules, CSS-in-JS, Tailwind,
   or any styling framework.
2. **Button variants — FINAL DECISION: no new variants in Milestone 1.** `Button` ships with
   only the two documented variants (Primary/trace-blue, Secondary-Ghost). `ApprovalCard`'s
   Approve/Reject buttons stay local one-off styling, outside the shared `Button` component, so
   Milestone 1 does not permanently encode a potentially incorrect design decision into the
   shared component API. **This is not a silent endorsement of the current green/red treatment
   as the design-system standard** — see the deferred design decision recorded immediately below.
3. **Badge alpha — APPROVED.** `StatusBadge` standardizes on the 20% background alpha. The
   existing 18% (`App.jsx:657`) vs. 20% (`App.jsx:77`) difference is treated as unintentional
   copy-paste drift, not a deliberate distinction, and is resolved to 20% everywhere.
4. **`ApiKeyPrompt.jsx` — APPROVED, narrow scope.** Migrate only its existing `<input>` and
   `<button>` onto the new `Input`/`Button` primitives. Its copy, layout, centering, onboarding/
   product-entry experience, and authentication behavior are unchanged. Its
   `fontFamily: 'sans-serif'` override remains, deferred to Milestone 2/A7.
5. **Typography/webfont — DEFERRED.** No webfont added, self-hosted, or loaded in Milestone 1.
   Existing font-stack and fallback behavior unchanged. Durably tracked in
   `docs/product-completion-backlog.md` under A3 (cross-referenced from A7), not left only in
   this plan — see that document for the recorded finding.

### Deferred design decision recorded for Milestone 2/3: `ApprovalCard`'s color treatment vs. the One Accent Rule

Not resolved here, and not to be treated as resolved by Milestone 1's inaction:

- `ApprovalCard`'s Approve (green fill) / Reject (red fill/ghost) buttons conflict with
  `DESIGN.md:151-153`'s documented "One Accent Rule" ("Trace Blue is the only color used for
  interactive affordances").
- A future milestone (2 or 3) must explicitly decide whether to (a) preserve the current
  green/red semantic color differentiation as an accepted, named exception to the One Accent
  Rule, or (b) redesign those two actions to comply with it — e.g. via icons, label wording, or
  another non-color-based distinction accessible without relying on color alone.
- Milestone 1 makes **no** visual change to these two buttons and does **not** decide this
  question either way — their current rendering is preserved exactly, as inherited, pending that
  future decision.

---

## 9. Testing and Verification Strategy

**[Fact]** The frontend has zero automated test coverage today (no test script in
`package.json`, no test framework installed) — this is a pre-existing gap, not something this
milestone is expected to fix (adding a test framework would itself be a dependency decision
requiring approval per instruction 11, and is out of this milestone's scope).

Verification for this milestone therefore relies on:
1. `npm run build` completes with zero errors.
2. `npm run lint` (oxlint) passes with zero new warnings/errors introduced.
3. Manual click-through, in the dev server, of every view that exists today: the Playground
   submit → live SSE flow (including an approval-required run, to exercise `ApprovalCard`), the
   Memories page, all 5 Observability sub-views, all 4 Evals sub-views, and the `ApiKeyPrompt`
   screen.
4. Before/after visual comparison (manual screenshots, not automated visual-regression tooling —
   installing such a tool is a dependency decision outside this milestone's scope) for each of
   the views above, to confirm no unintended pixel drift beyond what §8's decisions explicitly
   authorize.
5. A final grep sweep (§7 Step 9) confirming no stray hardcoded token-eligible hex value was
   left behind outside the token module.

This matches the project's own established discipline of live-verifying rather than trusting a
diff or a build alone (the same practice that caught real bugs in Phase 1 and Phase 3).

---

## 10. Regression Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Subtle unintended pixel drift during extraction (spacing/color/radius mismatch) | 1:1 value transcription from `DESIGN.md`/`.impeccable/design.json`; manual before/after comparison (§9); known drift points resolved explicitly via §8, not silently |
| Behavioral regression (SSE handling, form submission, API calls break) | All `useState`/`useEffect`/handler logic stays exactly where it is in the page files — only JSX markup/style objects are replaced by component usage; no logic is moved during this milestone |
| No automated test coverage to catch a regression | Manual click-through of every existing view (§9) is the compensating control; consistent with this project's established live-verification practice |
| Scope creep into routing/responsiveness/onboarding-copy/accessibility "while already in the file" | Firmly bounded by §2's non-goals; any such change belongs to a later milestone even if it's tempting to fix in passing |
| Over-engineering the component system (Table, chart, or markdown-theme components, excess variant surface) | §4's explicit "left alone" list; only the 7 components in §4's table are built |

---

## 11. Definition of Done

**A1 — Design token extraction**
- A single, real token source of truth exists (`frontend/src/theme/tokens.js`, per §8 decision
  1), containing every color/typography/radius/spacing value currently hardcoded across the
  three page files, matching `DESIGN.md`/`.impeccable/design.json` exactly.
- No component computes a raw token-eligible hex/px value inline anymore — only genuinely
  one-off layout numbers (grid columns, chart-specific pixel math, `maxHeight` scroll boxes)
  remain as local literals.
- `index.css`'s dead Vite-template variables are removed.
- Visual output is unchanged from today except for the specific points §8 explicitly resolves.

**A2 — Reusable component system**
- `Label`, `StatusBadge`, `Panel`, `Button`, `Input`/`TextArea`, `NavItem`/`Nav`, and the
  minimal state-text primitive exist as real, imported components, each consuming the A1 token
  module.
- Every current usage of these seven patterns across `App.jsx`/`ObservabilityPage.jsx`/
  `EvalsPage.jsx` and `ApiKeyPrompt.jsx`'s input/button only (§8 decision 4) is migrated onto
  them — no duplicate hand-rolled copies of these same patterns remain in those files.
- `ApprovalCard`'s Approve/Reject buttons remain local one-off styling, unchanged, per §8
  decision 2 — not migrated onto `Button`, not visually altered.
- Table markup, chart internals, and the markdown-override map are explicitly untouched beyond
  token references, per §4.
- `npm run build` and `npm run lint` both pass cleanly.
- Manual click-through (§9) of every existing view shows unchanged behavior and unchanged
  appearance, except the one approved intentional change (§8 decision 3: `StatusBadge` alpha
  standardized to 20%).
- Zero new npm dependencies were added.
- No routing, responsive, onboarding-behavior, auth-model, or feature-page-behavior change
  occurred, beyond the narrow, approved `ApiKeyPrompt` input/button component-wiring.

---

*§§1-11 above were written during planning, before implementation began — no source or
application files were modified while producing this document at that time, and no worktree
existed yet. §8's decisions are now approved as final; implementation proceeds in an isolated
worktree, logged in §12 below.*
