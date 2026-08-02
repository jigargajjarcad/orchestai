# F1.1 — Human Accessibility Verification Report

**Date:** 2026-08-01
**Branch:** `f1-accessibility-conformance`
**Scope:** Manual keyboard-only and screen-reader review of every surface, following F1 (Accessibility Conformance).
**Stance:** Verification only. No refactors, no redesign, no new features.

---

## 1. Method

F1 closed with the note that "automation cannot verify usefulness of announcements, only presence."
This pass was run as a real user session against the **running application** (Vite dev server + the
offline stub backend), not against jsdom, so that layout, compositing, live focus movement and real
`Tab` traversal were all genuine.

For each of the 11 sub-views, at 1280 / 768 / 375 px (plus 320 px and 200 % zoom):

| Probe | What it establishes |
|---|---|
| Real `Tab` / `Shift+Tab` traversal from `<html>` | actual tab order, focus traps, lost focus, wrap point |
| Computed `outline` on the genuinely-focused element | whether the focus indicator is really visible |
| Accessible name / role / `aria-expanded` / `aria-pressed` / `aria-current` per stop | what a screen reader announces at each stop |
| Heading, landmark, live-region, table and dialog inventory | what heading/landmark navigation actually finds |
| `axe-core` in the real browser, on **populated** states | contrast and ARIA correctness on data, not empty states |
| `axe` colour-contrast engine on every status permutation | measured, composited contrast ratios |
| `documentElement.scrollWidth` at 320 px and 200 % zoom | WCAG 1.4.10 Reflow, 1.4.4 Resize Text |
| Live SSE run, stream drop, reconnect, delete-with-focus-recovery | whether announcements make sense in real use |

**Important methodological note:** the ratios and behaviours below are *measured*, not inferred.
Where a finding is a judgement call about usefulness rather than a standards violation, it says so.

---

## 2. Headline

F1's conformance claim **does not survive contact with populated screens.**

F1 audited eleven surfaces with axe and reported zero violations. Those audits ran against
**empty states** — no task selected, no run executed, no detail panel open. Every screen that
actually shows a status badge, a status pill, or rendered agent output has a WCAG 1.4.3 contrast
failure that F1's sweep never rendered and therefore never saw.

The keyboard story is much stronger: tab order is logical on every surface, the mobile drawer's
focus trap is genuinely correct, `Escape` works, focus returns on dialog close, and the Timeline
control F1 fixed is properly operable. Two real keyboard defects remain.

---

## 3. Findings by severity

### CRITICAL

---

#### C1 — Inline code in agent output renders at 1.60:1 and is effectively invisible

**Measured:** foreground `#08060d` on background `#313244` = **1.60:1**. Required: 4.5:1.
**Where:** every inline backtick in a Final Result or Manager Review — Playground, both markdown renderers.
**Reproduced at:** `Playground — completed (list/final result)`, target `code`, text `db_query`.

**Why it matters.** This is not a marginal contrast shortfall — it is near-black text on dark grey.
The text is unreadable for *everyone*, not only low-vision users. Agents routinely emit tool names,
field names, IDs and code fragments in backticks, so this destroys real content.

**Root cause.** `index.css` styles bare `code` with `color: var(--text-h)`. `--text-h` is defined
twice: `#08060d` at `:root`, and `#f3f4f6` inside `@media (prefers-color-scheme: dark)`. The
application chrome is hard-coded dark (`#root` sets `#cdd6f4`; `App.css` sets `body { background:
#11111b }`) and does **not** follow the system scheme — but this one leftover variable does. The
markdown `code` renderers in `App.jsx` then set a dark `background` while inheriting that
scheme-dependent colour. So on any browser whose OS is in **light** mode — the majority default —
inline code is invisible. In dark mode it happens to look fine, which is why it was never noticed.

**Affected users.** All users on a light-scheme OS; catastrophically so for low vision and
low-contrast displays.

**WCAG.** 1.4.3 Contrast (Minimum) — Level AA.

**Recommended fix.** Set an explicit `color` on both branches of the markdown `code` renderer
(inline and fenced) in `App.jsx`, so the rendering no longer depends on the visitor's OS theme.
Give the fenced branch a transparent background so its parent `<pre>` supplies the surface, as
intended.

