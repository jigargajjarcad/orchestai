# Milestone 3 — Final Cross-Page UX Consistency Pass (Investigation + Plan)

> **Status: investigation and planning only. Not approved for implementation.** Follows the
> same investigate-then-plan discipline used for Milestones 1 and 2. No source/application file
> was modified while producing this document. No implementation worktree was created.
> `main`/`origin/main` remain unchanged at `05d4917`.

---

## 1. Executive Summary

The backlog's original Milestone 3 scope (`docs/product-completion-backlog.md`'s A9-A21/A26)
was written before Milestones 1 and 2 existed, on the assumption that A9-A21 ("restyle only"
items — agent execution UI, cards, Observability, Evals) were still untouched. They are not.
Milestone 1's actual implementation approach (migrating every page onto the shared component
system) incidentally restyled almost all of A9-A21 as a byproduct, and Milestone 2's own
investigation phase already documented this precisely (§3.1a of
`docs/superpowers/plans/2026-07-24-milestone2-core-frontend-modernization.md`).

Re-verifying that evidence against the current repository (post-Milestone-2, `main` at
`05d4917`) confirms: **of the ten A9-A21 rows, nine have no remaining implementation gap** —
seven are "Partially complete" only because a populated render state has never been reachable in
this environment (no valid Anthropic API key has ever existed here, so no task has ever
completed, and Observability's Cost Dashboard has one similarly-unreached populated sub-state);
one (A17-A20, Observability) is fully "Complete / verified clean"; one (A21, Evals) is
"Partially complete" with two narrow, named verification gaps only. **None of these nine rows
have real implementation work remaining** — the gap in every case is a verification limitation
this environment structurally cannot lift, not a missing feature.

The one item with genuine, well-evidenced, actionable remaining work is **A26** — and it turns
out to be considerably more concrete than the backlog's one-line description ("verifies A1-A25
actually cohere") suggested. Direct investigation of the actual current code against `DESIGN.md`
and `.impeccable/design.json` surfaced a specific, precisely root-caused list of real
cross-page inconsistencies — most already known and explicitly deferred from Milestones 1/2,
one newly found during this investigation (`ToolCallRow`'s border-left status against
`DESIGN.md`'s named-exception list), and one internal contradiction inside `DESIGN.md` itself
(the One Accent Rule vs. the Button section's own "destructive ghost" example). This plan
proposes Milestone 3's scope as **exactly that list** — a bounded, evidence-backed consistency
pass — not a re-litigation of any already-completed Milestone 1/2 work, and not a redo of A9-A21.

---

## 2. Current Repository / Backlog State

**[Fact]** `main`/`origin/main` both at `05d4917` (Milestone 2 merged and pushed, zero
divergence, confirmed by fresh `git fetch` immediately before this investigation began).

**[Fact]** `docs/product-completion-backlog.md`'s §5 execution sequence names Milestone 3 as
"A9-A21, A26: full restyle/consistency pass onto the Milestone 1/2 foundation." This wording
was accurate at the time it was written (before Milestone 1 existed) and is now stale in one
specific, material way: it describes A9-A21 as a restyle job still to be done, when in fact that
restyle already happened, as a side effect of how Milestone 1 was actually built. This is the
same correction Milestone 2's own plan already made for its own scoping purposes (§3.1 there);
this document extends that correction into a full re-classification for Milestone 3 specifically.

**[Fact]** Milestone 2's own final pre-merge audit (recorded in this session, not re-derived
here) confirmed zero diff lines in `AgentCard`, `ToolCallRow`, `ApprovalCard`,
`ManagerReviewCard`, `MemoriesPage`'s table body, and the `ReactMarkdown` `components` override
map across all of Milestone 2's 11 commits; `EvalsPage.jsx` had zero diff entirely;
`ObservabilityPage.jsx` was touched only for `SummaryView`'s two stat-grid `gridTemplateColumns`
values. This means the A9-A21 evidence gathered during Milestone 2's own investigation
(§3.1a there) is still current in substance — only line-number citations needed refreshing,
which this document does directly against the current file state.

**[Fact, re-confirmed directly for this investigation]** No valid Anthropic API key exists
anywhere in this environment: no `.env` file at the repo root, no `ANTHROPIC_*` environment
variable, and both `src/OrchestAI.API/appsettings.json` and `appsettings.Development.json` have
empty `Anthropic:ApiKey` fields. This is the same structural limitation documented throughout
Milestones 1 and 2 — it still means no task can reach a genuine `Completed` state, and no
`approval_required`/`manager_review_started`/`task_completed` event can ever fire here.

**[Fact]** No database seed mechanism exists that would create a populated `Task`/
`AgentExecution` record without a real agent run: `src/OrchestAI.Infrastructure/Data/
DatabaseSeeder.cs` (100 lines) seeds only dev-convenience `Users` and `ModelPricing` rows —
nothing resembling a completed task. `scripts/seed-sports-demo-data.sql` is unrelated (Sports
sample-app demo data, a different product surface). This confirms the populated-state
verification gap named repeatedly below is a real, structural limitation of this environment,
not an oversight — there is no shortcut available today to seed a fake completed task for
verification purposes.

**[Fact, newly confirmed this session, point-in-time — not previously checked at this depth]**
There is currently no reachable backend of *any* kind for this frontend to talk to. `App.jsx`'s
default `API_BASE` fallback points at `https://orchestai-production.up.railway.app`; a direct
read-only check *at the time of this investigation* returned Railway's own
`{"status":"error","code":404,"message":"Application not found"}` — the referenced production
deployment was not live at that moment. This is a live, external, third-party-hosted service —
it could be redeployed or wake up at any time, so this fact should be re-checked rather than
assumed permanent if it becomes load-bearing for a future decision; it does not affect this
plan's own proposed scope (§4), which is frontend-only regardless of backend reachability. A
Postgres container is running locally on port 5432, but it belongs to an unrelated worktree's
own `docker-compose` project (`workstream2-adr017-followups`), not this project's backend; no
OrchestAI API process is running locally either. This closes off even the one theoretical
exception noted below (`MemoriesPage`'s data comes from a real `GET` fetch, not an SSE stream —
architecturally the one place pre-existing historical data, if any existed and were reachable,
could satisfy a gap without a new agent run) — right now, nothing is reachable at all, for any
component.

**[Fact]** `AgentCard`'s messages/tool-calls/cost, `ApprovalCard`'s full flow, and
`ManagerReviewCard` are populated exclusively from live SSE events during the current browser
session (`App.jsx`'s `handleSubmit`'s `es.onmessage` switch) — never from a REST fetch of
historical data. So even where a reachable backend with old completed-task rows to exist, these
three components structurally cannot render populated content without a live, currently-running
SSE stream in an open tab. `MemoriesPage` is the one architectural exception (a real `GET
.../memories` fetch) — moot today given no backend is reachable at all, but worth noting for
whoever plans verification for a future milestone where a backend might be back up.

---

## 3. Evidence-Based Assessment of A9-A21 and A26

Source for every citation below: direct reading of the current file state on `main` at
`05d4917` (this session), cross-checked against Milestone 2's own §3.1a evidence table (which
itself cited the actual Milestone 1 implementer/reviewer reports, still present as local
session artifacts at `.claude/worktrees/milestone1-frontend-foundation/.superpowers/sdd/
task-{3,4,5}-report.md` and `task-final-cleanup-report.md`).

**Classification key** (exactly as required): **Complete / verified clean** · **Partially
complete** · **Still requiring implementation** · **Not yet assessed**. As established in
Milestone 2's own methodology and preserved here without change: an element that was never
exercised in a live render at all — not even in an empty/default state — is classified Partially
complete even when its code has been directly, explicitly compared and matches byte-for-byte or
token-for-token. Live and code-comparison are both real verification; they are not
interchangeable for the top-level classification.

| Backlog item | File / component (current line refs) | What satisfies it | Verification method | Gaps / caveats | Classification |
|---|---|---|---|---|---|
| A9 — Agent execution experience (form, header, SSE) | `App.jsx:311-767` (`App()` overall; header `578-629`; form `650-690`; `handleSubmit`/SSE `onmessage` switch `386-521`) | Form fields on `Input`/`TextArea`/`Button`; header task-status badge on `StatusBadge`; real SSE handling drives real `Pending→Running→Failed` transitions | **Live, computed-style verified** repeatedly across Milestone 1 and re-confirmed during Milestone 2's own post-merge controller verification (a real task submission reaches `Failed` status live, header/form styling confirmed) | The live-streaming multi-message/multi-tool-call feed during a *successful* run has never been exercised — every real task fails at the Orchestrator's planning call before any message/tool-call event fires, because no valid Anthropic key exists. This is confirmed structurally (§2 above), not assumed. The SSE-handling logic itself is untouched since before Milestone 1. | **Partially complete** — form/header/status-transition path (including the `Failed` path) verified clean live; the successful-run streaming-feed visual path has never been exercised by any method, because it has never occurred |
| A10 — Agent cards | `AgentCard` (`App.jsx:58-114`) | Outer shell on `<Panel accentStatus>` (`:61`) + `<StatusBadge borderAlphaSuffix="60">` (`:71`) | **Shell: live, computed-style verified** (a real `Failed`-status card for the Orchestrator has rendered in every prior live-verification round) | The card's conditional inner content — messages (`:74-89`), tool-call list (`:91-97`, renders `ToolCallRow`), cost/token footer (`:99-104`), saved-memories line (`:106-110`) — has never rendered with real data, since the Orchestrator fails before writing messages, dispatching tools, or accruing cost. Verified only via direct code comparison to the token system. | **Partially complete** — shell verified clean live; inner conditional content verified clean via code comparison only, never live-rendered |
| A11 — Tool-call visualization | `ToolCallRow` (`App.jsx:23-56`) | Structure/colors token-based (`color` derived from `colors.statusWarning`/`statusCompleted`/`statusFailed`, `:25`); `borderLeft: 3px solid ${color}` (`:36`) | **Direct code comparison only** — never rendered live, since no tool call has ever fired in this environment (same root cause as A10) | Same never-fired gap as A10. **New finding, see §3's A26 discussion below**: this component's `borderLeft` status-accent is not explicitly named in `DESIGN.md`'s enumerated exception list ("agent cards, approval card, manager-review card" / "agent execution cards, approval prompts, error banners") even though it functions identically to the named exceptions — a documentation-completeness gap, not an implementation defect. | **Partially complete** — verified clean via code comparison; never live-rendered |
| A12 — Human-in-the-loop approval experience | `ApprovalCard` (`App.jsx:116-201`) | Outer border/buttons (`:121-128,140-197`) deliberately kept as untouched local one-offs since before Milestone 1; only its `<textarea>` moved to the shared `TextArea` (`:166-172`) | **Untouched portion: verified clean via the strongest form of code comparison** (provably byte-for-byte unchanged across all of Milestone 1). **`TextArea` portion: code comparison only**, never live-rendered (`approval_required` never fires here) | Milestone 2's accessibility task additionally gave the shared `TextArea`/`Input`/`Button`/`NavItem` components a `.focus-ring:focus-visible` CSS class — real, but dormant on this specific instance, since `ApprovalCard` has never rendered live at all to keyboard-focus-test it. **Confirmed still-unresolved deferred finding** (Milestone 1, re-confirmed in Milestone 2's §9 disposition table as "likely Milestone 3/A26"): Approve (`colors.statusCompleted` fill, `:146`), Reject (`colors.alertRed` border/text, `:157`), Confirm Reject (`colors.statusFailed` fill, `:179`), and Cancel (raw `#313244` border, `:189`) all use non-Trace-Blue colors for interactive affordances, directly against `DESIGN.md`'s stated "One Accent Rule." This card is also a hand-rolled div, not built on the shared `Panel` component, despite `Panel`'s `accentStatus="WaitingForApproval"` already computing an equivalent border/border-left pattern. | **Partially complete** — the deliberately-untouched/migrated portions are verified clean by their respective methods; the One-Accent-Rule question is resolved in §8 decisions 1 and 6 (kept as red, with the Reject button's specific palette-mixing corrected), not a verification gap |
| A13 — Manager/reconciliation synthesis experience | `ManagerReviewCard` (`App.jsx:203-229`) | `<Panel accentStatus>` (`:216`) + `<StatusBadge borderAlphaSuffix="60" label=...>` (`:219`), reusing `AgentCard`'s already-verified pattern | **Direct code comparison only** — `manager_review_started`/`manager_review_completed` require every agent to complete successfully first, which has never happened here | Never rendered in any check, in either the pre- or post-Milestone-1/2 state | **Partially complete** — verified clean via explicit code comparison to the already-live-verified `AgentCard` pattern; zero live exercise |
| A14 — Final result presentation | Final-result `Panel` + `ReactMarkdown` (`App.jsx:725-746`) | `<Panel>` chrome; markdown overrides (`h1`/`h2`/`h3`/`code`/`a`/`p`/`ul`/`li`) token-based; `remarkPlugins={[remarkGfm]}` (`:731`, Milestone 2) | **Panel chrome: code comparison only** (never live-exercised — `finalResult` is never set here). **Placeholder/empty state: live, pixel-verified** in Milestone 1 and again in Milestone 2 (exact copy confirmed live, `App.jsx:755`). **`remark-gfm` wiring: execution-verified** via an actual `react-dom/server` render (Milestone 2), confirming real GFM table/strikethrough/task-list output, though not through `App.jsx`'s own full `components` override set | The actual rendered markdown with real content (headings, code blocks, links, and specifically GFM tables) has never been visually exercised end-to-end inside the running app, since `finalResult` is never set here | **Partially complete** — the empty/placeholder state is verified clean live; the populated markdown-rendering state has never been exercised end-to-end inside the app itself |
| A15 — `remark-gfm` fix | `App.jsx:2,731` | Dependency added, wired into `remarkPlugins` | **Execution-verified** (Milestone 2 — actual `renderToStaticMarkup` execution confirmed real GFM output) | None remaining — this item is fully done, not part of Milestone 3 | **Complete / verified clean** |
| A16 — Memories experience | `MemoriesPage` (`App.jsx:231-309`) | Eyebrow on `<Label>` (`:261`); error/empty/loading on `<StateText>` (`:266,271,273`, incl. Milestone 2's "Loading memories…" copy fix); table markup token-based (`:275-306`) | **Eyebrow/error/empty/loading states: live, pixel-verified** (Milestone 1 and re-confirmed live in Milestone 2's own CDP verification) | The populated `<table>` with real memory rows has never rendered — no agent has ever completed successfully here, so no memory has ever actually been saved. Verified only via direct code/token comparison. **New finding**: the row-level "Delete" button (`:295-300`) uses raw hex `#dc262680`/`#f38ba8` (Alert Red), not a token and not Trace Blue — the same underlying One-Accent-Rule question as A12, plus a hardcoded-value (not token) instance. | **Partially complete** — eyebrow/loading/empty/error states verified clean live; the populated table and its Delete-button color choice are open items (populated-table = verification limitation; Delete-button color = a real, actionable A26 item) |
| A17-A20 — Observability dashboard, timeline, cost, error views | `ObservabilityPage.jsx` (617 lines, all 5 sub-views: `TimelineView:198`, `SummaryView:255`, `DashboardView:362`, `ErrorRatesView:486`, `CompareView:560`) | Full migration onto `Panel`/`Label`/`StatusBadge`/`NavItem`/`StateText`, tokens throughout (zero remaining hex, confirmed by grep); Milestone 2 additionally added the responsive `statGridCols` (1024px breakpoint) to `SummaryView`'s two stat grids (`:285,292` region) | **Live, pixel- and computed-style-verified, extensively, including with real populated data** — Observability doesn't require a successful agent run to have real data (unlike A9-A16), so this is the one area verified against genuinely populated Failed-task data throughout Milestone 1 and re-confirmed live in Milestone 2 | Only the already-accepted 2px `Label` position offset applies here (§9 of Milestone 2's plan, permanently accepted, not reopened). The Cost Dashboard sub-view's populated-data rendering (as opposed to its live-verified empty state) rests on the same component/token pattern already verified live elsewhere on this same page, not an independent populated-state check of its own — named for consistency, not a material gap. | **Complete / verified clean** |
| A21 — Evaluation UI (suites/runs/results/regression/post-hoc) | `EvalsPage.jsx` (513 lines, `SuitesView:49`, `RunView:82`, `ResultsView:167`, `PostHocView:310`) | Full migration onto the same components; three `Button`-based actions given an explicit unconditional-color override to preserve original disabled-state behavior | **Live, computed-style-verified for the great majority**, including two genuinely-triggered error paths (`regressionError` live-triggered and measured; two of three action buttons measured in both disabled/enabled states) | Two specific, named exceptions from Milestone 1: `resultsError`'s path was not independently re-triggered (verified via code comparison to the sibling `regressionError` pattern); `Refresh summary`'s disabled/enabled color-stability claim rests on code comparison only (this environment has no completed eval traces to post-hoc-score against). **Confirmed still true today** — nothing about this has changed since Milestone 2 (zero diff in `EvalsPage.jsx`). | **Partially complete** — the large majority verified clean live; two narrow, named sub-paths verified via code comparison only |
| A26 — Final cross-page UX consistency pass | Not started as a dedicated pass — see the itemized findings below | N/A | N/A | **Its blocking dependency (A22-A25) is now satisfied** — Milestone 2 landed both (Tasks 5/6, confirmed via commit history), so A26 is newly eligible to start, correctly blocked before now. The concrete, evidence-backed content of this pass is detailed immediately below, since the backlog's own one-line description ("verifies A1-A25 cohere") is too abstract to plan against directly. | **Still requiring implementation** |

### 3.1 — A26, in concrete detail

Direct investigation (this session) of `App.jsx`'s hand-rolled components against `DESIGN.md`
and `.impeccable/design.json` surfaced a specific, bounded list — not a re-opening of any
already-decided Milestone 1/2 question, and not vague "polish":

1. **`ApprovalCard`'s buttons violate the One Accent Rule** (`App.jsx:141-197`) — Approve
   (green, `colors.statusCompleted`), Reject (red border/text, `colors.alertRed`), Confirm
   Reject (red fill, `colors.statusFailed`), Cancel (raw hex border `#313244`, not even a
   token). `DESIGN.md`'s own text states plainly: "Trace Blue is the only color used for
   interactive affordances (buttons, links, focus)." This is Milestone 1's own already-flagged
   deferred finding, explicitly named in Milestone 2's disposition table as "likely Milestone
   3/A26" — confirmed still unresolved, not touched by Milestone 2. **Resolved by §8 decision
   1**: red stays for destructive actions, formalized as a named exception.

2. **`DESIGN.md` contradicts itself about destructive-action color.** The same document's §5
   Buttons section reads: "Secondary / Ghost: ... 1px colored border at 50% alpha (e.g.
   `#dc262680` for a destructive ghost action)." That is a literal, named exception to the One
   Accent Rule stated two sections earlier, for exactly the pattern `ApprovalCard`'s
   Reject/Cancel buttons and `MemoriesPage`'s Delete button (`App.jsx:295-300`, also
   `#dc262680`/`#f38ba8`) already use. `frontend/src/components/Button.jsx`'s own code comment
   (written during Milestone 1) already noticed this tension and deliberately built the shared
   `Button` component's `ghost` variant using only Trace Blue, explicitly declining to
   implement a red "destructive" variant, while leaving `ApprovalCard` un-migrated as a
   consequence. **This is a real, unresolved product/design decision, not an implementation
   bug**: does the design system formally adopt a "destructive-action" exception (Alert Red for
   Reject/Cancel/Delete-style actions), or does it hold the One Accent Rule as written and
   redesign these three buttons off color-coding entirely (e.g. via icon + label wording only)?
   **Resolved by §8 decision 1**, same as item 1 above — a formalized exception, `DESIGN.md`
   corrected to state it, no redesign.

