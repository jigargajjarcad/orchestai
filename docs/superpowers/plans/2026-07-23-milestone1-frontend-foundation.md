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

**[Fact — corrected during Task 1, was wrong in the original plan]** `frontend/src/index.css`
(111 lines) is the unmodified Vite React template's CSS, but it is **not entirely dead** — the
original plan claimed this based on a class-name/variable grep, which cannot detect bare
element-selector leakage. Task 1's implementer verified real rendering via `getComputedStyle`
on the built app and found it splits into two groups:
- **Genuinely dead, safe to remove (removed in Task 1):** `#social .button-icon` (no `#social`
  element exists anywhere), the `@media (max-width: 1024px)` responsive `font-size` override
  nested under `:root`, and `p { margin: 0 }` (a true no-op — `App.css`'s `* { margin: 0 }`
  already wins on specificity).
- **Live, currently affecting real rendering — NOT removed in Milestone 1:** the `:root`
  color/font custom properties, the `h1, h2` element selectors, the `code` element selector, and
  the `@media (prefers-color-scheme: dark)` block that redefines those same custom properties
  (two deferred decisions in §8) — plus a third rule the original plan also mis-assumed was
  dead: **`#root`'s `width: 1126px` / centering / `border-inline` block**. `App.jsx`'s `<h1>`
  and `ApiKeyPrompt.jsx`'s `<h2>` only set some inline properties (not `font-family`/
  `letter-spacing`), and `EvalsPage.jsx`'s `<code>` snippet (line 71) has no inline style at all,
  so the `h1`/`h2`/`code` rules fill in what inline styles don't cover. `#root`'s rule is
  different in kind, not a DESIGN.md conflict: removing it was verified (via a before/after
  screenshot diff) to shift the entire app ~157px and drop the centered column and its border —
  this is simply confirmed load-bearing, current, correct layout, already reflected in every
  approved baseline screenshot. It does not conflict with any documented rule and no visual
  change is being contemplated for it, so unlike the two items in §8 it needed no human decision
  — it is left in place because it is already what "correct" looks like today, not because a
  fix is being deferred.

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
| `index.css` | 111 | Partially dead Vite-template CSS — only the genuinely-unused subset is removed this milestone; the live `h1`/`h2`/`code`/`:root`-vars rules are left exactly as-is (two new deferred decisions, §8) |

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
(replacing its current Vite-template variables — which, per the correction above, are not all
dead) consumed via `var(--token)` from CSS classes, closer to what `.impeccable/design.json`'s
pre-written `.ds-*` classes anticipate. This was rejected for Milestone 1 (§8, decision 1) as a
larger, genuine architecture change — every inline `style={{}}` object would need to become a
CSS class, or a hybrid — bigger than "extraction," and arguably overlapping with A2's component
work in an entangled way rather than a clean two-step build.

Token file contents (regardless of which option is approved) are a direct, value-for-value
transcription from `DESIGN.md:100-269`/`.impeccable/design.json:6-79` — colors (crust through
status-warning), the 4 typography styles (heading/title/body/label), the 4 radii
(sm/md/lg/xl), the 5 spacing steps (xs/sm/md/lg/xl), and the two documented alpha conventions
(20% badge background / 50–60% badge border) as precomputed hex+alpha-suffix constants matching
the codebase's existing 2-digit-hex-alpha-suffix technique exactly (no new technique
introduced).

Only `index.css`'s genuinely-dead rules (§1's corrected list) are removed either way — that
subset's removal isn't contingent on the decision in §8. The live `h1`/`h2`/`code`/`:root`-vars
rules are untouched regardless of which token option is chosen — see the two deferred decisions
in §8.

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

## 7. Migration Order and Rationale — Tasks

Six tasks, strictly sequential (each depends on the previous). Do not reorder. Global
constraints binding every task: no new npm dependency; no routing/responsive/onboarding-
copy/accessibility/auth/feature-behavior change; no Table/chart/markdown-override
componentization; preserve all existing state/handler logic exactly, touch only JSX markup and
style sourcing.

### Task 1: Design token module + dead CSS cleanup

**Files:** Create `frontend/src/theme/tokens.js`. Modify `frontend/src/index.css`.