---

### HIGH

---

#### H1 — Every status badge and status pill fails WCAG AA contrast

**Measured, in the real browser, on real data:**

| Element | Foreground | Composited background | Ratio | Need |
|---|---|---|---|---|
| `StatusBadge` "Completed" (TopBar, Summary, Compare) | `#16a34a` | `#1c3934` | **3.78** | 4.5 |
| `StatusBadge` "Failed" (Summary) | `#dc2626` | `#44202c` | **2.92** | 4.5 |
| `StatusBadge` "Running" (TopBar, mid-run) | `#2563eb` | `#1f2c54` | **2.62** | 4.5 |
| `StatusBadge` "Running" (agent card) | `#2563eb` | `#232d51` | **2.59** | 4.5 |
| `StatusPill` "Completed" (graph node) | `#16a34a` | `#213639` | **3.85** | 4.5 |
| `StatusPill` "Failed" (DetailPanel) | `#dc2626` | `#3f2334` | **2.89** | 4.5 |
| DetailPanel "Error · category" label | `#dc2626` | `#232336` | **3.18** | 4.5 |

Computed across every surface tier, the whole status palette is below the bar as text — the worst
case for each hue is Running 2.28, Skipped 2.34, Pending 2.37, Failed 2.55, Completed 3.28.
Only Warning is close (4.49, still fractionally short).

**Why it matters.** Execution status is the single most important signal this product renders. It
appears in the persistent TopBar, on every agent card, on every graph node, in the detail panel and
in both comparison panes. The badge text is also small (9–12 px) and bold, which is exactly the
regime where a sub-4.5:1 ratio stops being legible.

**Affected users.** Low-vision users, users on low-quality or glare-affected displays, older users.
Colour-blind users are additionally affected because the badge's *only* other differentiator is hue.

**WCAG.** 1.4.3 Contrast (Minimum) — Level AA.

**Why F1 missed it.** F1's axe sweep covered eleven surfaces in their default, unpopulated state.
A `StatusBadge` only renders once a task is selected or a run has executed; a `StatusPill` only
renders inside a graph node or an open detail panel. None of those existed at audit time.

**Recommended fix.** Follow F1's own `textMuted` precedent: add a new *status text tier* token set
rather than changing the existing status hues (which are also used for fills, borders, edges and
accents that are not text and are not failing). Point only the text uses at it.

---

#### H2 — Graph edges are keyboard-focusable with no visible focus indicator

**Measured, after a real `Tab` keypress, in both graphs:**

```
.react-flow__edge   outlineStyle: "none"   visibleRing: false   tabindex: 0
                    aria-label: "Edge from s1 to s2"          (Observability)
                    aria-label: "Edge from orchestrator to aaaa1111-bbbb-2222-cccc-333333333333"
                                                              (Playground)
```

Nodes are fine — they get a real 2 px `#89b4fa` ring from `timelineFocus.css`. Edges do not: React
Flow's own stylesheet sets `outline: none` on focused edges, and nothing overrides it for edges.

**Why it matters.** A keyboard user tabbing through the execution graph hits an edge, then a node,
then an edge — and on roughly half those stops the focus indicator simply vanishes. They cannot
tell where they are. Compounding it, the announcement at those invisible stops is `"Edge from s1 to
s2"` or a raw GUID: no meaning, no action available (edges have no activation handler). On a
realistic 23-node trace this is ~22 dead, invisible tab stops interleaved with the useful ones.

**Affected users.** Keyboard-only users, sighted users with motor impairments, screen-magnifier
users (who track the focus ring), screen-reader users (who hear GUIDs).

**WCAG.** 2.4.7 Focus Visible — Level AA. Also degrades 2.4.3 Focus Order.

**Fix applied.** Two parts, because the first alone was not enough:

1. `edgesFocusable={false}` on both graphs — removes the invisible tab stops, resolving 2.4.7.
2. `domAttributes: { 'aria-hidden': 'true' }` on every edge in `computeGraphLayout` — removes the
   `role="img"` + `"Edge from s1 to s2"` nodes from the accessibility tree.