3. **`ToolCallRow`'s `border-left` status accent isn't in `DESIGN.md`'s named exception
   list.** `DESIGN.md` names the 3px `border-left` exception for "agent execution cards,
   approval prompts, error banners" / "agent cards, approval card, manager-review card"
   (Elevation §4, Cards/Panels §5). `ToolCallRow` (`App.jsx:36`) uses the identical pattern
   (status-colored 3px left border) but is never named. Functionally it is exactly a
   "status-carrying" element (running/success/failed), so this reads as an incomplete
   enumeration in `DESIGN.md` rather than an implementation violation — but it needs an
   explicit decision (add `ToolCallRow` to the documented exception list) rather than staying
   silently ambiguous. **Resolved by §8 decision 2**: added to the documented list, no visual
   change.

4. **Status-accented card border alpha is a precisely root-caused, already-deferred bug.**
   `frontend/src/theme/tokens.js:195-219`'s own extensive comment documents this exactly:
   `DESIGN.md` states the status-tinted outer border on `AgentCard`/`ManagerReviewCard`
   (via `Panel`'s `accentStatus`) should be "40% alpha," but the shipped code has always used
   the two-character hex suffix `"40"` — which is `64/255 = 25.1%` alpha, not 40%. This is the
   same class of hex-suffix-as-decimal-string transcription slip as the status-badge alpha bug
   that Milestone 1 *did* fix (standardized to a true 20%); this Panel-border instance was
   deliberately left unfixed in Milestone 1, "deferred to a future milestone alongside the
   badge-alpha-style cleanup" (the comment's own words). Confirmed via Milestone 2's disposition
   table as still deferred, "likely Milestone 3/A26." This needs a decision: fix to a true 40%
   (visual change, hex `"66"`), or accept 25.1% as the real intended value and correct
   `DESIGN.md`'s prose instead (documentation-only, no visual change). **Resolved by §8
   decision 3**: the procedure (compare via isolated render, then decide) is approved; the
   specific outcome is determined and recorded in Task 2 (§14) itself.

5. **`App.jsx`'s header has 6 hardcoded color/font values with existing token equivalents**
   (`App.jsx:578` outer shell `background:'#11111b'`/`color:'#cdd6f4'`/hardcoded font-stack
   literal; `:582` `borderBottom:'1px solid #1e1e2e'`; `:587` `<h1>` `color:'#89b4fa'`; `:588`
   subtitle `color:'#585b70'`) — flagged, not fixed, by Milestone 2's Task 1 (explicitly out of
   that task's narrow scope). Direct token equivalents already exist in `theme/tokens.js`
   (`colors.crust`, `colors.text`, `colors.base`, `colors.traceBlue`, `colors.surface2`, and the
   `typography.*.fontFamily` stack). Mechanical, low-risk, in-scope for a consistency pass.
   **The same `#1e1e2e` literal also appears at the Playground grid's left-column border**
   (`App.jsx:646-647`, `isNarrow`-conditional `borderRight`/`borderBottom`) — not part of the
   header count above, but the same token gap; include it in the same tokenization task rather
   than leaving one adjacent instance of the identical value untouched.

6. **`EvalsPage.jsx`'s two Milestone-2-deferred findings** (§8 decision 7 of the Milestone 2
   plan explicitly deferred both "to the later milestone already identified" — this is that
   milestone): the `<code>` chip styling leak (`EvalsPage.jsx:57`, same underlying `index.css`
   rule family Milestone 2's Task 1 fixed for `h1`/`h2`, not yet applied here) and the
   raw-developer-facing empty-suite copy ("No suites yet — create one via `POST
   /api/v1/eval-suites`") that Milestone 2's own corrected §13 explicitly excluded from its own
   scope, naming it as deferred to here.

7. **Two narrower Milestone-2-flagged carry-forwards**, both small and well-scoped:
   `EvalsPage.jsx`'s `resultsError`/`regressionError`/`summaryError` all render with
   `tone="muted"` even when the underlying message is a genuine fetch failure, not a benign
   empty state; and `ObservabilityPage.jsx`'s `ComparisonSide` "Select a task." fallback text is
   slightly inaccurate in a narrow, rare defensive-path edge case.

**Not part of A26, confirmed correctly excluded:** the `Label` 2px vertical offset (Milestone 1,
permanently accepted as-is — a prior fix attempt made it worse, no new attempt proposed); the
still-deferred webfont-loading decision (Milestone 1/2 decision 10 — an A3 identity concern, not
a cross-page consistency one; revisit only if a future milestone reopens A3 directly).

---

## 4. Proposed Milestone 3 Scope

**In scope** — everything in §3.1's numbered list above (7 items), all genuinely unimplemented
or requiring an explicit decision, none reachable by "just verify it" alone:

- Resolve the One-Accent-Rule / destructive-action question (§3.1 items 1-2) and apply the
  resulting decision consistently to `ApprovalCard`'s 4 buttons (§3.1 item 1) and `MemoriesPage`'s
  Delete button (§3.1 item 2, discussed further in the A16 row of §3's table).
- Decide and apply `ToolCallRow`'s border-left documentation status (§3.1 item 3).
- Decide and apply the Panel accent-border-alpha fix (§3.1 item 4).
- Tokenize `App.jsx`'s header's 6 hardcoded values (§3.1 item 5).
- Fix `EvalsPage.jsx`'s `<code>` chip styling and empty-suite copy (§3.1 item 6).
- Fix `EvalsPage.jsx`'s error-tone assignment and `ObservabilityPage.jsx`'s `ComparisonSide`
  edge-case text (§3.1 item 7).

**Explicitly NOT in scope — already complete, no implementation gap, exclude entirely:**

- A9, A10, A11, A13, A14, A16's core restyle work (all done; the *only* open item from each is
  a populated-state verification limitation this milestone cannot lift either, since the
  Anthropic-key limitation is unchanged — see §6 below). Pulling these into Milestone 3 "to
  finish verifying them" would not be new work, just repeating an unreachable check.
- A15 (`remark-gfm`) — fully complete, done in Milestone 2.
- A17-A20 (Observability) — fully complete, verified clean, nothing to do.
- A21 (Evals) — the two named narrow verification gaps are, again, environment-blocked, not
  implementation gaps; not new Milestone 3 work.
- Any re-opening of Milestone 1/2's own closed decisions not directly implicated by the above
  (webfont loading, the `Label` 2px offset, routing/responsive architecture, the A7/A8 content).

This keeps Milestone 3 to what the evidence actually supports: a genuinely bounded "final
consistency pass," matching the backlog's own description of A26's *purpose* far more precisely
than its original one-line scope estimate did.

---

## 5. Explicit Non-Goals

- No re-implementation or re-verification-as-new-work of any A9-A21 item already classified
  Complete or Partially-complete-for-verification-reasons-only above.
- No new backend/API work — every §4 item is a frontend-only styling/token/copy/documentation
  change.
- No change to `ApiKeyPrompt.jsx`, `useRouting.js`, `useViewportWidth.js`, or any Milestone 2
  deliverable's own behavior.
- No formal WCAG AA audit/certification (A25a, permanently deferred per Milestone 2's own
  disposition, unaffected by this milestone).
- No attempt to build a mocked/seeded-data verification harness as *required* scope — see §6's
  explicit treatment of this as an optional, separately-approved opportunity, not a silent
  scope addition.

---

## 6. Architectural Findings

**No new architecture is needed.** Every §4 item is achievable with tools Milestones 1 and 2
already built: the `Panel`/`Button`/`StatusBadge`/`StateText` component system, the
`theme/tokens.js` token set (already has `colors.crust`/`text`/`base`/`traceBlue`/`surface2`
and the typography font stack needed for the header tokenization), and the existing
`index.css` mechanism already used for the `h1`/`h2` fix (directly reusable for `EvalsPage`'s
`<code>` chip).

**`App.jsx`'s single-file risk, restated.** `AgentCard`, `ToolCallRow`, `ApprovalCard`,
`ManagerReviewCard`, `MemoriesPage`, and the main `App()` function (including the header, the
routing hook, the responsive hook, and the Change-key affordance) all live in one file. Every
Milestone 2 task briefing had to state explicit line-range boundaries to avoid one task's
implementer accidentally touching another's territory; the same discipline is required here,
more so, since Milestone 3's own scope (`ApprovalCard`, `MemoriesPage`'s Delete button, the
header) sits directly inside the historically highest-risk file, immediately adjacent to
Milestone 2's own additions (the routing/responsive hooks, the Change-key button) and to
components explicitly excluded from this milestone's scope (`AgentCard`, `ToolCallRow`'s
non-border-left content, `ManagerReviewCard`). A task that opens `App.jsx` to fix the header's
6 hardcoded values must not "helpfully" touch `AgentCard` two hundred lines away just because
the file is already open.

**A genuine verification opportunity, flagged per your explicit request, not assumed into
scope.** No seed mechanism or mock-data path exists today to reach any of the populated
agent-dependent states named throughout §3 (a real `AgentCard` with messages/tool-calls/cost, a
real `ManagerReviewCard`, real populated markdown, a real Memories table row). Building a
small, dev-only fixture/mock-data toggle purely for verification purposes (never shipped, never
touching production code paths) is a real, bounded option that would let Milestone 3's *own*
verification (and future milestones') actually exercise these states live instead of accepting
the same code-comparison-only ceiling every prior milestone has hit. This is presented as an
**optional decision item** (§8) — not folded into scope by default, since it's tooling/test
infrastructure work orthogonal to A26's actual consistency-pass content, and the backlog's own
§0 lock says no new orchestration capability; a dev-only verification fixture doesn't touch
orchestration capability, but it is still new scope that deserves its own explicit yes/no.

**Routing/responsive/sub-tab boundaries, unaffected but worth restating.** None of §4's items
touch `useRouting.js`, `useViewportWidth.js`, or `ObservabilityPage.jsx`/`EvalsPage.jsx`'s
internal `SubNav` state. The `ComparisonSide` text fix (§3.1 item 7) is a string change inside
an already-existing function, not a state-management change, and does not risk coupling
sub-tab state to routing.

---

## 7. Dependencies and Sequencing

| Item | Depends on | Notes |
|---|---|---|
| ApprovalCard One-Accent-Rule decision (§3.1 #1-2) | §8 decision 1 (destructive-action exception, resolved: keep red) | Must be decided before any button-color code change. Decision 6 (found later, during task-breakdown — see §8) separately governs whether `MemoriesPage`'s Delete button and `ApprovalCard`'s Reject button specifically change color |
| ApprovalCard → `Panel` migration | The One-Accent-Rule decision above, if the decision changes button treatment enough to affect the whole card's structure; otherwise independent | Migrating the outer shell onto `Panel accentStatus="WaitingForApproval"` also resolves the border-alpha question for this card for free, once §8's alpha decision is made |
| `ToolCallRow` border-left documentation decision (§3.1 #3) | None — a `DESIGN.md` edit, independent of everything else | Can run first or in parallel with anything |
| Panel accent-border-alpha decision + fix (§3.1 #4) | §8 decision (true 40% vs. correct the doc) | Affects `AgentCard`/`ManagerReviewCard`/(possibly) migrated `ApprovalCard` simultaneously — do this once, not per-component |
| Header tokenization (§3.1 #5) | None | Fully independent, mechanical, lowest risk |
| `EvalsPage` `<code>` chip + empty-suite copy (§3.1 #6) | None | Independent; first time `EvalsPage.jsx` is touched since before Milestone 1 |
| `EvalsPage` tone fix + `ComparisonSide` text fix (§3.1 #7) | None | Independent, smallest items |

**Recommended order (superseded in detail by §14's own task breakdown — kept here for the
high-level rationale):** (1) the `DESIGN.md` documentation decisions first, since the button/
shell code changes depend on them; (2) header tokenization, `ToolCallRow` doc fix, `EvalsPage`
copy/tone fixes in parallel — fully independent, no file overlap risk between them; (3) the
Panel-alpha fix (lands entirely in `tokens.js`/`DESIGN.md`, not `App.jsx` — zero file overlap
with `ApprovalCard`, so it does not need to be bundled into the same pass) followed by
`ApprovalCard`'s own tokenization/migration in `App.jsx`, which does need its own careful
line-range discipline since it sits between `AgentCard` and `ManagerReviewCard` in that file.

**Parallelization:** header tokenization, `EvalsPage.jsx` changes (both items), and the
`ToolCallRow`/`DESIGN.md` doc edit can all run in parallel-dispatched tasks safely — no file
overlap. The `ApprovalCard`/Panel-alpha work should be one task, not split, since it's one
coordinated region of `App.jsx`.

**Regression risks:**
- Editing `App.jsx`'s header (tokenization) sits directly beside Milestone 2's `isNarrow`
  responsive logic and the Change-key button — a task must not touch either.
- Editing `ApprovalCard` sits between `AgentCard` (above) and `ManagerReviewCard` (below) in the
  same file — explicit line-range boundaries are required in the task brief, as established
  practice from Milestone 2.
- Migrating `ApprovalCard` onto `Panel` changes its DOM structure slightly (an extra wrapping
  element potentially) — `ApprovalCard` has no live-reachable state in the running app at all
  (see §11's correction), so this must be verified via an isolated component render (mounting
  the migrated card directly with hand-constructed props), not a live in-app check, to confirm
  no spacing regression.
- `EvalsPage.jsx`'s two independent fixes (chip/copy, tone) should still be one task each with
  clear line boundaries, since `EvalsPage.jsx` hasn't been touched by any implementer since
  before Milestone 1 and its current exact structure needs fresh verification before editing.

---

## 8. Decisions — Final (approved)

Decisions 1-6 below are approved as final for Milestone 3. None is reopened here.

1. **ApprovalCard / destructive-action color exception — APPROVED.** Formalize a documented
   exception for destructive actions in `DESIGN.md`: keep red for destructive/reject actions
   where appropriate. `DESIGN.md`'s One Accent Rule is amended to explicitly name this
   exception so it no longer contradicts the Button section's own destructive-ghost-button
   example. No change to which actions use red — this is a documentation/consistency fix, not a
   redesign.
2. **`ToolCallRow` border-left documentation — APPROVED.** Add it to `DESIGN.md`'s named
   exception list, matching its actual, already-shipped behavior exactly. No visual change.
3. **Panel accent-border alpha — APPROVED PROCEDURE, outcome determined during Task 2.** Before
   deciding, compare the current ~25.1% implementation against a true-40% version via an
   **isolated component render** of `AgentCard`/`ManagerReviewCard` (neither is live-reachable
   in this environment right now — see Task 2's own corrected methodology below and §11 — so
   this is not a live in-app comparison). Use that comparison to determine whether to fix the
   implementation to true 40% or correct `DESIGN.md`'s prose to state 25.1% — not a blind
   decision. The resulting choice is recorded with its comparison evidence in Task 2's own
   report, not re-approved in a second round-trip, since the procedure itself (not a specific
   outcome) is what was approved. **This decision governs `AgentCard`/`ManagerReviewCard`'s own
   border alpha only** — `ApprovalCard`'s current one-off border alpha (37.6%, `#f59e0b60`) is a
   separate, third value not addressed by this decision at all; it changes only as a side effect
   of decision 4's `Panel` migration, discussed there and in Task 3, not resolved here.
4. **ApprovalCard → `Panel` migration — APPROVED, conditional.** Migrate if the result remains
   behaviorally and visually equivalent aside from the explicitly approved alpha correction
   (decision 3, which changes `ApprovalCard`'s border alpha from its prior 37.6% one-off to
   whichever value `AgentCard`/`ManagerReviewCard` now use) **and** the explicitly approved
   Reject-button palette correction (decision 6 below). Preserve existing functionality and DOM
   semantics where practical. Because `ApprovalCard` is not reachable in the running application
   (§11), verification is via an isolated component render with representative props, performed
   *after* the migration lands (§7's sequencing).
5. **Dev-only mock/seed data path — DEFERRED.** Not added as part of Milestone 3. Isolated
   component rendering continues to be the verification method for otherwise-unreachable states.
6. **`ApprovalCard`'s "Reject" button / `MemoriesPage`'s Delete button — Two-Palette-Rule
   correction, APPROVED.** Found while specifying Task 3's exact tokenization: both buttons mix
   the two color palettes on one element — border resolves to `colors.statusFailed` (hex
   `#dc2626`, the **hard** Status palette) while text uses `colors.alertRed` (hex `#f38ba8`, the
   **soft** Catppuccin palette). `DESIGN.md`'s own Two-Palette Rule states: "Two distinct color
   roles never blended... Never use Alert Red for a 'Failed' badge or Status Failed for inline
   error text — the two systems read as different signals and mixing them erodes that
   distinction." **Approved resolution: both buttons use `colors.statusFailed` consistently for
   both border and text**, matching `ApprovalCard`'s own "Confirm Reject" solid-fill button,
   which already correctly uses `colors.statusFailed`. **This is an intentional, explicitly
   approved visual correction — the rendered color of both buttons' borders changes (from a
   mixed hard/soft-red pairing to a single consistent hard-red), not a mechanical,
   appearance-preserving tokenization like the rest of Task 3/4's work.** Name it as such in
   both tasks' reports; do not fold it into the "byte-for-byte identical" framing that governs
   every other hex-to-token conversion in this milestone.

---

## 9. Detailed Implementation Tasks

Superseded by §14 (Implementation Task Breakdown), added once all six §8 decisions were final.
§14's Tasks 1-7 are the authoritative, current task list — this section is kept only as a
pointer, not duplicated here, to avoid two task lists drifting out of sync with each other as
decisions were resolved.

---

## 10. Regression-Risk Analysis

| Risk | Mitigation |
|---|---|
| A task touching `ApprovalCard`/header accidentally edits `AgentCard`/`ToolCallRow`/`ManagerReviewCard`/routing/responsive code nearby in the same file | Explicit line-range boundaries in every task brief, exactly as Milestone 2's §14 did; a task-scoped reviewer checks `git diff --stat` shows only the intended function touched |
| The Panel-alpha fix changes `AgentCard`/`ManagerReviewCard`'s appearance in a way not caught because neither is reachable live in this environment right now (no local backend exists — §2 — and `ManagerReviewCard` never has been, in any milestone) | Isolated component render of both, comparing computed border values at both candidate alpha settings before deciding (Task 2's own method) — not a live check, and not skipped just because live is unavailable |
| `ApprovalCard`→`Panel` migration introduces a DOM/spacing regression | Isolated component render (`renderToStaticMarkup`) comparing the pre- and post-migration shell's computed padding/background/radius/border, since this component has no live-reachable state at all (§11) — not the live pixel/screenshot comparison Milestone 1 used for components that *were* live-reachable |
| `EvalsPage.jsx` edits (untouched since before Milestone 1) hit stale assumptions about its current structure | Fresh direct read of the file immediately before dispatching Tasks 6/7, not reused from any prior milestone's citations |
| Scope creep: "since I'm in `App.jsx` anyway" temptation to also fix A9/A10/A13/A14/A16's already-flagged-but-out-of-scope items | Explicit non-goal statement (§5) in every task brief; final whole-branch review specifically audits for this, as Milestone 2's did |

---

## 11. Verification Strategy

Because this environment still has no valid Anthropic API key (§2), the same discipline
Milestone 2 established applies without change:

- **Live-verifiable without any agent run**: the header tokenization (fully reachable, no task
  needed); `EvalsPage.jsx`'s copy/tone changes for states already reachable today (empty-suite
  view, any error state already exercised live in Milestone 1/2's own history); `ToolCallRow`'s
  border-left doesn't change visually at all under this plan (a doc-only fix), so nothing new
  to verify there beyond confirming no code changed.
- **Isolated component render** (its own category — neither "live in the app" nor "code
  comparison," per Task 2/3's corrected methodology below): `AgentCard`'s `Failed`-status shell
  and `ManagerReviewCard`'s shell (both consumed by the Panel accent-border-alpha fix, decision
  3), plus `ApprovalCard`'s entire button/shell surface (decisions 1, 4, 6). None of these three
  are reachable in the running app *in this environment right now* — `AgentCard`'s `Failed`
  state requires a running local backend (§2 confirms none exists here today, so this is not
  simply "submit a task and watch it fail" the way earlier milestones sometimes could);
  `ManagerReviewCard` has never been live-reachable in *any* milestone regardless of backend
  availability (it needs every agent to succeed first); `ApprovalCard` only renders when
  `approvalRequest` is truthy (`App.jsx:698`), set exclusively by the SSE `approval_required`
  case (`App.jsx:443`), which never fires here. All three are verified the same way: mounting
  the component directly with hand-constructed props, outside the running app, via the same
  `react-dom/server` `renderToStaticMarkup` technique Milestone 2 used for `remark-gfm` — a
  real, legitimate method. If a local backend happens to be available at implementation time
  for unrelated reasons, a live confirmation of `AgentCard`'s `Failed` shell specifically is a
  welcome bonus, not a requirement — do not block on standing one up.
- **Code-compared / structurally verified only**: `AgentCard`/`ManagerReviewCard`'s *populated*
  content (messages, tool calls, cost, saved-memories, the review's own dynamic text) remains
  unreachable exactly as before and is untouched by this milestone's scope regardless — no new
  verification claim is made about it here.
- **Cannot be meaningfully verified until a valid API key and a reachable backend both exist**:
  nothing in this milestone's own scope requires this to be *complete* — every §4 item has at
  least one available verification method (live, isolated-render, or code comparison) — but note
  this is a narrower claim than "reachable live," since three of this milestone's own targets
  (`AgentCard`/`ManagerReviewCard`'s shells, `ApprovalCard` entirely) rely on isolated-render
  precisely because live reachability is currently blocked, not just theoretically limited.
- No agent-dependent populated state will be described as live-verified in this milestone's
  final report unless it actually was — same standard as §3.1a/§11.0 of the Milestone 2 plan.

---

## 12. Definition of Done

- Decisions 1, 2, 4, 5, 6 (§8) are final and reflected in the shipped result; decision 3's
  outcome (§8-3/Task 2) is recorded with its comparison evidence.
- `DESIGN.md`'s One Accent Rule explicitly names the destructive-action exception and no longer
  contradicts the Button section's own destructive-ghost example (decision 1) — a documentation
  change.
- `ApprovalCard`'s Approve/Confirm-Reject/Cancel buttons and its outer shell's non-`Panel`-related
  values are appearance-preserving tokenizations (zero rendered change). **`ApprovalCard`'s
  Reject button and `MemoriesPage`'s Delete button are an intentional exception**: both now
  render `colors.statusFailed` consistently for border and text (decision 6) — a real,
  approved, named visual change resolving the Two-Palette-Rule violation found during this
  milestone's own task-breakdown, reported as a deliberate correction, not folded into the
  "identical to before" claim that covers every other tokenization in this milestone.
- `DESIGN.md` names `ToolCallRow` in its border-left exception list (decision 2) — a
  documentation change only, zero visual change.
- The Panel accent-border-alpha decision (§8-3) is applied consistently to every component using
  `panelAccentBorderAlphaSuffix` (`AgentCard`, `ManagerReviewCard`, and `ApprovalCard` once
  migrated), with `DESIGN.md`'s prose matching the shipped value exactly, whichever way it
  resolved. `ApprovalCard`'s own border alpha changing from its prior one-off 37.6% to this
  value is an expected, approved consequence of the `Panel` migration (decision 4), not a
  regression.
- `ApprovalCard` renders via the shared `Panel` component (decision 4), preserving all existing
  content/functionality/DOM semantics where practical, verified via isolated component render
  (§11 — this component has no live-reachable state in the running app).
- `App.jsx`'s header (and the Playground grid's matching `#1e1e2e` instance) has zero hardcoded
  color/font-family literals with an existing token equivalent.
- `EvalsPage.jsx`'s `<code>` chip renders with the same corrected mono treatment `h1`/`h2`
  received in Milestone 2; its empty-suite copy is revised to match the product voice
  established in Milestone 2's §8a; its `resultsError`/`regressionError`/`summaryError` use
  `tone="error"` where the underlying condition is a genuine fetch failure, `tone="muted"`
  elsewhere.
- `ObservabilityPage.jsx`'s `ComparisonSide` fallback text is accurate for the case it actually
  covers.
- `npm run build`/`npm run lint` clean, matching or explicitly justifying any change to
  Milestone 2's 5-warning baseline.
- **No completion claim states or implies that an agent-dependent unreachable state (a genuine
  `Completed` task, `approval_required`, `manager_review_started`, a live `Failed`-status
  `AgentCard` in this specific environment, populated `AgentCard`/`ManagerReviewCard`/Memories-
  table content) was verified live** — `ManagerReviewCard` has never been live-reachable in any
  milestone, and `AgentCard`'s `Failed` state additionally requires a running local backend that
  does not exist in this environment right now (§2); every such state touched by this
  milestone's own scope (Task 2's alpha comparison, Task 3's `ApprovalCard` migration) is
  verified via isolated component render instead, reported using this document's §3
  classification standard, not glossed over as equivalent to a live check.

---

## 13. Final Reporting Requirements

The Milestone 3 completion report (once implementation is approved and executed) must state,
using the same classification standard as §3 above:

- Every state verified live, with the specific method (screenshot, computed-style, or both).
- Every state verified via **isolated component render** (its own category, per §11 — not
  folded into "live" or "code comparison"), naming which component, what props were
  hand-constructed, and what was confirmed (e.g. `AgentCard`/`ManagerReviewCard`'s alpha
  comparison for Task 2; `ApprovalCard`'s full button/shell surface for Task 3; `MemoriesPage`'s
  Delete button for Task 4).
- Every state verified via code comparison only, and exactly why (unreachable, and why).
- Explicit confirmation that no agent-dependent populated state was described as live-verified.
- The final disposition of each of §8's decisions, with reasoning, exactly as this document's
  own §8 (Milestone 2) and its predecessor did — including decision 6's exact before/after color
  values for both the `ApprovalCard` Reject button and the `MemoriesPage` Delete button, named
  explicitly as an intentional visual correction, not folded into any "unchanged" claim.
- Confirmation that no A9-A21 item outside this milestone's actual §4 scope was touched.

---

## 14. Implementation Task Breakdown

Approved 2026-07-24: decisions 1, 2, 4, 5, 6 final; decision 3's procedure approved (outcome
determined in Task 2 itself, per §8). This section operationalizes §3-§9 into discrete units for
`subagent-driven-development`. Every task inherits these global constraints (repeated in each
dispatch, not just here):

- No new npm dependencies.
- Do not modify `AgentCard`'s or `ManagerReviewCard`'s own JSX beyond the single, scoped
  `panelAccentBorderAlphaSuffix`-driven border value Task 2 touches — no restyling, no
  restructuring, no touching their populated-content branches (messages/tool-calls/cost/memory
  lines, `App.jsx:74-110`).
- Do not touch `ObservabilityPage.jsx`/`EvalsPage.jsx`'s internal `SubNav` sub-tab state
  (`useState('timeline')`/`useState('suites')`) — out of scope, unrelated to this milestone.
- Do not touch `useRouting.js`, `useViewportWidth.js`, or any Milestone 2 behavior.
- Styling: inline `style` objects only, consistent with the codebase's established convention.
- No visual change is permitted unless a task's own brief explicitly says so. Task 1 is
  documentation-only (zero visual change). Tasks 3 and 4's *tokenization* portions (Approve/
  Confirm-Reject/Cancel, and the general hex-to-token conversions) are appearance-preserving —
  but each also carries one explicitly approved, named exception (decision 6's Reject/Delete
  color correction) that both tasks' briefs mark clearly as intentional, not incidental. Task
  2's alpha fix and Task 3's `Panel` migration are permitted to change rendered appearance, only
  in the specific ways their briefs describe. Task 6-partial (the `<code>` chip's font
  correction) is a permitted, named visual change (matching Milestone 2's `h1`/`h2` fix); its
  copy revision is content-only. Task 7 changes only which `tone` value/string is used, not any
  color/layout beyond what those props already control.

**Line references below are current against `main` at `05d4917`, confirmed directly this
session — re-verify immediately before dispatch if any prior task in this sequence has already
landed and shifted them.**

### Task 1: `DESIGN.md` documentation fixes (decisions 1 & 2)

**Scope:** Documentation only. No source file changes.

1. **One Accent Rule exception (decision 1).** Edit `DESIGN.md`'s "The One Accent Rule" (§2,
   currently reads: "Trace Blue is the only color used for interactive affordances (buttons,
   links, focus)."). Add an explicit destructive-action exception, e.g.: "...with one named
   exception: a destructive/reject action (e.g. a Reject or Delete button) may use Alert Red
   instead, matching the Button section's own destructive-ghost pattern below — this is the
   only other color permitted on an interactive affordance." Cross-reference `DESIGN.md`'s
   Button section's existing destructive-ghost example (`#dc262680`) so the two sections agree
   instead of contradicting each other.