**Spec:**
- Read `DESIGN.md` (full file) and `.impeccable/design.json` (full file) as the source of truth.
  Transcribe every named color (crust, base, mantle, surface0, surface2, overlay0, subtext0,
  text, trace-blue, signal-green, alert-red, beacon-yellow, agent-mauve, agent-sky, agent-peach,
  status-running/completed/failed/pending/warning, error-bg), the 4 typography styles
  (heading/title/body/label — family, size, weight, line-height, letter-spacing), the 4 radii
  (sm/md/lg/xl), and the 5 spacing steps (xs/sm/md/lg/xl) into `tokens.js` as plain named
  JS exports (e.g. `export const colors = { crust: '#11111b', ... }`, `export const radii = {...}`,
  `export const spacing = {...}`, `export const typography = {...}`).
- **Approved architecture (§8 decision 1 — final, do not deviate):** plain JS constants only.
  Do NOT create a `tokens.css`, CSS custom properties, or any CSS Modules/CSS-in-JS file.
- Also export precomputed alpha-suffixed variants for the status-badge pattern, using the
  codebase's existing 2-digit-hex-alpha-suffix technique (e.g. a badge background at hex+`33`
  for 20% — verify the exact 2-digit hex for 20% is `33`, and confirm the border alpha the
  codebase already uses at ~50-60%, e.g. `${color}99`/`${color}A0`-equivalent 2-digit suffixes —
  derive the precise hex pairs mathematically, do not guess) for each of the 5 status colors,
  named clearly (e.g. `statusBadgeAlpha = { background: '33', border: '99' }` as suffix
  constants, or fully precomputed per-color strings — implementer's choice, whichever is
  cleanest to consume from `StatusBadge` in Task 2).
- **`index.css` cleanup — corrected scope (post-discovery, twice-corrected — see below):**
  `index.css` is **not** entirely dead. Verify each rule's live/dead status by real
  `getComputedStyle` output on the built app (a class-name/variable grep is not sufficient — it
  cannot detect bare element-selector leakage), not by assumption — re-verify even the rules this
  brief calls "safe," since this exact plan already got one of them wrong once (`#root`, below).
  Delete only: `#social .button-icon`, the `@media (max-width: 1024px)` responsive `font-size`
  override nested under `:root`, and `p { margin: 0 }` (a true no-op given `App.css`'s
  `* { margin: 0 }`). **Do NOT delete or modify** the `:root` color/font custom properties, the
  `h1, h2` selectors, the `code` selector, the `@media (prefers-color-scheme: dark)` block, or
  `#root`'s own `width: 1126px`/centering/`border-inline` rule — the last of these is
  **not** a DESIGN.md conflict like the others; it is simply confirmed, currently load-bearing
  layout (verified via before/after screenshot diff: removing it shifts the app ~157px and drops
  the centered column/border) that is already what every approved baseline screenshot shows, so
  it needs no decision, just no touching. The `h1`/`h2`/`code`/`:root`-vars group is different —
  those fill in `font-family`/`letter-spacing`/colors that `App.jsx`'s `<h1>`,
  `ApiKeyPrompt.jsx`'s `<h2>`, and `EvalsPage.jsx`'s `<code>` don't set
  inline) and fixing them is an approved-later, not approved-now, visual change — see the two
  deferred decisions in §8. Leaving them in place, unmodified, is correct for this task, not an
  incomplete cleanup.
- Do not touch `App.css` (confirmed live: global box-sizing reset + body background/color/font).
- No other file changes. No new dependency.

**Verification:** `npm run build` and `npm run lint` clean. No visual change yet (tokens.js is
not consumed by anything until Task 2) — confirm by running the app and comparing to the
existing baseline screenshots at `/tmp/m1-verify/baseline/*.png` (paths given in dispatch
context) if easy to do; not blocking if the dev server isn't already running for this task,
since no component consumes the tokens yet.

---

### Task 2: Shared component primitives

**Files:** Create `frontend/src/components/Label.jsx`, `StatusBadge.jsx`, `Panel.jsx`,
`Button.jsx`, `Input.jsx` (export both a text-input and a textarea variant from this one file),
`NavItem.jsx` (export both `NavItem` and a thin `Nav` row wrapper from this one file), and one
minimal state-text primitive file (name it `StateText.jsx`, exporting a single component with a
`tone` prop of `'muted'` or `'error'`).