Step 2 was added during the pre-merge review. Step 1 alone fixed the *tab order* but left the
meaningless labels in the accessibility tree, so a VoiceOver/NVDA user browsing with a virtual
cursor — who never presses Tab — still walked three span-ID announcements before reaching the first
useful node. Verified against the real Chrome accessibility tree (CDP `getFullAXTree`), not just the
DOM attributes: no node named "Edge from …" remains, and the edges are still painted.

Hiding them is architecturally right rather than merely convenient: an edge carries no information
that is not already stated in words on the nodes it connects — Observability's node `ariaLabel` says
"Child of Orchestrator, running alongside Data agent". It is the visual rendering of that sentence,
which is exactly the decorative case `aria-hidden` exists for, and the same reasoning the time-axis
tick row already uses.

**Residual, stated precisely so this is not read as more than it is.** Three unnamed `image` nodes
remain in the accessibility tree — React Flow's own bare `<svg>` wrappers, one per edge, which it
renders outside the edge object and which no edge prop can reach. They carry no name, so nothing
meaningless is announced, and they are pre-existing rather than introduced here (the same unnamed
`<svg>` mapping affects icon SVGs elsewhere in the app). Removing them would require patching the
library. Not addressed; not claimed as addressed.

---

#### H3 — Starting a run destroys the keyboard user's position

**Measured focus trail, pressing `Enter` on "Run Agents":**

```
before Enter  -> BUTTON "Run Agents"
during run    -> BODY
after run     -> BODY
```

**Why it matters.** The Playground's primary action is "Run Agents". The submit handler immediately
sets `submitting`, which sets `disabled` on the button. A disabled element cannot hold focus, so the
browser drops focus to `<body>` — at the exact instant the user commits to the run. Focus never comes
back. To reach anything afterwards — the reconnect button, the approval card, the results — the user
must `Tab` from the top of the document again, past the skip link, four nav items, the collapse
toggle and "Change key". A screen-reader user additionally loses their reading position.

**Affected users.** Every keyboard-only and screen-reader user, on the app's single most important
interaction.

**WCAG.** 2.4.3 Focus Order — Level A. Related: 3.2.2 On Input.

**Recommended fix.** Keep the control focusable while the run is in flight, using `aria-disabled`
plus a guard in the handler, rather than the `disabled` attribute. This is the WAI-ARIA Authoring
Practices recommendation for exactly this case, keeps the disabled visual treatment, and preserves
the user's place. The live region already announces "Task started. Agents are running."

---

#### H4 — Observability and Evals have no in-page headings at all

**Measured heading inventory** (desktop, populated):

| Sub-view | Headings found |
|---|---|
| Observability / Timeline | `h1 Observability` — nothing else |
| Observability / Cost Dashboard | `h1 Observability` — nothing else |
| Observability / Error Rates | `h1 Observability` — nothing else |
| Evals / Results | `h1 Evals` — nothing else |
| Evals / Post-Hoc | `h1 Evals` — nothing else |

Meanwhile the pages clearly *have* visual section headings — "Cost Over Time", "Breakdown by Agent /
Model", "Agent Error Rates", "Tool Error Rates", "Pass/Fail Summary", "Regression vs. Baseline",
"Score historical traces", "Agents Involved", "Providers / Models", "Final Result", "Agent
Executions", "Eval Suites", "Trigger a run". Every one is a `<Label>` rendering a `<span>`: uppercase,
letter-spaced, muted, sitting directly above the block it introduces. Styled as a heading, not marked
up as one.

**Why it matters.** Heading navigation (`H` in NVDA/JAWS, the rotor in VoiceOver) is the primary way
screen-reader users move around a page — far more used than landmarks or tabbing. On these nine
sub-views it finds exactly one item, the view title. A user landing on the Cost Dashboard has no way
to jump to "Breakdown by Agent / Model"; they must read or arrow through everything above it.

**Affected users.** Screen-reader users, primarily. Also anyone using a reader-mode or
document-outline tool.

**WCAG.** 1.3.1 Info and Relationships — Level A. (Failure technique F2: conveying structure through
presentation without the corresponding markup.)