2. **`ToolCallRow` border-left exception (decision 2).** Edit `DESIGN.md`'s Elevation §4 named
   exception list ("agent execution cards, approval prompts, error banners") and Cards/Panels
   §5's list ("agent cards, approval card, manager-review card") to also name `ToolCallRow` (or
   a general phrase like "any element carrying a single dominant execution/tool-call status,"
   if that reads more naturally as a rule that already covers it implicitly — writer's
   discretion, but the concrete named list must include tool-call rows one way or the other).

**Do NOT** touch `.impeccable/design.json` unless the writer confirms it also needs a matching
update for consistency (it currently only documents the Status-Accented Card pattern generally,
not a component-by-component list) — flag rather than expand scope if so.

**Dependencies:** None. Can run first, in parallel with Tasks 5-7.

**Definition of Done:** `DESIGN.md`'s One Accent Rule no longer contradicts its own Button
section. `ToolCallRow` (or an equivalently precise general rule covering it) appears in both
named-exception lists. No source file touched.

**Verification method:** Direct documentation review — read the edited `DESIGN.md` sections and
confirm the contradiction named in §3.1 items 1-2 no longer exists. Not applicable to
live/isolated-render verification (no code changed).

**Risk:** None — pure prose edit, zero blast radius on the running app.

---

