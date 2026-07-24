# Milestone 2 — Core Frontend Modernization (Investigation + Plan)

> **Status: investigation and planning only. Not approved for implementation.** Follows the
> same investigate-then-plan discipline used for Milestone 1
> (`docs/superpowers/plans/2026-07-23-milestone1-frontend-foundation.md`). No source/application
> file was modified while producing this document. No worktree was created.

---

## 1. Goal

Take the Milestone 1 foundation (`frontend/src/theme/tokens.js`, the seven shared components)
and use it to build the parts of the product experience that Milestone 1 explicitly did not
touch: a real application shell, URL-addressable navigation, responsive layout down to common
laptop/tablet widths, a genuine first-run/product-entry experience, API-key management UI, the
`remark-gfm` fix, and a real (not just token-colored) loading/empty/error/accessibility pass.

This is **not** a redesign of already-restyled feature pages (agent execution, memories,
observability, evals) — those were functionally and visually migrated onto the Milestone 1
component system already, as a byproduct of how Milestone 1 was actually implemented (see §3.1,
an important correction to the backlog's original planning-time assumption).

---

## 2. Exact Backlog Scope

Source: `docs/product-completion-backlog.md` §5 ("Recommended Execution Sequence"), item 3:

> "Milestone 2 — Core frontend modernization (A3–A8, A15, A22–A25): shell, routing, responsive
> design, onboarding + API-key management, `remark-gfm`, loading/empty/error states,
> accessibility baseline. Depends on Milestone 1."

**[Fact]** Milestone 1 is now the completed dependency this item names — confirmed merged and
pushed at `4af817c`.

### Included (Must Build, this milestone)

| # | Item | Backlog wording |
|---|---|---|
| A3 | Application shell (header/nav rebuilt on the component system) | `docs/product-completion-backlog.md:92` |
| A4 | Client-side routing (URL-addressable views) | `:93` |
| A5 | Responsive design — down to common laptop/tablet widths | `:94` |
| A6 | Navigation & information architecture consistency (same work as A3/A4) | `:96` |
| A7 | First-run / product-entry experience, within the closed API-key model | `:97` |
| A8 | API-key presentation/management within the existing model | `:98` |
| A15 | `remark-gfm` fix | `:106` |
| A22 | Loading states | `:114` |
| A23 | Empty states (incl. the Playground's default empty-result panel, per A7 cross-reference) | `:115` |
| A24 | Error states | `:116` |
| A25 | Accessibility baseline — ARIA labels, keyboard operability, visible focus states | `:117` |

### Explicitly excluded from Milestone 2 (named exceptions inside the same ranges)

- **A5a** (true mobile/phone optimization of dense views) — `DEFER` in the backlog
  (`:95`), not part of "A5" as cited in the §5 sequence. Confirmed: the backlog's own letter-
  suffixed rows (`A5a`, `A11a`, `A16a`, `A25a`) are consistently used throughout this document to
  mark "the deferred narrower cousin of the main MUST item" — never swept in by a bare number
  range like "A3–A8" or "A22–A25". This milestone follows that established convention, not a new
  interpretation.
- **A25a** (full formal WCAG AA audit/certification) — same reasoning, `DEFER` (`:118`).

### Not part of this backlog item at all (confirm not accidentally in scope)

A9–A21, A26 — the backlog's original sequence assigned these to "Milestone 3 — Frontend
feature-page restyle." **Important finding, not an assumption:** these are already
substantially done. See §3.1.

---

## 3. Current-State Evidence

All evidence below was gathered against the actual merged `main` at `4af817c` (re-confirmed via
`git log -1`, `git status --short` — clean), not against pre-Milestone-1 assumptions.

### 3.1 — Correction to the backlog's own sequencing assumption (important, affects future Milestone 3 scoping, not this one)

**[Fact]** The backlog's §5 sequence was written assuming Milestone 1 would be narrowly "A1, A2
only" (tokens + components, nothing consuming them), with all page-content restyling
("Milestone 3") to follow later. That is not what was actually built and merged. The Milestone 1
plan's own Tasks 3–6 (`docs/superpowers/plans/2026-07-23-milestone1-frontend-foundation.md §7`)
explicitly migrated:

- `App.jsx`: `AgentCard` (A10), `ToolCallRow` (A11), `ApprovalCard` (A12, partially — buttons/
  border deliberately kept local, see §5 below), `ManagerReviewCard` (A13), final-result
  presentation (A14), `MemoriesPage` (A16), the overall agent-execution experience (A9).
- `ObservabilityPage.jsx`: the dashboard, timeline, cost, and error views (A17–A20).
- `EvalsPage.jsx`: the full eval UI (A21).

**This means A9–A21 are already migrated onto the Milestone 1 component system**, not pending
"Milestone 3" work as the backlog originally assumed when it was written (before Milestone 1
started). This is a real finding worth recording now so a future Milestone 3 planning pass
doesn't duplicate work or get confused about scope — but it does **not** change Milestone 2's own
scope, which never included A9–A21 in the first place. A26 ("final cross-page UX consistency
pass... verifies A1–A25 actually cohere") genuinely cannot happen yet regardless, since it
explicitly depends on A22–A25 (this milestone) being done first — it stays correctly out of
Milestone 2.

**Important, explicit qualifier added after independent review:** "migrated" and "verified clean"
are not the same claim, and this plan's first draft did not distinguish them — Milestone 1's own
history is the reason that distinction matters (Task 3's first migration pass imported the
correct components but still produced 7 categories of real visual drift, caught only by actual
pixel-diffing, not by confirming an import existed). §3.1a below gives the per-item evidence,
using the exact verification method actually used for each — live pixel/computed-style comparison
where the environment could exercise it, direct code comparison where it could not (several
render paths structurally never fire in this dev environment, since no task ever completes
successfully without a real Anthropic API key) — and states plainly, not by default assumption,
which is which.

**[Recommendation, not a decision made here]** When Milestone 3 is eventually planned, its scope
should be re-derived from what's actually still open at that time (likely just A26, plus
whatever's left over from the deferred findings in §5 below, plus any genuine gaps named in
§3.1a) rather than assumed from the backlog's original, now-partially-stale, A9–A21 listing.

### 3.1a — Evidence table: verification depth per item

Source for every claim below: the actual Milestone 1 implementer/reviewer reports, which still
exist as local session artifacts at
`.claude/worktrees/milestone1-frontend-foundation/.superpowers/sdd/task-{3,4,5}-report.md` and
`task-final-cleanup-report.md` (these are git-ignored working files, not committed project
history — cited here so this evidence is captured durably in the one place that *is* committed,
this plan, rather than only existing in files that could be lost if that worktree is ever
cleaned up). Line numbers for current code are against the merged `main` at `4af817c`.

**Classification key** (exactly as required): **Complete / verified clean** · **Partially
complete** (gap named) · **Still requiring implementation** · **Not yet assessed**. Both live
pixel/computed-style comparison and an explicit, cited direct code comparison are real
verification — never a bare, uncited claim that a component is imported/used. But the two
methods are not treated as interchangeable for the top-level classification: **an element that
was never exercised in a live render at all — not even in an empty/default state — is classified
Partially complete even when its code has been directly, explicitly compared and matches
byte-for-byte or token-for-token.** "Complete / verified clean" is reserved for elements that were
actually rendered live and checked, in at least their ordinarily-reachable state; a populated
sub-state that never rendered live rests on the same code-comparison standard as the rest of that
element and is noted as a caveat within an otherwise-Complete row (see A17-A20's Cost Dashboard
note) rather than downgrading the whole row.

| Backlog item | File / component | What actually satisfies it (with line refs) | Verification method + citation | Gaps / caveats | Classification |
|---|---|---|---|---|---|
| A9 — Agent execution experience (form, header, SSE) | `App.jsx:308-661` (`App()` overall; header `:575-608`, form `:621-661`, hooks/handlers/SSE switch `:308-566` in between) | Form fields → `Input`/`TextArea`/`Button` (`App.jsx:623-660`); header task-status badge → `StatusBadge` (`App.jsx:600`); real SSE event handling drives real `Pending→Running→Failed` transitions | **Live, computed-style verified**, repeatedly: Task 3's final combined review (`review-71551f0..8fbf667.diff` review report) drove a real task submission and confirmed the header badge, form fields, and submit button's computed styles live; independently re-confirmed again during this session's post-merge controller verification and the final whole-branch review's own spot-check | The **live-streaming multi-message/multi-tool-call feed during a long, successful run** was never exercised in any check — this dev environment has no valid server-side Anthropic key, so every real task fails at the Orchestrator's planning call before any message/tool-call event ever fires. This is an environment/data limitation (confirmed structurally, not assumed), not an unverified code gap in the SSE-handling logic itself, which is untouched from before Milestone 1 (task-3-report.md:353: `handleSubmit`, the SSE `onmessage` switch, and every state-update helper it calls are confirmed "byte-for-byte unchanged"). | **Partially complete** — core form/header/status-transition path verified clean live; the successful-run streaming-feed visual path was never exercised by any method, live or code-compared, because it never occurred |
| A10 — Agent cards | `AgentCard` (`App.jsx:55-111`) | Outer shell → `<Panel accentStatus>` (`:58`) + `<StatusBadge borderAlphaSuffix="60">` (`:68`) | **Shell: live, computed-style verified** — a real `Failed`-status `AgentCard` (for the `Orchestrator`) rendered in every Task 3/4/5 live-verification run and in this session's own post-merge check; badge padding/letterSpacing/border-alpha were specifically measured via `getComputedStyle` in the final combined Task 2+3 review | The card's **conditional inner content** — the messages list (`:71-86`), the tool-call list (`:88-94`, renders `ToolCallRow`), the cost/token footer (`:96-101`), and the saved-memories line (`:103-107`) — never rendered with real data in any check, since the Orchestrator fails before writing messages, dispatching tools, or accruing cost. Verified only via **direct code comparison** (task-3-report.md:176, the `AgentCard` bullet itself, confirming its outer Panel/badge/border literals were swapped 1:1 for identical `colors.*` token values; the inner messages/cost/memory divs rest on that same general 1:1-swap treatment rather than a report line specific to each of the three individually) | **Partially complete** — shell verified clean live; inner conditional content verified clean via code comparison only, never live-rendered |
| A11 — Tool-call visualization | `ToolCallRow` (`App.jsx:20-53`) | Structure unchanged; every hex color swapped 1:1 for its token equivalent | **Direct code comparison only** (task-3-report.md:173: "kept its exact structure... swapped 1:1... byte-identical") | Never rendered in any live check — no tool call has ever fired in this dev environment (same root cause as A10/A9's gap). The code-comparison method is a real, explicit verification (not a bare "it's imported" claim), but it is not a live-render confirmation. | **Partially complete** — verified clean via code comparison; never live-rendered, named explicitly rather than assumed equivalent to a live check |
| A12 — Human-in-the-loop approval experience | `ApprovalCard` (`App.jsx:113-198`) | Outer border/buttons (`:118-125,138-193`) deliberately kept as untouched local one-offs, byte-for-byte identical to pre-Milestone-1 code (confirmed by diff, not by inference); only its `<textarea>` moved to the shared `TextArea` (`:163-169`) | **Untouched portion: verified clean via the strongest form of code comparison** — the lines are provably character-for-character unchanged (git diff shows zero changes to `App.jsx`'s `ApprovalCard` border/button style objects across all of Milestone 1). **`TextArea` portion: not live-rendered** — `approval_required` never fires in this environment (requires an Orchestrator planning call that succeeds with `requireApproval:true`), verified only via code comparison (task-3-report.md:181-189, confirming its `style` override — `padding: '6px 8px', fontSize: 12` — reproduces the original textarea's exact padding/fontSize) | The whole card, textarea included, has never been visually exercised live end-to-end. The color-token literal swap on the buttons (background/color values, not layout) was checked by direct comparison, not live. | **Partially complete** — the deliberately-untouched portion is verified clean by the strongest available method (unchanged-bytes diff); the migrated textarea portion is verified clean via code comparison only, never live |
| A13 — Manager/reconciliation synthesis experience | `ManagerReviewCard` (`App.jsx:200-226`) | `<Panel accentStatus>` (`:213`) + `<StatusBadge borderAlphaSuffix="60" label=...>` (`:216`), reusing `AgentCard`'s already-verified color/padding/alpha values | **Direct code comparison only**, explicitly stated as such in the source report: task-3-report.md:92,94 — "**Not independently live-exercised** — in this environment the Orchestrator's planning step fails before any `manager_review_started` event fires, so `ManagerReviewCard` never rendered in either the baseline or after-fix live run... Verified by code/mechanism instead" | Never rendered in any check, in either the pre- or post-Milestone-1 state — `manager_review_started`/`manager_review_completed` require every agent to complete successfully first, which never happened in this environment | **Partially complete** — verified clean via explicit code comparison to the already-live-verified `AgentCard` badge pattern; zero live exercise of this specific component, named plainly in the source report itself |
| A14 — Final result presentation | Final-result `Panel` + `ReactMarkdown` overrides (`App.jsx:696-719`) | `<Panel style={{padding:'20px 24px'}}>` (`:699`); markdown `h1`/`h2`/`h3`/`code`/`pre`/`a` color literals swapped 1:1 for `colors.*` tokens (`:703-712`) | **Panel chrome: direct code comparison only** (task-3-report.md:95: "Not independently live-exercised — the task fails before any `finalResult` is ever set... Verified by code/mechanism: identical `Panel` component, identical `style`-merge-last mechanism proven pixel-exact by [the `AgentCard`/`ManagerReviewCard` Panel check]"). **Placeholder text: live, pixel-verified** — the "Submit a task to see results here." state (`:722-724`) was diffed with 0-pixel difference (task-3-report.md:100) | The actual rendered markdown (headings, code blocks, links, and — relevant to A15 — tables) has never been visually exercised with real content in any check, since `finalResult` is never set in this environment. Its color-token swap was checked by direct comparison only. | **Partially complete** — the empty/placeholder state is verified clean live; the populated markdown-rendering state (the part A15's `remark-gfm` work will actually touch) has never been exercised by any method beyond a static color-literal comparison |
| A16 — Memories experience | `MemoriesPage` (`App.jsx:228-306`) | Eyebrow → `<Label>` (`:258`); error → `<StateText tone="error">` (`:263`); empty/loading → `<StateText tone="muted">` (`:268,270`); table markup untouched, cell colors tokenized (`:274-296`, fixed in the whole-branch-review cleanup, commit `8d9725d`) | **Eyebrow/error/empty states: live, pixel-verified** — task-3-report.md:97 ("color and font-weight confirmed exact... visually and by color/weight sampling"), :99 (error banner "0 pixels with any diff... across 52,700 pixels checked"), and the "No memories saved yet" empty state was confirmed live in this session's own final controller verification screenshot. **Table cell colors: direct code comparison**, explicitly cross-checked against `tokens.js` and `ObservabilityPage.jsx`'s equivalent mapping to resolve a real ambiguity (`#1e1e2e` → `colors.base`, not `colors.mantle` — task-final-cleanup-report.md:26) | The populated `<table>` with real memory rows has never rendered in any check — no agent has ever completed successfully in this environment, so no memory has ever actually been saved to trigger it. Verified only via the tokens.js cross-check above, not a live render. | **Partially complete** — eyebrow/loading/empty/error states verified clean live; the populated table's cell styling verified clean via direct code comparison only, never live-rendered |
| A17-A20 — Observability dashboard, timeline, cost, error views | `ObservabilityPage.jsx` (614 lines, all 5 sub-views) | Full migration onto `Panel`/`Label`/`StatusBadge`/`NavItem`/`StateText`, tokens throughout (zero remaining hex, confirmed by grep on the merged `main`) | **Live, pixel- and computed-style-verified, extensively, including with real populated data** — task-4-report.md's live-verification section screenshotted all 5 sub-views against baseline, then went further: selected a real `Failed` task in Timeline/Summary (confirmed `StatusBadge`, `SpanRow`'s Gantt bar/dot render correctly against live API data) and two real tasks in Compare (confirmed `ComparisonSide` end-to-end) — screenshots preserved at `/tmp/m1-verify/task4/03b/04b/07b-*-with-data.png`. The Error Rates view was confirmed against a genuinely populated `Agent Error Rates` table (`Orchestrator 6/6/100%`, from real accumulated Failed-task data), not just an empty state. | Only the same accepted 2px `Label` position offset applies here (already dispositioned, §9). One narrower exception, named for consistency with how the same gap is treated elsewhere in this table: the Cost Dashboard sub-view only ever live-rendered its *empty* state (`$0.000000` / `0` executions / "No data in this range.", task-4-report.md:116) — its behavior with genuinely populated cost figures rests on the same component/token pattern already verified live for the sibling `SummaryView`/`ErrorRateTable`, not a populated-state live check of its own. This is the single most thoroughly live-verified area of the four page files overall, because Observability doesn't require a successful agent run to have real data to render (unlike A9-A16 above). | **Complete / verified clean** |
| A21 — Evaluation UI (suites/runs/results/regression/post-hoc) | `EvalsPage.jsx` (513 lines, all 4 sub-views) | Full migration onto the same components; three `Button`-based actions given an explicit unconditional-color override to preserve the "never dims when disabled" original behavior (`buttonStyle` had no disabled state) | **Live, computed-style-verified for the great majority**, including two genuinely-triggered error paths: `regressionError` was live-triggered (selecting a run with no baseline set) and measured exact (task-5-report.md:50); both `Run suite` and `Submit post-hoc scoring request` buttons were measured in *both* disabled and enabled states via `getComputedStyle`, confirming the color-stability fix actually works (task-5-report.md:211-225) | Two specific, named exceptions: `resultsError`'s path was not independently re-triggered in the final fix pass (task-5-report.md:61) — verified via code comparison to the sibling `regressionError` pattern instead; `Refresh summary`'s disabled/enabled color-stability claim rests on code comparison only, since this dev environment has no completed eval traces to post-hoc-score against and that button never actually receives a `disabled` prop to test either way (task-5-report.md:231) | **Partially complete** — the large majority (including two real error paths and two of three buttons' disabled/enabled behavior) is verified clean live; two specific, named sub-paths (`resultsError`, the `Refresh summary` button) are verified via code comparison only |
| A26 — Final cross-page UX consistency pass | N/A — not started | N/A | N/A | Cannot start until A22-A25 (this milestone) land, per the backlog's own definition of A26 | **Still requiring implementation** |

**Summary of the downgrade from the reviewer's original high-level conclusion:** the independent
reviewer's original assessment ("all three page files heavily use the M1 component system... the
claim does not overstate") was correct as far as it went — every import/usage claim it checked
was real. But "imported and used" is not the same claim as "verified clean," and applying that
distinction here downgrades **7 of the 9 assessed rows** (A9, A10, A11, A12, A13, A14, A16) from
an implicit "done" to **explicitly Partially complete** — each with a precisely named,
non-hypothetical gap (a specific render path that has never fired in this environment, verified
only by direct code comparison rather than a live render). Only **A17-A20** (Observability) earns
an unqualified **Complete / verified clean**, because it's the one area that doesn't depend on a
successful agent run to have real data to render against. **A21** (Evals) is Partially complete
but only barely — two narrow, specifically-named sub-paths, not a broad gap. **A26** is
correctly **Still requiring implementation** (it hasn't started, by the backlog's own
definition). None of this changes Milestone 2's own scope (§2) — A9-A21/A26 are not Milestone 2
items regardless of their exact completion depth — but it is a materially more honest picture
than "already substantially done" for anyone scoping a future Milestone 3.

### 3.2 — Application shell and identity (A3)

**[Fact]** `frontend/index.html:7` still reads `<title>frontend</title>` (the Vite scaffold
default) — a small, concrete gap directly within A3's "application shell" scope.

**[Fact — carried forward from the backlog, not yet addressed by this plan]**
`docs/product-completion-backlog.md:92`'s A3 row records a Milestone 1 finding: the documented
`'JetBrains Mono', 'Fira Code', monospace` font-stack (`DESIGN.md:157-158`) is not loaded as a
webfont anywhere — `index.html` has no font `<link>`, `frontend/public/` contains only
`favicon.svg`/`icons.svg` (both re-confirmed unchanged on the merged `main`). A visitor without
either font installed silently sees generic system monospace instead of the documented identity.
This was explicitly left open ("tracked here for later review") rather than resolved in
Milestone 1, and — unlike the other four Milestone 1 findings assessed in §9 — it sits squarely
inside A3, which **is** in Milestone 2's scope. It was not previously carried into a Milestone-2
disposition; doing so now: **this needs an explicit decision, added to §8**, not left silent. Two
considerations: (1) §13's own A7 Definition of Done says the first-run screen must "establish
OrchestAI's identity" — an identity that depends on which font actually renders; (2) loading a
webfont is a small, genuinine scope addition (a `<link>` tag or a self-hosted font file), not
free, and wasn't part of any prior estimate.

### 3.3 — Routing (A4)

**[Fact]** `App.jsx:324`: `const [view, setView] = useState('playground')`. Four possible values
(`'playground'`/`'memories'`/`'observability'`/`'evals'`), driving a plain conditional render
(`App.jsx:610-729`). Zero URL involvement anywhere — no `window.location`, `history.pushState`,
or routing library reference in the entire codebase (confirmed via grep).

**[Fact]** `ObservabilityPage.jsx:594` (`useState('timeline')`) and `EvalsPage.jsx:477`
(`useState('suites')`) hold their own independent, page-local "sub-view" state for their
internal tab bars (Timeline/Summary/Cost Dashboard/Error Rates/Compare, and Suites/Run/Results/
Post-Hoc respectively) — these reset to their default every time the page unmounts (i.e., every
time you navigate to a different top-level area and back), since nothing persists them.

**[Fact]** No routing library exists in `frontend/package.json` (`react`, `react-dom`,
`react-markdown` are the only three runtime dependencies — confirmed unchanged from Milestone
1's own re-confirmation, `docs/product-completion-backlog.md:52-53`).

**[Fact]** `frontend/vercel.json` already has a SPA-fallback rewrite rule:
`{"source": "/(.*)", "destination": "/index.html"}`. **This means the production deployment
already correctly serves `index.html` for any path** — the "hard refresh on a client-side route
returns 404" failure mode that routing libraries usually need special hosting config to avoid is
already solved for the Vercel deployment, before any routing code is even written. Vite's dev
server defaults to SPA-fallback behavior (`appType: 'spa'`) as well; `vite.config.js` doesn't
override this, so local dev refresh-on-any-path should already work too — worth a concrete,
live check during implementation (not assumed), but not a blocker requiring new config.

### 3.4 — Responsive design (A5)

**[Fact]** Zero media queries in `App.jsx`, `ObservabilityPage.jsx`, or `EvalsPage.jsx`
(re-confirmed via grep against the merged `main` — unchanged from the Milestone 1 finding).

**[Fact]** `DESIGN.md` and `.impeccable/design.json` specify **zero** breakpoint values —
`.impeccable/design.json:36` still reads `"breakpoints": []`. Unlike Milestone 1's tokens (which
were extractions of an already-fully-specified system), **there is no existing design-system
breakpoint spec to extract here — any concrete breakpoint values this milestone uses are a new
design decision, not extraction**, and need explicit approval (§8).

Concrete failure points identified by reading the actual layout code, not by assumption:

| Location | Current layout | Where it breaks |
|---|---|---|
| `App.jsx:617` (Playground) | `display: 'grid', gridTemplateColumns: '380px 1fr'` | Fixed 380px left column; unusable right pane below roughly 700–800px total viewport width |
| `App.jsx:576` (Header) | `display: 'flex'` row: logo+subtitle, `Nav`, right-aligned task-status area — no `flexWrap` | Overflows horizontally with no wrap once the combined content exceeds the viewport width (no explicit threshold, but well before tablet-portrait widths once a task is active and the status/cost area appears) |
| `ObservabilityPage.jsx:285,292` (`SummaryView`) | `display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)'` (two separate 4-stat grids) | Four equal columns become too narrow for their content well before common tablet widths |
| `ObservabilityPage.jsx:528` (`ComparisonSide`) | `display: 'grid', gridTemplateColumns: '1fr 1fr'` | Two full side-by-side task panels; the least mobile-friendly view in the app — already covered by A5a's deferral for genuinely dense/phone-width optimization, but still relevant at tablet widths |
| `EvalsPage.jsx:490` | `maxWidth: 1100` (no responsive floor) | Already somewhat shrink-friendly (has a max-width cap, not a fixed width) — the least concerning of the set |

**[Fact]** `index.html:6` already sets the viewport meta tag correctly
(`width=device-width, initial-scale=1.0`) — this specific prerequisite is already in place,
confirmed not something Milestone 2 needs to add.

**[Fact]** `frontend/src/index.css`'s leftover (deliberately not-yet-touched, per Milestone 1's
own deferred findings) `h1`/`h2` rules already carry an incidental
`@media (max-width: 1024px) { font-size: ... }` override — an accidental leftover from the
original Vite template, not a deliberate responsive strategy. Any Milestone 2 responsive work
needs to be aware this rule already exists and already affects `App.jsx`'s `<h1>` and
`ApiKeyPrompt.jsx`'s `<h2>` at exactly 1024px — a coincidence worth being deliberate about
(harmonize with it, or supersede it consciously) rather than colliding with it unknowingly.

### 3.5 — First-run / product-entry experience (A7) and API-key management (A8)

**[Fact]** `ApiKeyPrompt.jsx` (current, post-Milestone-1, 38 lines): its `<input>`/`<button>`
were mechanically swapped onto `Input`/`Button` in Milestone 1 (narrow scope, as planned). The
outer `<div>` still carries `fontFamily: 'sans-serif'` (the one place in the entire frontend
breaking the mono-only rule), and the copy is still plain prose with zero product framing —
identical in substance to what Milestone 1's own investigation found, deliberately untouched.

**[Fact]** `App.jsx:568-570`: `if (!keySet) return <ApiKeyPrompt onSubmitted={...} />` — a full
takeover with no other content. Once past it, the very first thing rendered is the Playground's
empty state (empty form + `StateText`'s "Submit a task to see results here." placeholder,
`App.jsx:720-725`) — no orientation to the other three areas (Memories/Observability/Evals)
exists anywhere in the current first-run path.

**[Fact]** `frontend/src/apiKey.js` exports `getApiKey()` (line 28) and `clearApiKey()` (line
36) — both fully implemented, in-memory-only, but **still completely unused** anywhere in any
JSX file (grep confirmed zero call sites) — re-confirmed unchanged from the Milestone 1
investigation. There is still no way for a user to see which key is active, or to change/clear
it, without manually clearing browser/tab state.

**Already covered by Milestone 1's shared primitives (do not rebuild):** `Input`, `Button`,
`Label`, `Panel`, and `StateText` all exist and support a `style` override, `Input`/`TextArea`
support a `labelStyle` override — Milestone 2's A7/A8 work is UX/content/flow design using these
already-built primitives, not primitive-building. The `NavItem`/`Nav` components (already
consumed by `App.jsx`'s header) are the direct building block for whatever "orientation to the
other three areas" A7 ends up designing.

**Separating product/UX design from infrastructure, per the investigation's own instruction:**
the *infrastructure* (styled input, styled button, a nav component, a state-text component) is
100% done. What's still open is pure product/UX work: what the pre-key screen should actually
say, what the post-key landing state's primary action should look like, and how orientation to
Memories/Observability/Evals should be worded/laid out. This milestone plan does not pre-design
that content — it identifies that this is where the real work is (see §8's decisions).

### 3.6 — Loading/Empty/Error states (A22/A23/A24)

**[Fact]** Milestone 1's `StateText` component (`frontend/src/components/StateText.jsx`)
exists and is consumed everywhere a loading/empty/error text previously appeared — but Milestone
1 explicitly only did **style extraction** (preserving the exact prior colors/sizes), never
copy or UX redesign. Concretely still open:
- `MemoriesPage`'s "Loading…" (`App.jsx:268`) is still exactly that word, no skeleton/spinner.
- `EvalsPage.jsx`'s empty-suite copy ("No suites yet — create one via `POST /api/v1/eval-suites`",
  cited originally at `docs/product-completion-backlog.md:115`) is still raw developer-facing API
  hinting — confirmed unchanged in the current file.
- The Playground's default empty-result placeholder (`App.jsx:720-725`) is still plain text with
  no visual treatment beyond color/size.

**[Fact]** Error banners (`App.jsx:663-667`, `MemoriesPage`'s at `App.jsx:261-265`) are simple
colored `<div>` + `StateText` pairs — functionally fine, visually consistent with each other
already (both use the same `colors.errorBg`/`colors.statusFailed` pattern), but not
componentized into a shared "ErrorBanner" primitive — Milestone 1 left this as page-local markup
per its own explicit "Table/chart/markdown internals untouched beyond token references" scope
boundary (which doesn't literally cover this, but the same minimal-footprint principle was
applied consistently).

### 3.7 — Accessibility baseline (A25)

**[Fact]** Zero `aria-*`, `role`, `tabIndex`, or `:focus`/`focus-visible` treatment anywhere in
`frontend/src/components/*.jsx` — no actual attribute or CSS rule exists; grep hits are limited
to two explanatory code comments (`Input.jsx` noting the deliberate choice not to add a custom
focus ring in Milestone 1, and `Button.jsx` quoting DESIGN.md's "One Accent Rule," which mentions
"focus" only as one of the interactive affordances the rule covers).

**[Fact]** Nav items render icon+text as plain children (e.g. `🧠 Memories`,
`App.jsx:586-588`) with no `aria-label`; there is no `aria-current`/equivalent marking the active
nav item for assistive tech, even though the visual `active` state exists.

**[Fact]** `PRODUCT.md:55-59`'s accessibility section states "WCAG AA baseline: body text ≥4.5:1
contrast, fully keyboard-navigable. Reduced-motion support required on any future animation
work." Since `.impeccable/design.json:34-35`'s `shadows`/`motion` arrays are empty and DESIGN.md's
own stated philosophy is explicitly flat/no-motion, there is currently no animation anywhere to
need `prefers-reduced-motion` handling for — this only becomes relevant if Milestone 2 introduces
any transition/animation (e.g. a mobile nav drawer), which is not assumed here.

### 3.8 — `remark-gfm` (A15)

**[Fact]** `frontend/package.json` still has no `remark-gfm` dependency. `App.jsx:701-716`'s
`ReactMarkdown` usage is unchanged from Milestone 1. This is the one item in Milestone 2's scope
that **is** a small, self-contained dependency addition — flagged explicitly in §6/§8, not
assumed to be free of the "no dependency without justification" scrutiny just because it's small.

---

## 4. Relevant Files and Migration Surface

| File | Current state | Relevant to |
|---|---|---|
| `frontend/src/App.jsx` (732 lines) | Header/nav (A3/A6), view-switch state (A4), Playground grid (A5), key-gate (A7), loading/empty/error text (A22-A24), nav accessibility (A25), `ReactMarkdown` (A15) | A3,A4,A5,A6,A7,A15,A22,A23,A24,A25 |
| `frontend/src/ApiKeyPrompt.jsx` (38 lines) | Pre-key screen; sans-serif override untouched; zero product framing | A7 |
| `frontend/src/apiKey.js` (48 lines) | `getApiKey`/`clearApiKey` unused | A8 |
| `frontend/src/ObservabilityPage.jsx` (614 lines) | Own `SubNav` sub-view state; 4-col and 2-col grids; loading/empty text | A4 (sub-view routing question, §8), A5, A22-24 |
| `frontend/src/EvalsPage.jsx` (513 lines) | Own `SubNav` sub-view state; `maxWidth:1100`; developer-facing empty copy | A4 (same), A5, A22-24 |
| `frontend/src/index.css` | Post-Milestone-1: only the genuinely-dead rules removed; `h1`/`h2`/`code`/`:root`-vars/`#root` all still exactly as Milestone 1 left them | A5 (incidental `@media` collision, §3.4), possible §5 deferred-finding overlap |
| `frontend/src/theme/tokens.js` (231 lines) | Complete; no breakpoint/spacing-for-layout tokens exist (only component-level spacing) | Consumed by A5 if new breakpoint/layout tokens are added |
| `frontend/src/components/*.jsx` (7 files) | All support `style` overrides; `NavItem`/`Button`/`Input` etc. are the building blocks for A3/A7/A8/A25 | A3,A7,A8,A25 |
| `frontend/index.html` | `<title>frontend</title>` (Vite default), correct viewport meta | A3 (shell identity) |
| `frontend/vite.config.js` | No aliases, no routing-related config | A4 (only if a routing library needs config; not needed for a custom solution) |
| `frontend/vercel.json` | SPA-fallback rewrite already present | A4 (de-risks refresh/deep-link behavior) |
| `frontend/package.json` | No routing lib, no `remark-gfm` | A4 (dependency decision), A15 (dependency addition) |

---

## 5. Proposed Architecture

**[Recommendation — each numbered point below is flagged again in §8 as needing explicit
approval; nothing here is decided.]**

### 5.1 Routing (A4/A6)

Given: no existing routing library, a genuinely small/flat navigation need (4 top-level areas,
no nested layouts, no route-level data loading beyond what each page already fetches itself),
`vercel.json`'s SPA-fallback already solving the refresh/deep-link hosting concern, and this
project's consistent preference for zero-dependency solutions when the need is small (Milestone
1 added zero dependencies for a comparably-sized problem).

**Recommended: a small, custom hook using the native `history`/`popstate` API** (no routing
library) — mapping the four top-level paths (`/`, `/memories`, `/observability`, `/evals`) to
the existing `view` state, calling `history.pushState` on navigation and reading
`window.location.pathname` on load/`popstate`. This is a well-understood, small (rough estimate:
30-60 lines) pattern for exactly this shape of need, and avoids taking on a general-purpose
routing library's API surface (nested routes, loaders, code-splitting) for a problem that
doesn't have any of those requirements.

**Alternative considered:** a lightweight routing library (e.g. `wouter`, far smaller than
`react-router`). Only worth it if the sub-view routing question below (5.1a) is answered
"yes, nested routes for sub-tabs too" — at that point a library's nested-route matching starts
to earn its weight. Flagged as the fallback if the custom-hook approach turns out awkward once
actually attempted, not preemptively adopted.

**5.1a — Sub-open decision:** should `ObservabilityPage`'s and `EvalsPage`'s internal `SubNav`
tabs (Timeline/Summary/Cost Dashboard/Error Rates/Compare, and Suites/Run/Results/Post-Hoc) also
become their own addressable URLs (e.g. `/observability/timeline`), or does A4's "URL-addressable
views" requirement stop at the four top-level areas? **Recommendation:** top-level only for this
milestone — the backlog's own A4 justification (`:93`, "no deep links, no bookmarking, no
back-button support") is phrased entirely in terms of the four top-level views, not the internal
sub-tabs, and going further adds real complexity (would need to encode selected-task-ID state in
the URL too, for `TimelineView`/`SummaryView`/`ComparisonSide`) for a benefit that wasn't named as
a requirement. Sub-tab routing is a clean, separable enhancement if wanted later, not required to
satisfy A4 as written.

### 5.2 Responsive design (A5)

**[Recommendation]** Since no breakpoint values exist anywhere in the design system, propose two
breakpoints, both aimed at "common laptop/tablet widths" per A5's own stated bar (not phone
widths, which A5a defers):
- **~1024px** — collapses `ObservabilityPage`'s 4-column stat grids to 2 columns, and is where
  the incidental `index.css` `h1`/`h2` media query already sits (see §3.4) — reusing this exact
  value avoids introducing a second, different breakpoint that fights the accidental existing one.
- **~768px** — collapses the Playground's `380px 1fr` grid to a single stacked column (form on
  top, results below), and wraps the header row (`flexWrap: 'wrap'` or an explicit stacked
  layout) instead of overflowing.

These are proposed starting values, not extracted facts — flagged for approval (§8), and
expected to need visual iteration once actually built and viewed at real viewport sizes, the
same way Milestone 1's screenshot-driven verification caught things a spec alone couldn't.

**Styling mechanism:** continue the existing inline-`style`-object convention (no CSS Modules/
CSS-in-JS/Tailwind — consistent with Milestone 1's explicit architecture decision and this
codebase's only styling mechanism). Media queries can't be expressed in a plain inline `style`
object, so this necessarily means either (a) a small `useViewportWidth()`-style hook that
recomputes layout props in JS based on `window.innerWidth`/a resize listener, or (b) introducing
scoped CSS classes for just the handful of properties that need to respond to width (grid columns,
flex-wrap), leaving everything else as-is. **Recommendation: (a)**, a small hook — keeps the
"zero CSS files beyond the two already-existing global ones" pattern Milestone 1 established,
at the cost of computing layout in JS rather than in the browser's own CSS engine. Flagged as a
real architecture choice needing approval, not a foregone conclusion.

### 5.3 App shell (A3) and first-run/A7/A8

**[Recommendation]** The shell itself (header, nav, title) is mostly a restyle/reflow exercise
using components that already exist — lower risk. A7/A8 is genuine product design work (what the
first-run screen says, what "orientation to the other three areas" looks like, what an API-key
management affordance looks like) — this plan does not pre-design that content; it flags that a
short, focused design pass (even informal — a few sentences of copy, a rough layout sketch) should
happen before an implementer is dispatched, so the implementer isn't asked to invent product copy
on the fly the way no prior Milestone 1 task ever was.

### 5.4 Loading/Empty/Error/Accessibility (A22-A25)

**[Recommendation]** Extend the existing `StateText` component (already built, already
consuming tokens) rather than building new primitives — e.g. a `tone="loading"` variant if a
spinner/skeleton treatment is wanted, and copy changes applied directly at each call site (no new
component needed for better copy, just better words). Accessibility: add `aria-current="page"`
(or equivalent) to the active `NavItem`, `aria-label`s to icon-bearing nav items, and a visible
`:focus-visible` treatment — all additive changes to already-built components, not new
architecture.

---

## 6. Dependencies

| Candidate | Verdict | Reasoning |
|---|---|---|
| Routing library (`react-router`, `wouter`, etc.) | **Not recommended by default** | See §5.1 — the custom-hook approach is proposed first; only reconsider if sub-tab routing (5.1a) is approved and the custom approach proves awkward in practice. |
| `remark-gfm` | **Recommended, small, in-scope** | A15 explicitly names this as Milestone 2 scope; it's a single, narrow, already-anticipated dependency (flagged back in the original product-completion investigation), not new scope creep. Still requires explicit approval per the "no dependency without justification" rule — justification here is that A15 is a named backlog MUST item specifically about this gap. |
| Any CSS framework / CSS-in-JS / Tailwind | **Not proposed** | Consistent with Milestone 1's explicit architecture decision; nothing in Milestone 2's scope requires it. |
| A viewport/media-query utility library | **Not proposed** | §5.2's small custom hook is proposed instead; revisit only if that approach proves genuinely inadequate once attempted. |

No other dependency is proposed. Both proposed dependency decisions (routing library — leaning
no; `remark-gfm` — leaning yes) require your explicit approval before implementation, per §8.

---

## 7. Implementation Sequencing

Proposed order (not yet approved, subject to §8's decisions changing shape/order):

1. **App shell foundation (A3/A6):** header/nav reflow onto existing components, `index.html`
   title fix (small, bundled here since it's the same "shell identity" concern).
2. **Routing (A4):** the custom history-based hook, wiring the four top-level views to real URLs;
   verify refresh/back/forward/deep-link behavior live before moving on.
3. **Responsive layout (A5):** apply breakpoints to the Playground grid, header wrap, and
   Observability's stat/compare grids; verify at real viewport sizes via live screenshots, the
   same discipline Milestone 1 used.
4. **`remark-gfm` (A15):** small, isolated, can land anytime after dependency approval — no
   ordering dependency on the rest.
5. **Loading/Empty/Error copy and treatment (A22-A24):** revise copy at each call site,
   extend `StateText` if a richer tone is approved.
6. **Accessibility baseline (A25):** ARIA/focus-visible pass across `NavItem` and other
   interactive components — do this after shell/routing land, since nav structure changes in
   steps 1-2 would otherwise need a second accessibility pass.
7. **First-run/product-entry (A7) + API-key management (A8):** last, since it benefits from the
   shell/nav/routing decisions above being settled (orientation content references the other
   three areas, which should already be reachable via real routes by this point).

This groups the more mechanical/infrastructure-shaped items (1-4) before the more product-design-
shaped items (7), consistent with §5.3's separation of infrastructure from design work.

### 7.1 — A4/A7 dependency, made explicit

Raised by independent review: the ordering above already places routing (step 2) before
first-run/product-entry (step 7), but the plan did not previously say *why* in dependency terms,
or *how* A7's orientation content will actually invoke navigation. Both are stated explicitly
here rather than left implicit.

**A7 formally depends on A4 being complete before it is implemented.** Not a scheduling
preference — a real dependency: A7's Definition of Done (§13) requires "orientation to the
Playground, Memories, Observability, and Evaluations areas" from the post-key landing state.
Orientation that isn't genuinely navigable (i.e., that doesn't actually take the user to a real,
addressable location) would not satisfy that requirement — it would just be decorative text
naming the other three areas.

**Mechanism:** A7's orientation elements are not a separate, ad hoc set of links. They are built
using the same navigation primitive A4 produces — whatever `NavItem`/`Nav` (or the new
routing hook's exposed `navigate(path)` function, once A4 decides its exact shape per §8 item 1)
already uses to move between top-level views and update the URL. Concretely, this means: if A4
lands using the existing `NavItem` component wired to the new routing hook, A7's orientation UI
should call that same hook/function (e.g., a set of `NavItem`-styled or plain-button elements
whose `onClick` calls the identical `navigate('/memories')` the header nav itself calls) —
**not** raw `<a href="/memories">` tags that would trigger a full page reload, and not a second,
parallel navigation mechanism. This also means A7 cannot be meaningfully implemented, let alone
verified, until A4's routing hook exists and its exact API is settled.

**Consequence for A8:** API-key management (bundled with A7 in step 7 above) has **no** such
dependency on A4 — it only needs `Input`/`Button`/`apiKey.js`'s existing `getApiKey`/`clearApiKey`
(all already built). It is sequenced alongside A7 for convenience (same file, same step), not
because it needs routing — if useful during implementation, A8 could be pulled earlier or done
independently without violating any dependency.

---

## 8. Decisions — Final (approved)

All ten decisions below are approved as final for Milestone 2. None is reopened here.

1. **Routing implementation — APPROVED.** Custom `history`/`popstate` hook, no new dependency.
   Do not add a routing library.
2. **Sub-tab routing scope (5.1a) — APPROVED.** Top-level views only. Do not introduce URL
   routing for selected task IDs or Observability/Evals' internal sub-tabs in Milestone 2.
3. **Responsive breakpoint values — APPROVED.** The proposed ~1024px / ~768px values (§5.2) are
   the initial implementation baseline, with visual iteration during implementation if the real
   layouts demonstrate a need to adjust them.
4. **Responsive styling mechanism — APPROVED.** The JS viewport-width hook approach (§5.2), kept
   consistent with the plan. Do not introduce a new styling framework or dependency.
5. **`remark-gfm` dependency addition — APPROVED.** Add the one package and wire it into the
   existing `ReactMarkdown` usage as specified in §5/§13.
6. **The `index.css` `h1`/`h2` typography-leak finding — APPROVED TO FIX NOW**, because A3/A7
   are already touching the affected shell/onboarding areas. Scope, precisely: correct the
   unintended `index.css` heading font-family/letter-spacing leakage onto `App.jsx`'s `<h1>` and
   `ApiKeyPrompt.jsx`'s `<h2>`, aligning both with the intended mono-only typography/token system.
   This is an explicitly approved correction of a known Milestone 1 deferred defect — not a new
   design decision, and **not** an invitation to expand into unrelated typography or webfont
   work (see decision 10, still deferred). §9's disposition table is updated below accordingly.
7. **The `EvalsPage` `<code>` chip styling finding — DEFER.** Left unchanged in Milestone 2.
   Milestone 2 does not otherwise touch `EvalsPage.jsx`; this stays deferred to the later
   milestone already identified in §9, independent of decision 6 above (the two are **not**
   bundled, despite sharing the same underlying `index.css` rule family).
8. **A7/A8 product content — BEFORE IMPLEMENTATION, NOT YET APPROVED.** Implementation of A7/A8
   does not begin until a concrete content/copy/layout proposal is produced and separately
   approved. That proposal is §8a below — its own approval gate, distinct from this document's
   approval as a whole.
9. **Loading-state richness (A22) — APPROVED, lightweight option.** Improved/clearer copy plus
   consistent styling via the existing shared primitives/tokens (`StateText` and friends). No new
   skeleton system, no spinner framework, no animation infrastructure.
10. **Webfont loading for A3's identity (§3.2) — DEFER.** Do not add, self-host, or load
    `'JetBrains Mono'`/`'Fira Code'` in Milestone 2. Current system-monospace fallback behavior
    stays unchanged, tracked for the later milestone already identified in `docs/product-completion-backlog.md`'s A3 row.

---

## 8a. A7/A8 Concrete Content Proposal (PENDING APPROVAL — separate gate from §8)

This is a proposal, not yet approved. Implementation of A7/A8 does not begin until this section
is separately approved (§8 decision 8). Scoped strictly to A7/A8 per instruction — this is copy
and one small affordance, not a product redesign. No new screens, routes, or components beyond
what §5.3 already specifies; it fills in the actual words and the one missing piece of structure
(A8's key-management affordance) that §5.3 left unspecified.

**Voice constraints this proposal is held to** (from `PRODUCT.md`/`DESIGN.md`, both already
authoritative, neither reopened here): "an engineer's instrument panel, not a consumer product"
(`PRODUCT.md:26`); explicitly not "a marketing-adjacent product surface" (`PRODUCT.md:35`); "not
a dashboard you browse — it's a console you read" (`DESIGN.md:80`). Concretely: no adjectives
selling the product, no exclamation points, no "get started" filler — state what the tool is,
what it needs, and where to get it, in the same flat declarative register the rest of the app
already uses.

### 8a.1 — Pre-key screen (`ApiKeyPrompt.jsx`): identity + purpose

Current copy carries the honest in-memory/session disclaimer but establishes no identity and
gives no path to obtaining a key — confirmed gap, `product-completion-backlog.md:97`
("carries zero product framing... no context on what OrchestAI is or what the key is for").
Proposed copy (structure only changes by adding one subtitle line and one new paragraph; the
existing disclaimer paragraph is kept, substance unchanged):

```
OrchestAI
Multi-agent CQRS orchestration · .NET 8

An API key is required to run agents and query stored data. This instance has no self-service
signup — keys are provisioned out-of-band by an operator. If you don't have one, ask whoever
set up this deployment. Running it yourself locally: see the README's Quick Start
(scripts/bootstrap-local-dev.sh mints one).

Held in memory for this browser session only — never saved to disk, never sent anywhere
except as an Authorization header on requests to this API. Refreshing the page will require
re-entering it. This is a temporary development flow, not a production authentication design.

[key input]
[Continue]
```

Notes:
- The subtitle line (`Multi-agent CQRS orchestration · .NET 8`) is copied verbatim from the
  existing main-header subtitle (`App.jsx:579`), not invented — same identity string pre- and
  post-key, one source of truth for what the tagline says. **Rendered font is not identical,
  and this proposal does not change that:** decision 6 fixes only the `index.css` `h1`/`h2`
  element rule, so `ApiKeyPrompt`'s `<h2>` headline becomes mono, but its wrapper `<div>` still
  carries the deliberately-deferred inline `fontFamily: 'sans-serif'` (`ApiKeyPrompt.jsx:16`,
  §3.5/`product-completion-backlog.md` A7 row) — a `<p>` has no element-level CSS rule of its
  own, so the subtitle and body-copy paragraphs still inherit that sans-serif from the wrapper,
  same as today. Giving the subtitle its own inline mono override would be new typography scope
  beyond decision 6's explicit boundary ("not an invitation to expand into unrelated typography
  or webfont work"), so this proposal accepts the same text rendering in two different fonts
  pre- vs. post-key as a pre-existing, separately-tracked limitation, not something 8a fixes.
- The provisioning path (`scripts/bootstrap-local-dev.sh`, admin-bootstrap-endpoint model) is
  copied from the README's own already-documented Quick Start (`README.md:80-82, 149`), not a
  new claim about how auth works — this is describing the existing model, not changing it.
- No claim here is new backend behavior; §0 of the backlog (auth model closed to API-key-only)
  is unchanged.

### 8a.2 — Post-key landing/orientation content

The backlog is explicit that this is *not* a new screen: "no dedicated in-app landing screen is
required beyond what the existing four-area nav (`App.jsx:610-651`) already provides once
labeled/oriented properly... The Playground's default empty-result state is the same first-run
path and is addressed jointly with A23" (`product-completion-backlog.md:97`). Consistent with
that, and with §7.1's requirement that any orientation element route through A4's mechanism
rather than a raw link or a parallel one: this proposal adds no new clickable navigation
element. The nav bar (`Nav`/`NavItem`, already live and already the single navigation
mechanism after A4 lands) is the actual way a first-run user reaches Memories/Observability/
Evals; the orientation copy below only *names* those areas in prose, it does not duplicate the
nav with new links.

The one existing text slot that currently gives zero orientation is the Playground's
default (pre-submission) empty-result placeholder (`App.jsx:723`, current text: `'Submit a task
to see results here.'`). Proposed replacement (this is the A22/A23 lightweight-copy work,
§8 decision 9, applied to this specific slot — not separate new scope):

```
Fill in a task on the left and click Run Agents. Per-agent cards will stream in as they work,
and the final result renders here when they finish.

Once you've run a task or two: Observability has the cost and timeline breakdown for every
run, and Evals runs scored test suites against known-good outputs.
```

(The `isActive` branch — `'Waiting for agents to complete…'` — is unchanged; this only touches
the true first-run/idle default.)

### 8a.3 — API-key management affordance (A8)

`apiKey.js` already exports `getApiKey`/`clearApiKey`, fully built, currently unused in any
JSX (confirmed by grep) — this is the exact gap A8 closes. Proposed: a small text affordance in
the header's right-hand region, always visible (not conditional on an active task), reading
`Change key`. On click: `clearApiKey()` then reset the `keySet` state to `false`, which the
existing `if (!keySet)` gate (`App.jsx:568-570`) already routes back to `ApiKeyPrompt` with no
new logic required beyond the click handler itself. No part of the actual key value is ever
displayed — consistent with `apiKey.js`'s own never-persist/never-expose posture. Styling:
reuse the existing small muted-text treatment already used elsewhere in the header (e.g. the
task-id/cost text at `App.jsx:599-604`), not a new visual pattern.

This is the only new interactive element this proposal introduces, and it is a local state
reset, not a route change — it does not implicate §7.1's routing-dependency requirement.

### 8a.4 — Layout/structure summary

No structural change beyond what's stated above:
- `ApiKeyPrompt.jsx`: same centered-card layout (~480px max-width), same components (`Input`,
  `Button`); copy revised as in 8a.1; the `<h2>` headline becomes mono as a side effect of §8
  decision 6 (the `index.css` h1/h2 fix), not a separate change made here — the subtitle and
  body paragraphs remain sans-serif per the wrapper's still-deferred inline override, as noted
  in 8a.1.
- `App.jsx` header: one new small text affordance (8a.3) added to the existing flex header row,
  no new row or section.
- Playground empty-state placeholder: copy-only change (8a.2), same `StateText` component, same
  position.

---

## 9. Deferred Milestone 1 Findings — Current Disposition

Per the investigation instruction, each of the five is assessed on whether it belongs in
Milestone 2 specifically — not pulled in by default just because it was discovered earlier.

| Finding | Lives in | Touched by Milestone 2's actual scope? | Disposition |
|---|---|---|---|
| `ApprovalCard` green/red vs. One Accent Rule | `App.jsx`'s `ApprovalCard` | No — Milestone 2 doesn't touch `ApprovalCard`'s content (already restyled in Milestone 1, per §3.1; further redesign is Milestone 3/A26 territory) | **Stays deferred beyond Milestone 2.** |
| `h1`/`h2` typography leak (`index.css`) | `App.jsx`'s `<h1>`, `ApiKeyPrompt.jsx`'s `<h2>` | **Yes** — A3 and A7 are about to edit these exact elements | **Approved to fix now, §8 decision 6.** No longer deferred — corrected as part of Milestone 2's A3/A7 work, scoped narrowly to font-family/letter-spacing only (not a webfont addition, decision 10 stays deferred separately). |
| `EvalsPage` `<code>` chip styling (`index.css`) | `EvalsPage.jsx:57` | No — Milestone 2 doesn't touch `EvalsPage.jsx` at all | **Stays deferred**, §8 decision 7 — explicitly not bundled with the `h1`/`h2` fix above, despite the shared underlying rule family. |
| Status-accented card border alpha (`AgentCard`/`ManagerReviewCard`) | `App.jsx` | No — not part of A3/A4/A5/A6/A7/A8/A15/A22-25 | **Stays deferred beyond Milestone 2** (likely Milestone 3/A26). |
| `Label` 2px vertical position offset | `Label.jsx`, consumed everywhere | No — Milestone 2 doesn't plan to touch `Label.jsx`'s internals; the one prior fix attempt made things worse | **Stays accepted as-is.** No new attempt proposed. |

---

## 10. Explicit Non-Goals

Restated plainly, per the investigation's instruction to name what Milestone 2 is **not**:

- **No backend/API changes of any kind.** Every item in scope is a pure frontend concern.
- **No auth-model changes.** The API-key model stays exactly as `docs/product-completion-backlog.md §0` locks it — A8 is a UI affordance for the *existing* mechanism (see/clear the
  in-memory key), never a new auth mechanism, session, or cookie.
- **No unrelated feature work.** Nothing beyond A3,A4,A5,A6,A7,A8,A15,A22,A23,A24,A25.
- **No table/chart componentization.** `MemoriesPage`'s table, `ObservabilityPage`'s
  `DashboardView`/`ErrorRateTable`/`BarChart`/`SpanRow`, and `EvalsPage`'s tables are untouched —
  not part of this milestone's named scope (they were never Table/chart *component* work even in
  Milestone 1, and nothing here reopens that boundary).
- **No true mobile/phone-width optimization of dense views** (A5a, explicitly `DEFER`) — A5's
  responsive work targets common laptop/tablet widths only, per the backlog's own framing; phone
  widths for the Gantt timeline, cost tables, and comparison view are out of scope here.
- **No accessibility work beyond the stated baseline (A25).** No full WCAG AA
  audit/certification (A25a, explicitly `DEFER`).
- **No unrelated design-system changes.** `tokens.js`, `Panel`, `StatusBadge`, etc.'s existing
  values are not touched except where a genuine new need (e.g. a breakpoint token) arises — and
  even then, additive only, per Milestone 1's own established pattern of never modifying an
  already-approved default silently.
- **No Milestone 3 functionality** (A9-A21 restyling is already done per §3.1; A26 explicitly
  waits for this milestone to finish first).
- **No packaging/release work** (§D of the backlog — completely unrelated track).
- **No re-opening of the four/five deferred Milestone 1 findings**, with exactly one approved
  exception: the `h1`/`h2` typography-leak fix (§8 decision 6). That fix is scoped narrowly to
  font-family/letter-spacing correction — it is **not** license to also load a webfont (§8
  decision 10 keeps that deferred), touch `EvalsPage`'s `<code>` chip (§8 decision 7, deferred
  separately despite the shared `index.css` rule family), or reopen any of the other three
  deferred findings.

---

## 11. Testing and Verification Strategy

No automated frontend test framework exists (confirmed unchanged — still no test script in
`package.json`, consistent with every prior milestone's finding); verification continues to rely
on `npm run build`/`npm run lint` plus live, driven manual verification — the same discipline
established across all of Milestone 1's tasks (real screenshots, real `getComputedStyle` checks,
not eyeballing).

### 11.0 — Re-confirmed before Milestone 2 implementation: the Anthropic-key limitation persists

Checked directly, without fabricating or creating any credential: no `.env` file exists at the
repo root; no `ANTHROPIC_*` environment variable is set in this shell; both
`src/OrchestAI.API/appsettings.json` and `appsettings.Development.json` have an `Anthropic:ApiKey`
of zero-length. **The same limitation documented throughout Milestone 1 remains: no real
Anthropic call can succeed in this environment, so no task can reach a genuine `Completed` state,
and no `approval_required`/`manager_review_started`/`task_completed` event can ever fire.** This
governs the rest of this section and §13's Definition of Done:

- **Every state reachable without a successful agent run must be verified live** — this includes
  routing (all four top-level views, direct URL entry, refresh, back/forward), responsive layouts
  at every view, the first-run/API-key flow, the `Pending`→`Running`→`Failed` transition, and all
  of Observability/Evals (neither requires a successful run to have real data, per §3.1a's own
  finding).
- **Populated agent-dependent states that cannot be reached here must be verified through direct
  code comparison and static inspection, not asserted as live-verified.** This applies to
  whatever in Milestone 2's own scope touches those same unreachable paths (e.g., if `remark-gfm`
  table rendering is exercised via a manually-authored sample markdown string rather than a real
  `finalResult` from a completed task — see the `remark-gfm` verification note below — that is
  itself a form of static/direct verification and should be reported as such, not conflated with
  a live agent-driven render).
- **§13's Definition of Done does not claim any agent-dependent unreachable state was "verified
  live."** Anywhere it currently could be read that way, the wording is corrected below.
- **The final Milestone 2 completion report must state this limitation explicitly**, using the
  same "Complete / verified clean" vs. "Partially complete" (with the specific unverified aspect
  named) vs. "Still requiring implementation" vs. "Not yet assessed" classification standard
  §3.1a already established — not a vaguer "everything works" summary.

Proposed concrete flows for this milestone specifically:

- **Routing:** navigate to each of the four top-level views via the nav UI, confirm the URL
  changes; type each URL directly into the address bar (or `history.pushState` + reload via a
  driven browser session) and confirm the correct view renders on a cold load; use browser
  back/forward and confirm it moves between the actually-visited views correctly; hard-refresh
  on `/memories` (and the other non-root paths) and confirm no 404 (validates `vercel.json`'s
  rewrite in a real deployed-equivalent test, and Vite's dev-server default locally).
- **Responsive layouts:** resize/re-render at a small number of concrete widths bracketing the
  proposed breakpoints (e.g. 1440px baseline, ~1024px, ~900px, ~768px, ~700px) and screenshot
  each of the four top-level views plus Observability's Summary/Compare sub-views at each width;
  confirm no horizontal overflow/scrollbar at any tested width, and that the grid/flex changes
  actually kick in at the intended breakpoint.
- **First-run/API-key experience:** fresh load with no key set → confirm the redesigned
  `ApiKeyPrompt` renders correctly; enter a key → confirm the post-key landing state gives the
  orientation A7 calls for; exercise whatever A8 affordance is built (view/clear the active key)
  and confirm it actually round-trips through `apiKey.js`'s existing `getApiKey`/`clearApiKey`.
- **Existing Playground/Memories/Observability/Evals behavior:** full regression click-through
  identical in spirit to Milestone 1's final controller-level pass — submit a real task, observe
  the Pending→Running→Failed flow (same environment limitation as Milestone 1: no valid
  server-side Anthropic key in this dev environment restricts reaching a genuine Completed/
  Approval-required state, must be named honestly again if it recurs, not silently skipped),
  confirm Memories/Observability/Evals still render and behave exactly as before this milestone's
  changes, since none of A3-A8/A15/A22-25 should alter any of their underlying logic.
- **Regression checks:** re-run the exact hardcoded-token grep sweep pattern Milestone 1
  established, to confirm no new inline hex/px value was introduced outside token references; a
  full `npm run build`/`npm run lint` diff against Milestone 1's final baseline warning set (5
  warnings, same identities) to confirm nothing new was introduced.

---

## 12. Regression Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Routing hook interacts badly with the existing `ObservabilityPage`/`EvalsPage` internal sub-view state (double state sources: URL + local `useState`) | Explicitly scope A4 to top-level views only (§5.1a) unless approved otherwise, avoiding a two-state-sources problem within pages that keep their own sub-view state |
| Responsive JS-based layout hook causes a layout-thrash/flicker on resize, or misses the initial render's correct width | Verify via live, driven browser resize tests (§11), not just static code review — this is exactly the kind of runtime-only bug Milestone 1's own discipline (and this project's longer history — Phase 1's SSE bugs, Phase 2's packaging bugs) has repeatedly found only by actually running the thing |
| A7/A8 redesign creeps into touching the closed auth model (e.g. persisting the key to `localStorage` "for convenience") | Explicit non-goal (§10); any such change must be treated as reopening the closed auth-model decision, escalated for approval, never done silently |
| Accessibility pass (A25) is done superficially (ARIA added without real screen-reader/keyboard testing) | Verification flow should include actual Tab-key navigation through the nav bar and form, not just presence-of-attribute checks |
| `remark-gfm` addition changes markdown rendering in a way that affects the *content* correctness of past-completed evals/agent results (not just table rendering) | Verify the final-result markdown rendering (`App.jsx:701-716`) before/after on a manually-authored, multi-element markdown sample (headings, code, links, and now tables) fed directly into the `finalResult` state for the check — not just confirm the dependency installs cleanly. This is a static/direct verification, not a live agent-driven render (no real `finalResult` is reachable in this environment, per §11.0), and should be reported as such. |
| Scope creep into A9-A21 territory since those files (`App.jsx` especially) are already being edited for shell/routing reasons | Explicit non-goal (§10); a reviewer should check the diff doesn't touch `AgentCard`/`ApprovalCard`/`ManagerReviewCard`/`MemoriesPage`'s table beyond what A3/A4/A5/A7 genuinely require |

---

## 13. Definition of Done

**A3 — Application shell:** header/nav fully consumes `Nav`/`NavItem`/token values (already
true from Milestone 1) with any additional shell polish (title, layout) this milestone adds;
`frontend/index.html`'s `<title>` reflects the product, not the Vite default; the `h1`/`h2`
`index.css` typography leak is corrected per §8 decision 6 — `App.jsx`'s `<h1>` and
`ApiKeyPrompt.jsx`'s `<h2>` render with the intended mono-only font-family/letter-spacing, not
the leftover Vite-template sans-serif/negative-tracking values — verified via `getComputedStyle`
live, not assumed from the CSS change alone. No webfont was added as part of this fix (decision
10 stays deferred).

**A4 — Routing:** all four top-level views are real, distinct URLs; direct URL entry, refresh,
and back/forward all work correctly, verified live (§11), not assumed from code review alone.

**A5 — Responsive design:** the approved breakpoint(s) applied to the Playground grid, header
row, and Observability's stat/compare grids; verified via live screenshots at the bracketed
widths in §11 showing no horizontal overflow and correct layout collapse at the intended widths.

**A6 — Navigation/IA consistency:** folded into A3/A4's own DoD — no separate criteria.

**A7 — First-run/product-entry:** the pre-key screen establishes OrchestAI's identity and the
key's purpose; the post-key landing state gives a clear primary action and orientation to the
other three areas, per the backlog's own A7 wording — content approved via §8a (a separate,
explicit approval gate, not this document's own approval) before being built, not invented ad
hoc during implementation. The orientation elements navigate via A4's own mechanism (§7.1) —
verified live by actually clicking/navigating through them, not just present in markup.

**A8 — API-key management:** a real UI affordance exists to see the active key's presence and
to clear/change it, wired to the existing `getApiKey`/`clearApiKey` functions — no new auth
mechanism introduced.

**A15 — `remark-gfm`:** dependency added (approved, §8 decision 5), final-result markdown tables
render as an actual grid, verified on a manually-authored multi-element sample (§11's `remark-gfm`
verification note — no real `finalResult` is reachable in this environment), not just "the
package installed."

**A22/A23/A24 — Loading/Empty/Error states:** copy revised where it was raw developer-facing
text (e.g. `EvalsPage`'s suite-creation hint); consistent treatment applied via `StateText`,
lightweight only — improved copy and token-consistent styling, no skeleton/spinner/animation
system (§8 decision 9); the Playground's default placeholder is part of this, per the backlog's
own A23 cross-reference.

**A25 — Accessibility baseline:** `aria-current`/equivalent on the active nav item, `aria-label`s
on icon-bearing interactive elements, and a visible `:focus-visible` treatment — verified via
actual keyboard (Tab-key) navigation through the nav and form, not just attribute presence.

**Overall:** `npm run build`/`npm run lint` clean, matching or explicitly justifying any change
to Milestone 1's final 5-warning baseline; full manual click-through (§11) shows no regression
to any Milestone-1-delivered behavior; the grep-sweep regression check (§11) shows no new
hardcoded token-eligible value introduced. **No completion claim states or implies that an
agent-dependent unreachable state (a genuine `Completed` task, `approval_required`,
`manager_review_started`, a real `finalResult`) was verified live in this environment** — per
§11.0, those remain out of reach here, and any such state touched by this milestone's own scope
is verified via direct code comparison/static inspection instead, reported using §3.1a's
classification standard, not glossed over as equivalent to a live check.

---

## 14. Implementation Task Breakdown

Approved 2026-07-24: all ten §8 decisions final, §8a content proposal approved. This section
operationalizes §5/§7/§7.1/§8/§8a into discrete units for `subagent-driven-development`. Task
order matches §7 exactly. Every task inherits these global constraints (repeated in each task's
dispatch, not just here):

- No new npm dependencies except `remark-gfm` (Task 4 only, §8 decision 5).
- Do not modify `AgentCard`, `ToolCallRow`, `ApprovalCard`, `ManagerReviewCard`, `MemoriesPage`
  table body, or final-result markdown rendering logic — those are A9-A21-adjacent and out of
  scope (§2, §10).
- Do not touch `EvalsPage.jsx`'s `<code>` chip styling (§8 decision 7, stays deferred).
- Do not add webfont loading (§8 decision 10, stays deferred).
- Do not add sub-tab URL routing for `ObservabilityPage`/`EvalsPage` internal `SubNav` state
  (§5.1a, §8 decision 2) — that state stays local `useState`, untouched in shape or behavior.
- Styling: inline `style` objects only, consistent with the existing codebase convention — no
  CSS Modules/CSS-in-JS/Tailwind (§5.2, §6).
- Preserve all existing data flow, API calls, SSE handling, and business logic byte-for-byte
  unless a task explicitly says otherwise.

### Task 1: App shell foundation (A3/A6) — header/nav reflow, title fix, h1/h2 typography fix

**Scope (§5.3, §8 decision 6, §3.2):**
1. `frontend/index.html:7` — fix `<title>frontend</title>` to `<title>OrchestAI</title>`.
2. `frontend/src/index.css:61-80` — the `h1, h2 { font-family: var(--heading); ... }` rule
   currently leaks a non-mono font-family/letter-spacing onto `App.jsx`'s `<h1>` (line 578) and
   `ApiKeyPrompt.jsx`'s `<h2>` (line 17). Fix scope is **exactly**: align this rule's
   `font-family` with the app's mono token stack (reuse whatever token/constant
   `frontend/src/theme/tokens.js` already exposes for typography — do not hardcode a new font
   string if a token exists) and correct the `letter-spacing` values so they make sense for a
   monospace face (the current `-1.68px`/`-0.24px` values were tuned for a proportional font).
   **Do not** touch `font-size`, `margin`, `line-height`, or any other property in that CSS
   block. **Do not** touch `ApiKeyPrompt.jsx`'s outer `<div>`'s `fontFamily: 'sans-serif'`
   (line 16) — that stays exactly as-is, a separate, still-deferred issue (do not "fix it while
   you're in there").
3. Header/nav reflow onto shared components: confirm `App.jsx:576-608`'s header (logo/title,
   `Nav`/`NavItem`, task-status area) is already fully on Milestone 1's component system — it
   should be, per §3.1's correction, so this is a verification step, not new restyling work. If
   you find anything in the header still using a hardcoded color/spacing value that has a token
   equivalent, flag it in your report rather than silently changing it (Milestone 2 is not a
   general restyle pass).

**Do NOT do in this task:** webfont loading (decision 10), any change to `NavItem`/`Nav`'s
component API (routing wiring is Task 2), any A7/A8 content (Task 7), any EvalsPage change.

**Verification:** live screenshot/computed-style check of the header, the `<h1>` in the main
shell, and `ApiKeyPrompt`'s `<h2>`, confirming mono font-family now renders and no other visual
property shifted. `npm run build && npm run lint` clean (5 pre-existing warnings baseline).

### Task 2: Client-side routing (A4/A6) — custom history hook, top-level views only

**Scope (§5.1, §5.1a, §7.1, §8 decisions 1-2):**
Build a small custom hook (no routing library) using native `history.pushState`/`popstate`,
mapping exactly four paths to `App.jsx`'s existing `view` state: `/` → `playground`,
`/memories` → `memories`, `/observability` → `observability`, `/evals` → `evals`. Replace the
plain `useState('playground')` at `App.jsx:324` with this hook's state, keeping the existing
conditional render (`App.jsx:610-729`) working unchanged against the same four string values.
`NavItem` `onClick` handlers (`App.jsx:583-594`) should call the hook's navigate function instead
of `setView` directly.

**Explicitly stop at the top level.** `ObservabilityPage.jsx:594` and `EvalsPage.jsx:477`'s
internal `SubNav` `useState` (`'timeline'`/`'suites'` defaults) are NOT part of this task — do
not add URL segments, do not change how those components receive or manage that state, do not
add props threading sub-tab state from `App.jsx`. This is a hard boundary (§5.1a, §8 decision 2).

**Expose:** the navigate function (or the hook itself) needs to be usable from `App.jsx` for
Task 7's A7 orientation content later — return it in a form `App.jsx` can pass down or call
directly (e.g. `const { view, navigate } = useRouting()` at the top of the `App` component).
Document the exact hook name/shape/return signature in your report so Task 7's brief can cite it
precisely.

**Verification:** live-verify all four top-level URLs load correctly on direct entry (deep link),
browser back/forward moves between the four views correctly, and a hard refresh on
`/observability` (both Vite dev server and, if feasible, a `vite preview` build) does not 404.

### Task 3: Responsive layout (A5)

**Scope (§5.2, §8 decisions 3-4):** a small `useViewportWidth()`-style hook (`window.innerWidth`
+ resize listener), no CSS framework. Breakpoints: ~1024px (collapse `ObservabilityPage`'s
4-column stat grids to 2 columns) and ~768px (collapse the Playground's `380px 1fr` grid
(`App.jsx:617`) to a single stacked column — form on top, results below — and wrap/stack the
header row instead of overflowing). These are starting values; adjust during implementation if
the real layouts demonstrate a need to, per §8 decision 3 — note any adjustment and why in your
report.

**Do NOT** touch phone-width layouts (A5a, out of scope) or restyle anything not directly
involved in the grid-collapse/header-wrap behavior described above.

**Verification:** live screenshots at ~1440px (baseline, no change expected), ~1024px, and
~768px for the Playground and Observability views, confirming the described collapses occur and
nothing else regresses.

### Task 4: `remark-gfm` (A15)

**Scope (§8 decision 5):** `npm install remark-gfm` in `frontend/`, wire it into the existing
`ReactMarkdown` usage (`App.jsx:701-716`) via the `remarkPlugins` prop. This is the only task
permitted to add a dependency.

**Verification:** since no real `finalResult` can be produced live in this environment (no valid
Anthropic key — §11.0), verify via a manually-authored markdown sample (e.g. a temporary local
test render or a one-off script) containing a GFM table and a strikethrough, confirming both
render correctly through the same `ReactMarkdown`/`components` config already in place. Document
this as code-compared/structurally-verified, not live-verified, in your report — do not claim
more than was actually exercised.

### Task 5: Loading/Empty/Error copy (A22-A24)

**Scope (§5.4, §8 decision 9, §8a.2):** lightweight copy/styling improvements only, using the
existing `StateText` component — no new skeleton/spinner/animation system. Specifically:
1. The Playground's default empty-result placeholder (`App.jsx:723`) — replace
   `'Submit a task to see results here.'` with the exact §8a.2 copy:
   > "Fill in a task on the left and click Run Agents. Per-agent cards will stream in as they
   > work, and the final result renders here when they finish.
   >
   > Once you've run a task or two: Observability has the cost and timeline breakdown for every
   > run, and Evals runs scored test suites against known-good outputs."

   The `isActive` branch (`'Waiting for agents to complete…'`) is unchanged.
2. Review other loading/empty/error call sites already using `StateText` across `App.jsx`,
   `MemoriesPage.jsx`, `ObservabilityPage.jsx`, `EvalsPage.jsx` for copy clarity — improve
   wording only where genuinely unclear (e.g. bare "Loading..." with no context on what's
   loading), reusing `StateText`'s existing `tone` prop as-is. Do not add a new `tone` value
   unless you find a real gap `StateText`'s current two tones (`muted`/`error`) can't express —
   if so, flag it in your report rather than deciding unilaterally.

**Do NOT** touch `EvalsPage.jsx`'s `<code>` chip (decision 7, deferred) or any A9-A21-adjacent
component's loading/empty state (e.g. `AgentCard`'s internal states are out of scope).

**Verification:** live-verify every reachable empty/loading/error state (Playground empty,
Memories empty, Observability empty, Evals empty — whatever loads without a real agent run);
for anything gated behind a real task completion, code-compare only, documented as such.

### Task 6: Accessibility baseline (A25)

**Scope (§5.4, §8 excludes A25a full audit):** `aria-current="page"` (or equivalent) on the
active `NavItem`; `aria-label`s on icon-bearing `NavItem`s (🧠 Memories, 📊 Observability,
🎯 Evals — the emoji alone isn't an accessible name); a visible `:focus-visible` style on
interactive elements (`NavItem`, `Button`, `Input`/`TextArea`) that currently have none. All
additive changes to the existing shared components — no new components, no formal WCAG AA
audit/certification (that's A25a, deferred).

**Verification:** live-verify focus-visible rendering and tab order via keyboard navigation
through the header nav and the Playground form; inspect rendered ARIA attributes via computed
DOM/accessibility tree, not just source-reading.

### Task 7: First-run/product-entry (A7) + API-key management (A8)

**Depends on Task 2 (A4) being complete and merged first — do not start before then (§7.1).**

**Scope: implement §8a exactly as approved**, sections 8a.1-8a.4. Read §8a in the plan file in
full before starting — it is the complete, approved copy and behavior spec. Key points to hold
to precisely:
- `ApiKeyPrompt.jsx` copy changes per 8a.1, including the note that the subtitle/body paragraphs
  intentionally remain in the wrapper's existing sans-serif (do not "fix" this — out of scope,
  see Task 1's exact h1/h2-only boundary).
- Playground empty-state copy is Task 5's job (8a.2's text), not this task's — if Task 5 already
  landed, verify it matches 8a.2's exact wording; if not yet landed, coordinate rather than
  duplicating the change.
- The "Change key" affordance (8a.3): a small text affordance in the header's right-hand region,
  always visible, calling `clearApiKey()` (from `apiKey.js`) then resetting `keySet` to `false`.
  **No new authentication logic** — this only re-triggers the existing `if (!keySet)` gate
  (`App.jsx:568-570`). Style it like the existing muted small text at `App.jsx:599-604`, not a
  new visual pattern.
- No new clickable orientation links (8a.2's explicit "prose only" decision governs over §7.1's
  earlier illustrative example of clickable `NavItem`-styled orientation elements — §8a is the
  later, approved, more specific decision; the actual dependency on Task 2/A4 being complete
  still holds, because the orientation prose's truthfulness depends on the other three areas
  being genuinely reachable by then, even though this task adds no new links itself).

**Verification:** live-verify the full first-run flow (no key → prompt renders with new copy →
enter a key → lands on Playground with new orientation copy visible) and the "Change key" flow
(click it → `clearApiKey()` fires → prompt reappears → re-entering a key works). Confirm via
`localStorage`/memory inspection that `clearApiKey()` actually clears state, not just that the
UI transitions.

---

*Milestone 2 implementation approved 2026-07-24 (all ten §8 decisions final, §8a approved).
Executed via `subagent-driven-development` in worktree
`worktree-milestone2-core-frontend-modernization`. Do not merge, commit to `main`, or push
without explicit approval.*