**Note.** This project already established the fix pattern: `App.jsx` wraps exactly this `<Label>` in
`<h2 style={{ margin: 0, font: 'inherit', letterSpacing: 'inherit' }}>` for Memories' "Agent
Memories" and Playground's "Agent Executions" / "Final Result", precisely so the structure becomes
navigable with byte-identical rendering. Observability and Evals were simply never given the same
treatment.

---

#### H5 — The React Flow attribution link renders at 1.23:1

**Measured:** `#999999` on `#88888d` = **1.23:1**, on every canvas in both Playground and Observability.

**Why it matters.** It is a focusable link in the tab order whose label is illegible. Third-party
origin does not exempt it — it ships inside the product and axe reports it against our page.

**Affected users.** Low-vision users; anyone trying to read it.

**WCAG.** 1.4.3 Contrast (Minimum) — Level AA.

**Recommended fix.** Override `.react-flow__attribution` colours in the existing scoped stylesheet.
Restyling is preferable to hiding it, which would raise a licensing question this milestone has no
mandate to decide.

---

### MEDIUM

---

**M1 — Deleting a memory announces nothing.**
Focus recovery works correctly (verified: focus lands on the next row's Delete button, and on the
section heading when the last row goes). But no live region announces the outcome. A screen-reader
user presses Enter on "Delete memory roster-load-management-2026 from Research", and hears only
"Delete memory conditioning-index from Data, button" — a *different* button. Nothing confirms the
deletion happened. Measured: `live regions: []` after delete.
*Not a 4.1.3 failure* — that criterion governs how status messages are exposed, and does not require
that one exist. It is a real usability gap for a destructive, unconfirmed, irreversible action.
**Fix:** add a polite live region announcing "Memory <key> deleted."

**M2 — Evals field captions are not associated with their fields.**
"Subject version", "Baseline run", "From", "To", "Agent type", "Max traces", "Rubric" are `<Label>`
spans with no `htmlFor`; the controls carry matching `aria-label`s instead. Measured: zero `<label>`
elements in `#main` on Evals/Run and Evals/Post-Hoc. Accessible names therefore exist, but clicking a
caption does not focus its field, and the visible/programmatic association a screen reader reports is
synthetic. Observability's `DateRangePicker` and `TaskPicker` already do this correctly with
`useId` + `htmlFor` (P2-M6.3); Evals was not migrated. WCAG 1.3.1 (A), 3.3.2 (A) quality.
**Fix:** apply the `useId` + `Label htmlFor` pattern already used in `ObservabilityPage.jsx`.

**M3 — The live region can announce "undefined agent completed."**
Measured live-region sequence during a real SSE run:
`"Task started. Agents are running." → "Research agent started." → "undefined agent completed." → "Task completed. Total cost $0.0310."`
`App.jsx` interpolates `p.agentType` unguarded on `agent_completed` / `agent_failed`. The production
backend (`AgentBase.FinalizeSuccessAsync` / `FinalizeFailureAsync`) *does* always send `agentType`, so
this is **not** currently reachable in production — the offline test fixture omits it. It is a
robustness gap on an external input, and it means the e2e fixture has been exercising a nonsense
announcement.
**Fix:** fall back to a neutral phrasing when `agentType` is absent; correct the stub fixture.

**M4 — The API-key gate has no landmark and no `h1`.**
Live axe on the gate: `landmark-one-main` (1), `page-has-heading-one` (1), `region` (5 nodes). The
gate renders a bare `<div>` tree — no `<main>`, and its only heading is an `<h2>`. It is the app's
sole entry surface, and it is the one surface F1's axe suite never covered: `f1-accessibility.test.jsx`
calls `setApiKey('test-key')` in `beforeEach`, so `<App/>` never renders `ApiKeyPrompt`, and
`m6-4-onboarding.test.jsx` runs axe on a detached container where the `region` and
`landmark-one-main` rules do not fire.
Best-practice, not a WCAG A/AA violation.
**Fix:** wrap the card in `<main>` and promote the wordmark to `h1`.