### Task 2: Panel accent-border-alpha comparison, decision, and fix (decision 3)

**Depends on:** Nothing — independent of Task 1, though logically related. **Must complete
before Task 3** (the `ApprovalCard` migration needs the final alpha value).

**Correction from independent review, important for this task's method:** the original draft
of this task assumed a real `Failed`-status `AgentCard`/`ManagerReviewCard` could be reached
live by simply submitting a Playground task. That's only half right. `AgentCard` genuinely can
reach `Failed` live — **but only if a full local backend (the .NET API + Postgres) is actually
running**, which §2 confirms is **not** the case in this environment right now (no local
backend process, no reachable production URL). Standing up that full stack is a real, separate
lift (per the README's Quick Start) that this frontend-only milestone should not be blocked on.
**`ManagerReviewCard` is a different case entirely — it has never been live-reachable in any
milestone** (§3's A13 row): its only two `accentStatus` values are `WaitingForApproval`/
`Completed` (`App.jsx:212`), both requiring every agent to succeed first, which never happens
here regardless of backend availability. So this task's primary method is **isolated component
render** for both components, matching Task 3's own technique — not a live in-app check.

**Scope:**
1. Using the same `react-dom/server` `renderToStaticMarkup` technique as Milestone 2's
   `remark-gfm` check and Task 3's `ApprovalCard` verification: render `AgentCard` with a
   hand-constructed `execution` prop (`status: 'Failed'`, no messages/tool-calls/cost needed —
   only the shell/border is being compared) at the current `panelAccentBorderAlphaSuffix = '40'`
   (25.1%, `frontend/src/theme/tokens.js:219`). Render again with the constant temporarily
   changed to `'66'` (true 40%, `102/255`). Do the same for `ManagerReviewCard` with a
   hand-constructed `review` prop (`status: 'running'` and `status` anything-else, to see both
   its `WaitingForApproval`/`Completed` accent colors at both alpha values).
2. **Decide** based on the rendered/computed comparison (not a subjective screenshot judgment,
   since this is a server-render, not a browser screenshot — compare the actual computed
   `border`/`borderLeft` CSS values in the rendered HTML, or use a headless-Chrome screenshot of
   the isolated render if that's easier to reason about visually): does 25.1% look like the
   clearly-intended, deliberate, understated value consistent with this system's flat/dense
   aesthetic (favor correcting `DESIGN.md`'s prose to state 25.1%, leaving the constant
   unchanged) — or does true 40% look like an unremarkable, natural strengthening with no visual
   problem (favor changing the constant to `'66'` and leaving `DESIGN.md`'s "40%" prose as-is,
   correct all along)? Record the comparison and the reasoning in the task report — this is the
   evidence-based determination §8 decision 3 authorizes this task to make, not a second
   approval round-trip.
3. Apply the decided fix: **either** change `panelAccentBorderAlphaSuffix` in
   `frontend/src/theme/tokens.js:219` from `'40'` to `'66'` and update its own explanatory
   comment (`:195-218`) to reflect the correction, **or** leave the constant untouched and edit
   `DESIGN.md`'s Cards/Panels §5 (currently states "40% alpha") to read "≈25% alpha," removing
   the stated-but-uncorrected-intent language from `tokens.js`'s comment (since it would no
   longer be describing an open discrepancy).

**Do NOT** touch `AgentCard`'s/`ManagerReviewCard`'s JSX itself — the fix is entirely in
`tokens.js` (or `DESIGN.md`), consumed automatically by `Panel`'s existing `accentStatus`
mechanism. The hand-constructed props used for comparison in step 1 are verification-only
scaffolding, not a permanent addition to the codebase.

**Definition of Done:** One value change (code or doc, not both) with the isolated-render
comparison and stated reasoning in the report. `tokens.js`'s comment and `DESIGN.md`'s prose
agree with each other and with the shipped value, whichever way this resolved.

**Verification method:** **Isolated component render** for both `AgentCard` and
`ManagerReviewCard` — neither is live-reachable in this environment right now (`AgentCard`'s
`Failed` state needs a running local backend, which doesn't exist here today; `ManagerReviewCard`
has never been live-reachable in any milestone, backend or not). If a local backend happens to
be available at implementation time for unrelated reasons, a live confirmation is a welcome
bonus, not a requirement — do not block this task on standing one up.

**Risk:** If the constant changes, `AgentCard`/`ManagerReviewCard`'s border becomes marginally
more visible everywhere they render — confirm this doesn't clash with anything nearby (the
`StatusBadge` inside the same card uses a *different*, already-correct 20% background alpha —
unaffected either way).

---

### Task 3: `ApprovalCard` tokenization + Reject-button palette correction + `Panel` migration (decisions 1, 4, 6)

**Depends on:** Task 1 (needs `DESIGN.md`'s exception documented first, per your explicit
sequencing instruction) and Task 2 (needs the final alpha value before migrating onto `Panel`).

**Scope — three parts, done together since all touch the same lines:**

**Part A — appearance-preserving tokenization** (Approve/Confirm-Reject/Cancel/outer-shell only
— Reject is handled separately in Part A′ below, since it is *not* appearance-preserving).
Convert every raw hex/literal to its token equivalent, changing **zero** rendered pixels:
- `:121-128` outer div: `border: '1px solid #f59e0b60'` → `` `1px solid ${colors.statusWarning}60` `` (or the exact existing alpha-suffix token if one matches `60` precisely — check `tokens.js` for a named suffix before hardcoding a new bare string); `borderLeft: '3px solid #f59e0b'` → `` `3px solid ${colors.statusWarning}` ``; `background: colors.base` (already a token, no change).
- `:141-151` Approve: `background: colors.statusCompleted` (already a token); `color: colors.crust` (already a token) — no change needed, already clean.
- `:174-184` Confirm Reject: `background: colors.statusFailed` (already a token) — no change needed.
- `:185-196` Cancel: `border: '1px solid #313244'` → `` `1px solid ${colors.surface0}` ``.

**Part A′ — Reject-button Two-Palette-Rule correction (decision 6, APPROVED — an intentional
visual change, not a tokenization).** `:152-162`'s Reject button currently renders
`border: '1px solid #dc262680'` (hex `#dc2626` = `colors.statusFailed`, hard palette) paired
with `color: colors.alertRed` (soft palette) — a mix `DESIGN.md`'s Two-Palette Rule forbids.
Change **both** to `colors.statusFailed`: border becomes
`` `1px solid ${colors.statusFailed}${buttonGhostBorderAlphaSuffix}` `` (reusing the existing
`'80'`/50%-alpha suffix, `frontend/src/theme/tokens.js:231`, for the same visual weight as
today's border, just the correct hue) and `color: colors.statusFailed` (was `colors.alertRed`).
This **does change the rendered text color** (from `#f38ba8` to `#dc2626`) — call this out
explicitly and separately from Part A's byte-for-byte conversions in the task report; it is not
covered by "renders identically to before." The result exactly matches "Confirm Reject"'s own
already-correct `colors.statusFailed` fill, pairing the two steps of the same action under one
consistent hue, per decision 6's approved rationale.

**Part B — migrate the outer shell onto `Panel`.** Replace `ApprovalCard`'s hand-rolled outer
`<div style={{border, borderLeft, borderRadius, padding, marginBottom, background}}>` with
`<Panel accentStatus="WaitingForApproval" style={{padding: '14px 16px', marginBottom: 14}}>`
(reusing the same `accentStatus` key `ManagerReviewCard` already reuses for its own
running/completed color mapping, per `App.jsx:212`'s established precedent) — this
automatically produces the correct border/border-left using Task 2's now-decided alpha value,
removing the need for Part A's outer-div token conversion (superseded by the migration; do Part
A on the buttons only if the migration lands in the same pass, to avoid tokenizing a div about
to be deleted). Preserve every child element (the header row, the plan text, the agent-list/mode
text, the button row, the reject-note `TextArea`) exactly as-is inside the new `Panel` wrapper —
this is a shell swap, not a content change.

**A note on the outer border, so the migration's own effect isn't confused with Task 2's fix:**
`ApprovalCard`'s current shipped border alpha (`#f59e0b60` = `96/255` ≈ 37.6%) is a *third*
value, equal to neither Panel candidate (25.1% or true 40%). Migrating onto `Panel` changes this
card's border alpha regardless of which way Task 2 resolved — this was always an inevitable,
approved side effect of decision 4 (the migration), not something decision 3 (Panel's own alpha)
governs by itself. State this plainly in the report: "before: 37.6%, after: [25.1%|40%,
whichever Task 2 decided], changed because of the `Panel` migration, not a color regression."

**Definition of Done:**
- Approve/Confirm-Reject buttons: no change (already token-based).
- Cancel button: hex converted to token, rendered color byte-for-byte identical to before.
- Reject button: border and text both now `colors.statusFailed` — an **intentional, approved
  visual change** (decision 6), not a tokenization; the report states the exact before/after
  color values (text: `#f38ba8` → `#dc2626`) and names this as a deliberate correction, not an
  incidental side effect of the surrounding tokenization work.
- Outer shell: renders via `Panel accentStatus="WaitingForApproval"`; its border alpha now comes
  from `panelAccentBorderAlphaSuffix` (Task 2's decided value) rather than the card's own
  previous one-off 37.6% — an expected, approved consequence of the migration itself, not a
  second alpha decision.
- All child content (header, plan/agent/mode text, buttons, reject-note textarea) present and
  unchanged.

**Verification method:** **Isolated component render** (the only method available — `ApprovalCard`
has no live-reachable state in the running app at all, per §11). Mount `ApprovalCard` directly
with hand-constructed representative props (a plausible `request` object with `plan`/
`selectedAgents`/`executionMode`, `onApprove`/`onReject` no-op callbacks, `busy: false`), render
via the same `react-dom/server` `renderToStaticMarkup` technique Milestone 2 used for
`remark-gfm`, and confirm: (a) Approve/Confirm-Reject/Cancel's rendered HTML/computed values
before vs. after are identical (a real diff, not just "looks the same"); (b) Reject's rendered
border/text color both resolve to `colors.statusFailed`'s hex (`#dc2626`) — a genuine change
from the "before" render, confirmed as intentional, not flagged as a discrepancy; (c) the
migrated `Panel`-based shell's rendered padding/background/radius match the pre-migration
shell's values exactly, and its border alpha matches Task 2's decided `panelAccentBorderAlphaSuffix`
value (explicitly not the card's own prior 37.6% — that value is expected to change, per this
task's own note above); (d) all buttons and the reject-note textarea still render and their
`onClick`/`onChange` wiring is intact by code inspection (a server-render can't execute a click,
so click-behavior itself is confirmed by reading `handleSubmit`-equivalent code paths, not by
simulating a click).

**Risk:** `ApprovalCard` sits between `AgentCard` (above, `:58-114`) and `ManagerReviewCard`
(below, `:203-229`) in the same file — a `git diff --stat` after this task must show changes
confined to lines 116-201 only. The `Panel` migration may introduce one extra wrapping element
in the DOM (Panel renders its own outer `<div>`); confirm this doesn't break any parent-level
layout assumption (`ApprovalCard`'s own call site, wherever it's rendered in `App()`, just
expects one top-level element back — check that call site briefly for any layout assumption
before finishing).

---

### Task 4: `MemoriesPage` Delete-button Two-Palette-Rule correction (decisions 1, 6)

**Depends on:** Task 1 (same reasoning as Task 3 — the exception should be documented before
any destructive-action color code is touched).

**Scope:** `App.jsx:295-300`, the Delete button inside `MemoriesPage`'s populated table row.
This button has the **identical** border/text palette-mixing as `ApprovalCard`'s Reject button
(`border: '1px solid #dc262680'` = `colors.statusFailed` hard-palette hex, `color: '#f38ba8'` =
`colors.alertRed` soft-palette hex) — decision 6 (§8) resolves both together, not as separate
questions. Apply the same **intentional, approved visual correction**: change both to
`colors.statusFailed` — border becomes
`` `1px solid ${colors.statusFailed}${buttonGhostBorderAlphaSuffix}` `` and
`color: colors.statusFailed` (was `'#f38ba8'`). **This changes the rendered text color** (from
`#f38ba8` to `#dc2626`) — name it explicitly as the same approved correction Task 3 applies to
`ApprovalCard`'s Reject button, not an incidental tokenization side effect.

**Definition of Done:** Delete button's border and text both resolve to `colors.statusFailed`;
the report states the exact before/after color values and names this as the same intentional
decision-6 correction as Task 3's, applied for cross-page consistency between the two
functionally-equivalent destructive actions.

**Verification method:** This state IS reachable live in principle (the populated Memories
table renders once real memory rows exist) but is **not reachable in this environment** (no
memory has ever been saved here, and no backend is currently reachable at all — §2). Verify via
**isolated component render** (the same `react-dom/server` technique as Task 3 — render the
Delete button's markup/styles directly with representative props) to confirm the new color
values resolve correctly, since a live check is not possible; direct code comparison alone is
not sufficient here because the whole point is confirming an intentional *change* renders
correctly, not that something stayed the same.

**Risk:** Low — a 5-line change in an otherwise-untouched function, no structural change. Do not
touch any other cell in the same table row (agent/key/value/importance/updated columns,
`:289-293`) or the table's loading/empty/error states (`:266-273`) — this task is the Delete
button only.

---

### Task 5: `App.jsx` header tokenization

**Depends on:** Nothing — fully independent, can run in parallel with any other task.

**Scope:** `App.jsx:578` (outer shell `background:'#11111b'` → `colors.crust`,
`color:'#cdd6f4'` → `colors.text`, the hardcoded `fontFamily` literal → the existing
`typography.*.fontFamily` stack constant, whichever token already exposes it verbatim — check
`theme/tokens.js` for the exact export name before introducing a new one); `:582`
(`borderBottom:'1px solid #1e1e2e'` → `` `1px solid ${colors.base}` ``); `:587` (`<h1>`
`color:'#89b4fa'` → `colors.traceBlue`); `:588` (subtitle `color:'#585b70'` → `colors.surface2`).
Also `:646-647` (Playground grid's `isNarrow`-conditional `borderRight`/`borderBottom`, both
using the same `#1e1e2e` literal → `colors.base`, matching `:582`'s fix for consistency).

**Definition of Done:** Zero hardcoded color/font-family literals remain in the header block or
the Playground grid's left-column border; every value renders byte-for-byte identical to before.

**Verification method:** **Live** — this entire region renders on every page load, reachable
without any agent run or backend. Screenshot before/after, confirm 0-pixel diff, and/or
`getComputedStyle` the affected elements to confirm identical resolved values.

**Risk:** This code sits directly beside Milestone 2's `isNarrow` responsive logic and the
Change-key button (`App.jsx:577-632` region) — confirm neither is touched; a `git diff --stat`
should show only color/font-family value changes, no structural/logic changes.

---

### Task 6: `EvalsPage.jsx` `<code>` chip styling + empty-suite copy

**Depends on:** Nothing — independent. First time `EvalsPage.jsx` is touched by any implementer
since before Milestone 1; re-verify the exact current line numbers immediately before starting,
don't reuse any prior milestone's citations blind.

**Scope:**
1. `EvalsPage.jsx:55-59`'s `<code>POST /api/v1/eval-suites</code>` chip — apply the same
   `index.css` mono/typography correction Milestone 2's Task 1 applied to `h1`/`h2` (the shared
   `code { ... background: var(--code-bg); ... }` rule at `index.css`'s bottom, currently
   unaffected by that fix — confirm its `font-family` inherits correctly or needs the same
   `var(--mono)` treatment).
2. The surrounding sentence "No suites yet — create one via `POST /api/v1/eval-suites`." —
   revise to match the product voice established in Milestone 2's §8a (direct, technical,
   no marketing language) while keeping the underlying technical fact (the exact endpoint/method)
   intact — e.g., stating what a suite is and how to create one without reading as a raw API hint
   dropped into the UI.

**Definition of Done:** The `<code>` chip renders in the corrected mono style (matching `h1`/
`h2`'s Milestone 2 fix); the empty-suite copy reads as considered product copy, not a raw API
call, while still stating the actual creation path accurately.

**Verification method:** **Live** — the empty-suites state is the default state of `EvalsPage`'s
`SuitesView` before any suite exists, reachable without any agent run or backend (an empty list
is what renders when the fetch returns nothing, or even before it resolves, depending on exact
loading-state handling — confirm which and verify that exact reachable state).

**Risk:** `EvalsPage.jsx`'s internal `SubNav` state (`:29-48`, top-level `useState('suites')`
`:477`) must not be touched — this task only touches `SuitesView`'s copy/styling, not its state
management.

---

### Task 7: `EvalsPage.jsx` error-tone fix + `ObservabilityPage.jsx` `ComparisonSide` text fix

**Depends on:** Nothing — independent, can run in parallel with Task 6 (different regions of
the same file, low overlap risk, but still confirm no line collision before dispatching both at
once) and with any other task.

**Scope:**
1. `EvalsPage.jsx:224-226` (`resultsError`), `:301-303` (`regressionError`), `:442`
   (`summaryError`) — all currently render via `<StateText tone="muted">`. Change to
   `tone="error"` for whichever of these represent a genuine fetch/request failure (as opposed
   to a benign "no baseline set" style message) — read each one's actual trigger condition in
   context before changing; not every message under these three names is necessarily a true
   error, per Milestone 2's own note that this needs judgment, not a blanket find-replace.
2. `ObservabilityPage.jsx:522` (`ComparisonSide`'s "Select a task." fallback) — the message is
   inaccurate in the rare case it actually covers (reachable when the compare API returns
   truthy `data` but a falsy `data.first`/`data.second` after both tasks are already selected).
   Revise the text to match that actual condition rather than implying no task has been picked
   yet.

**Definition of Done:** Genuine fetch-failure states use `tone="error"`; benign states keep
`tone="muted"`; `ComparisonSide`'s fallback text accurately describes the condition it covers.

**Verification method:** `resultsError`/`regressionError` were previously live-triggered in
Milestone 1/2's own verification passes (one genuinely, one via the sibling pattern) — **live**
re-verification is possible by repeating that same trigger (selecting a run with no baseline
set, for the regression case). `summaryError` and the `ComparisonSide` rare-edge-case text are
**code-compared / structurally verified only** — the latter is a genuinely rare defensive path
that may not be practically triggerable even live; state this plainly rather than claiming a
live check that didn't happen.

**Risk:** Do not touch `EvalsPage.jsx`'s or `ObservabilityPage.jsx`'s `SubNav`/sub-tab state
management — only the named `StateText` `tone` props and one string literal.

---

## Sequencing Summary

```
Task 1 (DESIGN.md docs) ──┬──────────────────────────────┐
                          │                              │
Task 2 (Panel alpha:      │                              │
  compare → decide → fix) ┴──► Task 3 (ApprovalCard:      │
                               tokenize + Panel migrate    │
                               + isolated-render verify)   │
                                                            │
Task 1 also gates ──────────► Task 4 (MemoriesPage Delete  │
                               tokenization)                │
                                                            │
Task 5 (header tokenization) ─────────────────────── independent, any time
Task 6 (EvalsPage code chip + copy) ───────────────── independent, any time
Task 7 (EvalsPage tone + ComparisonSide text) ──────── independent, any time
```

Recommended dispatch order: Task 1 first (unblocks 3 and 4); Task 2 next, in parallel with Tasks
5, 6, 7 (all independent of Task 1/2 and each other); Task 3 last among the "gated" chain (needs
both 1 and 2 complete), with its own isolated-render verification as its final internal step;
Task 4 any time after Task 1.

---

*Milestone 3 task breakdown updated 2026-07-24, all six §8 decisions final and approved,
including decision 6's Reject/Delete palette correction. Investigation and planning only — no
source/application files were modified, no implementation worktree was created.
`main`/`origin/main` remain unchanged at `05d4917`. Nothing was committed or pushed. Awaiting
your explicit approval before any Milestone 3 implementation begins.*