**Depends on:** Task 1's `frontend/src/theme/tokens.js` — every component imports from it, no
component computes a raw hex/px value itself.

**Spec — build exactly these seven, nothing more:**
- **`Label`**: renders the Label typography style (11px/700/uppercase/0.07em letter-spacing) in
  its token color. Props: `children`. No variant props needed.
- **`StatusBadge`**: renders the tinted-pill pattern (`.impeccable/design.json`'s
  `.ds-status-badge`: background at the status color's ~20% alpha, border at ~50-60% alpha,
  full-opacity status-color text, 4px radius, `2px 10px` padding, uppercase 11px/700/0.05-0.06em
  label). Props: `status` (one of `Pending`/`Running`/`WaitingForApproval`/`Completed`/`Failed`/
  `Skipped` — cover all six current values, found across `App.jsx`'s `STATUS_COLORS` and
  `ObservabilityPage.jsx`'s `STATUS_COLORS`), optional `label` override (defaults to `status`
  uppercased). **Approved (§8 decision 3 — final): standardize on exactly 20% background alpha
  for every status, resolving the current 18%-vs-20% drift. This is the one deliberate,
  approved visual change in this milestone — do not treat any other alpha/color/spacing value as
  eligible for silent adjustment.**
- **`Panel`**: base card surface (`base` background, `surface0` 1px border, `xl` radius,
  `16px 18px` padding — matches `.ds-panel`). Optional `accentStatus` prop: when set, adds the
  3px status-tinted `border-left` and switches the outer border to a status-tinted ~40% alpha
  (matches `.ds-status-card`). Props: `children`, `accentStatus?`.