**M5 — The Timeline span tree has no tree or list semantics.**
Rows are nested `<div>`s; nesting depth is conveyed only by `padding-left: depth * 20`. The
expand/collapse buttons carry correct `aria-expanded` (F1's fix, verified working) but sit in no
`tree`/`treeitem`/`group` structure, so a screen reader announces the state without ever conveying
the hierarchy the state applies to. Measured: no `role=tree`/`treeitem`/`list` anywhere in the view.
WCAG 1.3.1 (A) for the depth relationship.
**Fix:** add `role="tree"` / `role="treeitem"` with `aria-level`, or nested `<ul>`/`<li>`.

**M6 — The DetailPanel dialog has no heading element.**
`aria-labelledby="observability-detail-panel-heading"` resolves to a `<div>`. The dialog is correctly
named, but a screen-reader user navigating by heading inside it finds nothing.
**Fix:** make the labelled node an `<h2>` with `font: inherit`.

**M7 — Disabled submit buttons vanish from the tab order without explanation.**
"Run suite" (disabled until a subject version is typed) and "Submit post-hoc scoring request"
(disabled until rubric + both dates) use the `disabled` attribute, so they are removed from the tab
order entirely. Measured: `disabled: true, aria-disabled: null, aria-describedby: null, title: null`.
A keyboard user tabs from "Rubric" straight past the submit control and is never told why they
cannot proceed. WCAG 3.3.2 (A).
**Fix:** `aria-disabled` + a described-by hint naming the missing input, matching H3's approach.

**M8 — Playground graph node names are concatenations, Observability's are sentences.**
Measured focused-node accessible names:
Playground `"OrchestratorCompleted6.00s"`, `"ResearchCompleted4.00s$0.031000"`.
Observability `"Agent Orchestrator, Completed, 5.20s. Child of …, running alongside …."`.
`ObservabilityPage` builds a rich `ariaLabel` per node; `PlaygroundExecutionGraph` passes the layout
through unchanged, so the focusable wrapper falls back to name-from-contents. The inner node `<div>`
*does* carry a good `aria-label`, but it is not the focusable element.
**Fix:** reuse Observability's `nodesWithAria` construction in the Playground graph.

---

### LOW

- **L1** — Evals suite rows announce as one run-together string: `"Roster analysis regression suiteResearch · Golden-set roster questions with expected-substring…"`. Separate the name from the metadata.
- **L2** — Touch targets below the project's own 44 px goal: sub-nav tabs 27 px, selects/date inputs 32 px, "Change key" 29 px, "Run Agents" 35 px, DetailPanel close 26 px, Timeline span toggles 14 px. All clear WCAG 2.2 AA (2.5.8, 24 px) **except the 14 px span toggles**, which are 14×14 with no `.touch-target` overlay. F1 applied that overlay to the Memories Delete button and the Toggle segments but not here.
- **L3** — A dropped stream announces twice with different wording: the polite region says "Live updates disconnected. Use Reconnect to resume following this task." and the `role="alert"` banner says "Live updates disconnected. The task may still be running on the server." Both are useful, but the duplication is noisy. Consider one message carrying both facts.
- **L4** — "Change key" clears the session with no confirmation and sits at tab stop 6 on every page, immediately before content. Easy to hit by accident; consequence is a full session reset.
- **L5** — On the Playground right pane, `Spinner` (`role="status"`, "Waiting for agents to complete…") mounts alongside the streaming live region, so two polite regions can announce near-simultaneously.
- **L6** — The gate autofocuses the API key field, so a screen-reader user lands on "API key, password, edit" and never hears the two paragraphs explaining that keys are provisioned out-of-band. The autofocus is defensible (M6.4 reasoned it through); consider `aria-describedby` linking the field to the explanation.
- **L7** — `SubNav` uses `role="group"` + `aria-current` rather than the tabs pattern. Functionally fine and deliberately chosen (P2-M6.3 decision A2); a real `tablist`/`tab`/`tabpanel` with `aria-controls` would announce "tab 3 of 5" and support arrow-key navigation.

---

### NICE TO HAVE

- **N1** — Give the graph a textual alternative: an off-screen ordered list of the trace, the way `BarChart` already provides an off-screen data table. Node-by-node tabbing works but gives no overview.
- **N2** — `aria-controls` between sub-nav buttons and the panel they swap, so the relationship is programmatic rather than positional.
- **N3** — Name the `<header>` landmark (`aria-label="Page context"`); it currently has no accessible name.
- **N4** — Announce sub-view changes. Switching Observability tabs replaces the whole content region silently; nothing tells a screen-reader user the switch happened or where focus now sits.

---

## 4. What is genuinely good

Stated explicitly, because it is most of the surface area and it was verified, not assumed:

- **Tab order is logical on all 11 sub-views, at all three breakpoints.** Skip link → nav → collapse toggle → context controls → content, every time. No focus traps outside dialogs. No tabbable element is off-screen or hidden.
- **The mobile drawer's focus trap is correct.** Verified over 14 `Tab` and 3 `Shift+Tab` presses: focus never once left the panel. `Escape` closes it and returns focus to the menu button. `aria-modal="true"`, correct accessible name, body scroll locked.
- **The DetailPanel dialog is correct.** Focus enters on the close button, cycles within, `Escape` closes, and focus returns to the exact graph node that opened it — verified end to end.
- **Every focusable control has a visible focus indicator** — except the graph edges (H2).
- **Timeline expand/collapse (F1's fix) genuinely works.** `Enter` toggles, `aria-expanded` flips, the label updates from "Collapse Orchestrator" to "Expand Orchestrator", children hide.
- **Memories delete-focus recovery genuinely works.** Focus moves to the successor row, then the previous row, then the section heading — exactly as designed.
- **Toggle is a correct radiogroup** with roving `tabindex`, Arrow/Home/End keys and `aria-checked`.
- **Every table has a caption and `scope="col"` headers.** The off-screen bar-chart data table is a genuinely good idea and is correctly anchored.
- **Reflow and zoom pass.** Zero horizontal document overflow at 320 px and at 200 % zoom, on every surface.
- **Landmarks are complete and correctly named** on all four main views: `aside "Sidebar"` → `nav "Primary"`, `header`, `main`. F1's sidebar landmark fix holds.
- **Error states are announced.** `ErrorBanner` is `role="alert"` everywhere; the stream-drop recovery path announces and offers a reachable Reconnect control.
- **The SSE announcement design is right.** Phase transitions only — task start, agent start/complete, review, completion with cost — never per-token or per-tool. That restraint is what makes it usable rather than spam.

---

## 5. Verdict on the F1 conformance claim

| F1 claim | Holds? |
|---|---|
| WCAG 2.1 AA on machine-verifiable criteria | **No** — 1.4.3 fails on every populated screen (C1, H1, H5); 2.4.7 fails on graph edges (H2) |
| axe clean across 11 surfaces | **Only in empty states.** Clean on Playground / Memories / Evals / Cost Dashboard / Error Rates; violations on Summary, Compare, both graphs, DetailPanel, and the API-key gate |
| Keyboard operable | **Yes**, with one real defect (H3) |
| Touch targets ≥ 44 px | **Partially** — applied to Memories Delete and Toggle only; ~12 other controls are 14–35 px (L2) |

The gap is not carelessness — it is that an automated sweep run against default, unpopulated screens
cannot see the states that carry the failures. That is precisely the class of defect a human pass is
for, and it is the argument for keeping the populated-state audit as a permanent gate.

---

## 6. Disposition

Per the milestone brief, **Critical and High are fixed in this milestone; Medium and Low are left as
recommendations.**

| ID | Severity | Action |
|---|---|---|
| C1 | Critical | Fix |
| H1–H5 | High | Fix |
| M1–M8 | Medium | Recommend — carry to a follow-up milestone |
| L1–L7 | Low | Recommend |
| N1–N4 | Nice to have | Recommend |

M2, M4 and M8 are each a small, well-understood application of a pattern this codebase has already
established elsewhere, and would make sensible first candidates for the follow-up.

---

## 7. Changes made

| Finding | Change |
|---|---|
| C1 | `App.jsx` — both markdown `code` renderers now set `color` explicitly (and `background: transparent` on the fenced branch), so rendering no longer depends on the visitor's OS colour scheme |
| H1 | `theme/tokens.js` — new `status*Text` tokens (an AA-safe *text* tier); `statusBadgeColors` gains `accent` so the border keeps composing from the untouched hue. `StatusBadge`, `NodeParts.StatusPill`, `DetailPanel`'s error label and `ObservabilityPage.STATUS_COLORS` now read text from the new tier. `timelineTheme.js` gains the matching `statusTextColors` map |
| H2 | `edgesFocusable={false}` on both graphs, plus `domAttributes: { 'aria-hidden': 'true' }` on every edge in `observability/layout.js` so the span-ID labels leave the accessibility tree too (see H2 above for the residual that remains) |
| H3 | `Button` gains an additive `ariaDisabled` prop (same visual, same state exposed, stays focusable); the Playground submit uses it, with the re-entry guard moved into `handleSubmit` |
| H4 | A `SectionHeading` helper in `ObservabilityPage.jsx` and `EvalsPage.jsx`; 14 section captions promoted from `<Label>` spans to `<h2>`/`<h3>`, `font: inherit` throughout. `ComparisonSide`'s task prompt becomes the panel's `<h2>` so the two panes are distinguishable in a heading list |
| H5 | `.react-flow__attribution` restyled in `timelineFocus.css`; that stylesheet is now imported explicitly by `PlaygroundExecutionGraph` instead of arriving via another module's import |

**No fills, borders, spacing, type or layout changed.** Every fix is either a text colour, an ARIA
attribute, a focusability flag, or a `font: inherit` element wrapper.

### Verification that the new tests are not vacuous

The browser suite was run against the pre-fix source (`git stash push -- src/`): **all 10 tests
failed**, then all passed once the fixes were restored. The contrast assertions in particular would
have been worthless had they been written against already-passing empty states — which is the exact
mistake this milestone found in F1.

### Gate results

| Gate | Result |
|---|---|
| `npm test` | **166 passed**, 19 files, 0 failed |
| `npm run test:e2e` | **353 passed**, 4 skipped, 0 failed (desktop + tablet + phone) |
| `npm run lint` | **exit 0** — 6 warnings, unchanged from F1's baseline |
| `npm run build` | **exit 0** |

New coverage: `src/f1-1-accessibility.test.jsx` (21 tests — token contract, badge rendering,
markdown colour, focus retention, heading structure) and `tests/playwright/a11y/f1-1.spec.js`
(11 tests × 3 viewports — composited contrast on populated screens, both colour schemes, edge
focusability, focus survival across submit, heading outlines).

---

## 8. Pre-merge cleanup

A senior-review pass over the F1.1 diff raised six findings. Resolutions:

| # | Finding | Resolution |
|---|---|---|
| 1 | H2 half-fixed — edges out of the tab order but still in the accessibility tree | **Fixed** — `aria-hidden` on every edge; verified against the real Chrome a11y tree. Residual unnamed `<svg>` wrappers documented in H2, not claimed as fixed |
| 2 | Five new tokens absent from `DESIGN.md` and `.impeccable/design.json` | **Fixed** — added to the frontmatter `colors:` block, to the sidecar's `colorMeta` (with tonal ramps), and to the sidecar's Status Badge worked CSS. The Status-palette "never a new hue" rule now carries an explicit F1.1 exception, in F1's format. The three prose/spec places that still said badge text is "the full-opacity status color" were corrected. Also closed F1's own gap: `text-muted` was in `tokens.js` and the sidecar but never in the DESIGN.md frontmatter |
| 3 | `timelineFocus.css` header comment no longer true | **Fixed** — header rewritten to describe both importers and the added attribution rule. Filename left as-is (a rename is churn without mandate) |
| 4 | Dead `style` param on `ObservabilityPage`'s `SectionHeading` | **Fixed** — removed |
| 5 | Contrast tests did not assert their subject was on screen | **Fixed** — four presence assertions added, so a fixture change makes the tests fail rather than silently pass |
| 6 | `Button`'s `ariaDisabled` does not guard its own `onClick` | **Left open, deliberately.** Changing it would alter behaviour, and today's only caller is a submit button whose guard lives in `handleSubmit`. Documented in the component's own JSDoc as a caller contract |

**Known-open items carried past merge:** review finding 6 above, and the 18 Medium/Low/Nice-to-have
accessibility findings in §3, none of which are WCAG A/AA failures.