- **`Button`**: two variants only — `primary` (trace-blue background, crust text, 700 weight,
  matches `.ds-btn-primary` exactly, including its disabled state dropping to `surface0`
  background / `overlay0` text) and `ghost` (transparent background, colored 1px border at ~50%
  alpha, text matching the border's base color — used for Cancel/secondary actions). **Approved
  (§8 decision 2 — final): do NOT add `success` or `danger` variants.** `ApprovalCard`'s
  Approve/Reject buttons are explicitly NOT migrated onto this component in this milestone —
  leave them exactly as they are in `App.jsx` (local one-off styles), untouched, in Task 3.
  Props: `variant` (`primary`|`ghost`), `disabled`, `onClick`, `type`, `children`.
- **`Input`**: text-input variant matching `.ds-input` (1px `surface0` border, `#181825`
  background, 7-10px padding, no custom focus ring — none exists in the current design, don't
  invent one) and a `TextArea` variant (same visual treatment, `resize: vertical`). Both render
  an optional `Label` above the field per `DESIGN.md:227-228`'s documented rule ("always the
  Label typography style directly above the field, never inline placeholder-only"). Props:
  `label`, `value`, `onChange`, and pass through any other native input/textarea props
  (`placeholder`, `rows`, `required`, `type`, etc.) unchanged.
- **`NavItem`** + **`Nav`**: flat text nav button — transparent background normally, `surface0`
  background fill + text brightened from `overlay0` to `text` when active, no hover-lift, no
  shadow (matches `DESIGN.md`'s Navigation section exactly). `NavItem` props: `active`,
  `onClick`, `children`. `Nav` is a thin `display:flex, gap:4` row wrapper, nothing more.
- **`StateText`**: single-purpose text wrapper for loading/empty/error inline copy. Props:
  `children`, `tone` (`'muted'` renders in `overlay0`/`surface2`-family color at Body size;
  `'error'` renders in `alert-red`). **Do not change any existing copy/wording when this is
  consumed in Tasks 3-5 — this component only carries the styling, the text stays identical to
  today.**

**Explicitly do not build in this task (per the plan's §4):** any Table component, any chart
component, any markdown-override component. These stay untouched, per Tasks 3-5's scope.

**Verification:** `npm run build` and `npm run lint` clean. These components aren't consumed by
any page yet, so no visual change to verify live yet — a quick self-check that each component
renders without throwing (e.g. a scratch, disposable render in the dev server, removed before
committing) is sufficient; do not add a permanent test file (no test framework exists, and
adding one is out of scope for this milestone).

---

### Task 3: Migrate `App.jsx`

**Files:** Modify `frontend/src/App.jsx` only.

**Depends on:** Task 1 (`tokens.js`) and Task 2 (all seven components).

**Spec — migrate onto the Task 2 components, preserving every existing prop/state/handler
exactly:**
- `STATUS_COLORS` map (lines 12-18): replace direct consumption with `StatusBadge` wherever a
  status pill is rendered — this covers both the `AgentCard` badge (lines 76-85) and the
  header's task-status badge (lines 656-671, the one previously at 18% alpha — this is exactly
  where Task 2's approved 20%-alpha `StatusBadge` resolves that drift).
- `ToolCallRow` (lines 20-53): keep its own structure (this is a well-formed, single-purpose
  component, extract as-is per §4) but source its colors from `tokens.js` instead of literal hex.
- `AgentCard` (lines 55-127): its outer wrapper becomes `<Panel accentStatus={execution.status}>`;
  its status badge becomes `<StatusBadge status={execution.status} />`; its message/tool-call
  child content keeps its current structure, colors sourced from tokens.
- `ApprovalCard` (lines 129-218): its outer wrapper becomes `<Panel accentStatus="WaitingForApproval">`
  (or equivalent — the card already only ever renders in the waiting-for-approval context); its
  textarea becomes the Task 2 `TextArea`. **Its Approve/Reject/Cancel/Confirm-Reject buttons stay
  exactly as local one-off styles — do NOT migrate them onto the new `Button` component (§8
  decision 2, final).** No visual change to these four buttons at all.
- `ManagerReviewCard` (lines 220-253): outer wrapper becomes `<Panel accentStatus={...}>` using
  its own narrower running/completed color logic (do not force it onto the full 6-value
  `StatusBadge` status set if its running/completed distinction doesn't map 1:1 — use
  `StatusBadge` if it fits cleanly, otherwise keep its badge-equivalent element local; use your
  judgment and note the choice in your report).
- `MemoriesPage` (lines 255-333): its eyebrow label becomes `Label`; its "Loading…" and "No
  memories saved yet…" text becomes `StateText` (`tone="muted"`, same wording); its error banner
  becomes `StateText` (`tone="error"`, same wording). **Leave its `<table>` markup untouched**
  (Table componentization is explicitly out of scope — §4).
- Header nav (lines 610-651): the four copy-pasted button blocks become `<Nav>` wrapping four
  `<NavItem active={view==='...'} onClick={...}>`.
- The input form (lines 686-729): `Task Title`/`Prompt` fields become `Input`/`TextArea` with
  their `Label`; the checkbox row stays a native `<input type="checkbox">` (no shared component
  covers checkboxes in this milestone — leave its current inline styling as-is, it's a single,
  simple usage); the submit button becomes `<Button variant="primary">`.
- The error banner (line ~732) becomes `StateText` (`tone="error"`, same wording).
- Final-result panel (lines 763-799): outer wrapper becomes `Panel`; its "Waiting for agents…" /
  "Submit a task to see results here." text becomes `StateText` (`tone="muted"`, same wording).
  The `ReactMarkdown` `components` prop color values (lines 778-787) move to `tokens.js`
  references, but the override map itself stays local to this file (not a shared component,
  per §4).

**Do not touch:** any `useState`/`useEffect`/handler function, the SSE event-handling switch
statement, any API call, any prop shape passed between functions — only JSX markup and inline
`style` objects change.

**Verification:** `npm run build`/`npm run lint` clean. Live-verify against the running
dev server + API described in the dispatch context: load the app, confirm the ApiKeyPrompt
screen, set a key, confirm the Playground empty state, submit a task and observe the
Pending→Running→Failed flow (a real Anthropic call will fail in this environment — that is
expected and fine, the point is confirming the UI states render correctly), and check the
Memories page. Compare each against the matching baseline screenshot named in your dispatch.

---

### Task 4: Migrate `ObservabilityPage.jsx`

**Files:** Modify `frontend/src/ObservabilityPage.jsx` only.

**Depends on:** Tasks 1-3 (reuses the same components; migrating `App.jsx` first should have
already proven their APIs).

**Spec:**
- Its `AGENT_COLORS`/`STATUS_COLORS` maps (lines 7-22) and shared `panelStyle`/`labelStyle`/
  `selectStyle` constants (lines 44-67) are replaced by `Panel`/`Label`/`tokens.js` imports —
  delete the locally-declared constants once nothing references them.
- `SubNav` (lines 69-94): becomes `Nav`/`NavItem`, same as `App.jsx`'s header nav.
- `TaskPicker`/`DateRangePicker` (lines 96-125): their `<select>`/`<input type=date>` elements
  keep native semantics (no shared "Select" component exists in this milestone's scope) but
  adopt the `Input`-equivalent visual treatment/token colors where it fits the existing
  `selectStyle` shape; use `Label` for their field labels.
- Status badges in `SummaryView` (lines 277-282) and `ComparisonSide` (lines 513-517) become
  `StatusBadge`.
- **Leave `SpanRow` (lines 147-194) and `BarChart` (lines 327-356) structurally untouched** —
  only their literal color/typography values move to `tokens.js` references; their positioning
  math, rendering logic, and markup shape do not change (§4).
- **Leave all `<table>` markup untouched** (`DashboardView`'s breakdown table,
  `ErrorRateTable`) — Table componentization is explicitly out of scope (§4).
- Loading/error text (e.g. "No data in this range.", error messages) becomes `StateText`.

**Verification:** `npm run build`/`npm run lint` clean. Live-verify: click through all 5
Observability sub-views (Timeline, Summary, Cost Dashboard, Error Rates, Compare) against the
running dev server + API, comparing each to its matching baseline screenshot named in your
dispatch.

---

### Task 5: Migrate `EvalsPage.jsx`

**Files:** Modify `frontend/src/EvalsPage.jsx` only.

**Depends on:** Tasks 1-4.

**Spec:**
- Its own, independently-declared `panelStyle`/`labelStyle`/`selectStyle`/`buttonStyle`
  constants (lines 7-41) are replaced by `Panel`/`Label`/`Input`/`Button` imports — delete the
  local constants once unused.
- `SubNav` (lines 43-63): becomes `Nav`/`NavItem`, same pattern as the other two files.
- Form fields across `RunView`/`PostHocView` (subject version, baseline-run select, rubric
  textarea, date/agent-type/max-traces fields) become `Input`/`TextArea` with `Label`; trigger/
  submit/refresh buttons become `Button variant="primary"`.
- Pass/fail colored text (lines 244-246, 281-283) sources its green/red from `tokens.js` instead
  of literal hex — this is plain inline text color, not necessarily a `StatusBadge` usage; use
  judgment on whether any of these read naturally as a `StatusBadge` (e.g. a pass/fail pill) or
  should stay plain colored text as today — do not change the visual *treatment* (pill vs. plain
  text), only the color *source*.
- **Leave all `<table>` markup untouched** (results table, regression table) — Table
  componentization is explicitly out of scope (§4).
- Loading/empty/error text becomes `StateText`.

**Verification:** `npm run build`/`npm run lint` clean. Live-verify: click through all 4 Evals
sub-views (Suites, Run, Results, Post-Hoc) against the running dev server + API, comparing each
to its matching baseline screenshot named in your dispatch.

---

### Task 6: Migrate `ApiKeyPrompt.jsx` (input/button only)

**Files:** Modify `frontend/src/ApiKeyPrompt.jsx` only.

**Depends on:** Task 2 (`Input`, `Button`).

**Spec — approved narrow scope (§8 decision 4, final):**
- Replace its `<input type="password">` with the Task 2 `Input` component and its `<button>`
  with `Button variant="primary"` — mechanical substitution only, same behavior
  (`onSubmitted`/`setApiKey` calls unchanged), same value/onChange wiring, same position in the
  layout.
- **Do NOT change:** the surrounding `<div>`/`<h2>`/`<p>` copy, layout, centering, or the
  `fontFamily: 'sans-serif'` override on the outer wrapper (`ApiKeyPrompt.jsx:14`) — this
  remains exactly as it is, deferred to Milestone 2/A7. Do not add a `Label` above this input
  either, since the current design has no label here and adding one would be a layout/copy
  change beyond "swap the input and button primitives."

**Verification:** `npm run build`/`npm run lint` clean. Live-verify: load the app fresh (no key
set) and confirm the `ApiKeyPrompt` screen still behaves identically — typing a key and
clicking Continue still transitions to the Playground — comparing to the baseline screenshot
named in your dispatch.

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

### Deferred design decision recorded for Milestone 2/3: heading (`h1`/`h2`) typography leak

Discovered during Task 1, not anticipated by the original plan (which incorrectly assumed
`index.css` was entirely dead — corrected in §1). **Explicitly deferred by the human, decided
2026-07-23: Option 1 — defer, do not fix in Milestone 1.**

- `App.jsx`'s page-title `<h1>` and `ApiKeyPrompt.jsx`'s `<h2>` set only some inline style
  properties (`fontSize`/`fontWeight`/`color`/`margin`), not `font-family` or `letter-spacing`.
  `index.css`'s leftover `h1, h2 { font-family: var(--heading); ... letter-spacing: -1.68px; }`
  (and `h2`'s own `-0.24px`/`500`-weight rule) fills in those two properties, so both headings
  currently render in `system-ui, "Segoe UI", Roboto, sans-serif` with negative letter-spacing —
  not the mono font-stack `DESIGN.md` documents everywhere else.
- This conflicts with `DESIGN.md`'s own "No-Serif Rule" (§3: "Nothing in this system is ever set
  in a serif or humanist sans"), but is **intentionally not fixed in Milestone 1** — correcting
  it is a real, additional visual change beyond this milestone's one approved change (the
  `StatusBadge` 20% alpha), and is not this task's or this milestone's call to make.
- The underlying cause (legacy Vite-template `index.css` rules leaking through because the real
  inline styles don't cover every property the browser needs) is left exactly as-is, unmodified,
  in Task 1's `index.css` cleanup — not deleted, not corrected.
- A future milestone (2 or 3) must explicitly decide whether to correct the heading
  `font-family`/`letter-spacing` to match the `heading` token, and how (fixing the inline style
  directly vs. finally retiring the legacy `index.css` rule it depends on).

### Deferred design decision recorded for Milestone 2/3: `EvalsPage` `<code>` chip styling

Discovered during Task 1, alongside the heading finding above. **Explicitly deferred by the
human, decided 2026-07-23: Option 1 — defer, do not fix in Milestone 1.**

- `EvalsPage.jsx:71`'s inline `<code>POST /api/v1/eval-suites</code>` snippet carries no inline
  style at all — its entire appearance (font, background, text color, padding, radius) comes
  from `index.css`'s leftover `code { ... }` rule, which currently renders it as a light-cream
  chip (`background: rgb(244, 243, 236)`) with near-black text (`color: rgb(8, 6, 13)`) sitting
  inside an otherwise all-dark Catppuccin Mocha panel — already visible in the pre-Task-1
  baseline screenshot (`/tmp/m1-verify/baseline/08-evals-suites.png`).
- This is visually inconsistent with the dark, Catppuccin-based product design documented in
  `DESIGN.md`, but is **intentionally not fixed in Milestone 1** for the same reason as the
  heading finding above — it is a real, additional visual change beyond this milestone's one
  approved change, not this task's call.
- The underlying `index.css` `code` rule is left exactly as-is, unmodified, in Task 1's cleanup
  — not deleted, not corrected, not silently overridden by a new inline style.
- A future milestone (2 or 3) must explicitly decide how to fix this — likely either an inline
  style on this specific `<code>` element sourced from `tokens.js`, or a small shared inline-code
  treatment if more call sites like it turn up during Milestone 3's page-content work.

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
- `index.css`'s genuinely-dead rules are removed (`#root`, `#social .button-icon`, the `:root`
  responsive font-size override, `p { margin: 0 }`); its live `h1`/`h2`/`code`/`:root`-vars rules
  are left exactly as-is, per the two deferred decisions in §8 — this is intended, not
  incomplete.
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
