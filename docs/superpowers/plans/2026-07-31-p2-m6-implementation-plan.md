# P2-M6 — Responsive + A11y Hardening — Implementation Plan

- **Base:** `main` @ `ca743d2` · **Branch:** `worktree-p2-m6-responsive-a11y-hardening`
- **Spec:** `docs/superpowers/specs/2026-07-31-p2-m6-responsive-a11y-hardening-design.md`
- **Status:** Planning only. NO CODE until this plan is approved.

---

## Part A — Critical review of the spec (challenged as a senior engineer)

The spec is **fundamentally sound** — correct blast-radius-first ordering, honest protected-area handling, real acceptance criteria, and it reuses in-codebase reference patterns rather than inventing. But a design pass is not an implementation pass, and the following gaps/risks would bite during implementation. Ten findings; six change the plan.

**A1 — [CORRECTION] Enabling jsx-a11y as a blocking gate in M6.1 would make CI red for the whole milestone.** The spec says "enable early so all later work is gated." But jsx-a11y flags pre-existing issues across M6.2/M6.3/M6.4 surfaces *not yet fixed*, so `npm run lint` fails from M6.1 until the final task. **Fix:** enable jsx-a11y at **`warn`** in M6.1 (visible, non-blocking), flip to **`error`** (blocking CI gate) in M6.5 once every surface is clean.

**A2 — [CORRECTION] O5 "SubNav → ARIA tablist" has hidden blast radius and is disproportionate.** `NavItem` is a shared primitive used by the primary Sidebar nav (`aria-current="page"`) *and* both SubNav bars (verified). Full `tablist`/`tab`/`aria-selected`/roving-tabindex/`tabpanel` semantics would require forking `NavItem` (blast radius: Sidebar + 2 SubNavs + their tests) and `aria-current="page"` is wrong for a tab. The SubNav items are **already** real `<button>`s with `aria-current` — that is WCAG-AA-conformant. **Fix:** DOWNGRADE — keep buttons + `aria-current`, wrap in `role="group"` + `aria-label`, and add the responsive **`flexWrap`** (the actual gap). Do NOT touch `NavItem`'s role semantics. Full tabs are out of scope (not required for AA; disproportionate risk).

**A3 — [CLARIFICATION] The global `:focus-visible` fallback does NOT rescue the `outline:none` selects.** Inline `style={{outline:'none'}}` beats a stylesheet `:focus-visible` rule (inline specificity). So O3's selects still need their inline `outline:none` removed/replaced explicitly; the global fallback (G2) only helps controls with no explicit outline handling. Both fixes are needed and independent.

**A4 — [CLARIFICATION] axe-core in jsdom cannot check color-contrast or responsive layout.** vitest-axe covers *structural* a11y (labels, roles, ARIA, alt) only. Contrast (G3/G5) and responsive/overflow are **not** machine-checkable in jsdom — they rely on the Playwright + manual layer. The plan must not over-claim axe coverage.

**A5 — [RISK/PROCESS] The `Input`/`Label` change is a shared primitive with confirmed test-breakage.** Existing tests query `getByText('Approve ✓')`, `'Reject ✗'`, `'Confirm Reject'`, `'Agent Executions'`, etc. Changing label DOM (span→`<label>`), headings (Label→h2), and glyph wrapping will affect some queries. **Fix:** every task that changes DOM structure updates the affected tests and re-runs the full 65-test suite as part of its own acceptance — updating a test to match an intentional a11y improvement is expected, but must be deliberate, not incidental.

**A6 — [FROZEN PRECISION] `ObservabilityPage.jsx` contains BOTH frozen and editable code in one file.** Frozen: `SpanRow` (166–213) and the `TimelineView` graph-render block (~215–440). Editable: Summary/Dashboard/Errors/Compare/SubNav/TaskPicker. So M6.3 edits the *same file* as the freeze. **Fix:** every M6.3 task specifies exact line ranges, and a **per-task `git diff` audit confirming zero hunks in the frozen ranges** is a hard acceptance check (P2-M5 already modified this file safely for the handoff — same discipline).

**A7 — [SEQUENCING] `App.jsx` is edited by BOTH M6.2 (Playground) and M6.4 (Memories).** Parallelizing them risks in-file churn/conflicts. **Fix:** sequence M6.4 after M6.2 (or strictly partition by line range). Not truly parallel.

**A8 — [SCOPE] Playwright stays LOCAL/manual, not in CI.** It's not a repo dependency (scratchpad-only, as in P2-M5). Adding browser installs to CI is a bigger lift and out of scope. **The CI gates are jsx-a11y (rides `npm run lint`) + axe (rides `npm test`)**; Playwright is the local integration-verification tool. State this so we don't imply Playwright-in-CI.

**A9 — [CONFIRM] P4 (inline-code) is a genuine correctness bug (verified), not just a11y.** Fixing it changes rendering (inline code stops becoming `<pre>`). That's a bug-fix inside the a11y/polish envelope — acceptable and desirable — but flag it as a *behavior change* so it's not mistaken for scope creep.

**A10 — [DESIGN REFINEMENT] Live region: pick ONE mechanism, not both.** `role="log"` on the feed (announces every card) *and* a separate polite status line would double-announce. **Fix:** a single visually-hidden `aria-live="polite"` **status line** reflecting phase transitions (task started / agent N started / agent N completed / manager review / task completed) is the primary mechanism; do NOT also put `role="log"` on the feed. Verify with a real screen reader (AT pass) — jsdom can assert the text content updates, not the announcement UX.

**Net:** the spec's direction is correct; A1, A2, A5, A6, A7, A10 change *how* we execute. None change *what* the milestone is.

## Part B — Confirmations

**Frozen areas — CONFIRMED untouched (per-task `git diff` audited):**
`observability/*` renderers (`layout.js`, `AgentNode.jsx`, `ToolCallNode.jsx`, `TraceEdge.jsx`, `timelineTheme.js`) · frozen Timeline render block + `SpanRow` in `ObservabilityPage.jsx` · `DetailPanel.jsx` (reference only) · backend `src/**` · SSE contracts · database · migrations · API contracts · `theme/tokens.js` **values** · `theme/typography.js` type-scale (rem deferred).

**Decisions — CONFIRMED unchanged:**
✓ Full phone support (~360px) · ✓ CompareView collapse · ✓ Responsive graph height · ✓ No page-level horizontal overflow · ✓ 44px touch targets (where practical) · ✓ rem typography DEFERRED · ✓ jsx-a11y enabled (warn→error per A1) · ✓ axe-core tests included.

---

## Part C — Sub-milestone plans

### M6.1 — Foundation: shared primitives + global a11y
- **Objective:** land the highest-blast-radius fixes (labels, focus, contrast, skip-link) + stand up the a11y tooling, so every later surface builds on a hardened base and is auto-checked.
- **Files expected to change:** `components/Input.jsx`, `components/Label.jsx`, `index.css`, `components/NavItem.jsx`, `App.jsx` (shell/Brand/TopBar metadata only), `components/shell/AppShell.jsx`, `.oxlintrc.json`, `package.json` (+`package-lock.json`), `src/test/setup.js` (+ a small `src/test/axe.js` helper). Affected tests: `components.test.jsx` and any asserting Input/Label DOM.
- **Must NOT change:** `theme/tokens.js` values, `typography.js`, `observability/*`, frozen `ObservabilityPage` block, `DetailPanel.jsx`, backend, CI job logic (only the lint config's rule severity).
- **Blast radius:** HIGH (Input/Label/NavItem/global CSS touch every surface). This is deliberate — verify broadly.
- **Risks:** Input DOM change breaks tests (A5); global focus rule double-ringing (mitigate via `.focus-ring` specificity); jsx-a11y noise (warn-only, A1).
- **Acceptance:** every form field has an associated label (axe: no `label`-rule violations); every control has a visible focus ring; nav text ≥4.5:1 (manual/Playwright); skip-link works (keyboard); jsx-a11y runs at `warn`; vitest-axe harness executes; full suite green (tests updated as needed).
- **Deterministic tests:** vitest-axe on a representative form; user-event test asserting label→field focus association and skip-link focus target.
- **Playwright:** keyboard tab from page top reaches skip-link → main; visible focus ring on nav + inputs at desktop.
- **A11y verification:** axe (labels/roles) + manual contrast check on nav/metadata + AT check of skip-link.
- **Responsive verification:** n/a (foundation) — but confirm no layout change from the CSS additions at 360/768/1280.
- **Regression risks:** contrast token swap could reduce visual hierarchy (verify design intent); label DOM change could shift form spacing (screenshot compare).

### M6.2 — Playground a11y & responsive
- **Objective:** make the live execution experience accessible (announcements, focus, headings) and phone-usable; fix the P4 render bug.
- **Files:** `App.jsx` (Playground + ApprovalCard + AgentCard/ToolCallRow + result markdown regions), `playground/PlaygroundExecutionGraph.jsx`. Tests: `App.playgroundGraph.test.jsx`, `ManagerReviewCard.test.jsx`, `components.test.jsx` (ApprovalCard).
- **Must NOT change:** `observability/*` (graph primitives imported by PlaygroundExecutionGraph), `theme` values, backend/SSE.
- **Blast radius:** MED-HIGH (App.jsx is large; changes are localized to Playground regions with line-range boundaries).
- **Risks:** live-region UX (A10); focus-timing regressions on approval; App.jsx in-file churn (A7 sequencing); glyph/heading edits break `getByText` queries (A5).
- **Acceptance:** SSE phase transitions announced via one polite status line; `approval_required` moves focus + announces; inline code renders inline (P4 fixed); result headings start at h2; result tables scroll-in-container (no page overflow ≤360px); graph height responsive + container labeled; no desktop/tablet regression.
- **Deterministic tests:** mocked-EventSource drive (P2-M5 pattern) asserting the status-line text updates per phase + focus lands on ApprovalCard on `approval_required`; markdown test asserting inline backtick → `<code>` not `<pre>`; vitest-axe on the Playground.
- **Playwright:** phone (375) — no horizontal overflow, graph usable, feed readable; keyboard walk of form→run→approval.
- **A11y:** AT pass of the streaming announcements + approval focus (the one gap unit tests can't fully cover).
- **Responsive:** 375/768/1280 — grid stacks, graph height adapts, result tables scroll internally.
- **Regression:** ensure the additive live-region doesn't disrupt the existing card feed; graph fit behavior (P2-M5) unchanged.

### M6.3 — Observability (editable) & Evals
- **Objective:** label + focus every select, make the chart accessible, collapse Compare on phone, fix Evals mouse-only rows, table semantics, SubNav responsive — **without touching the frozen Timeline block**.
- **Files:** `ObservabilityPage.jsx` (editable sub-views ONLY), `EvalsPage.jsx`, `components/NavItem.jsx` (only if a `role="group"` wrapper is added at call sites — preferably no NavItem change per A2). Tests: `ObservabilityPage.test.jsx`, `EvalsPage.test.jsx`.
- **Must NOT change:** `ObservabilityPage.jsx` frozen ranges (`SpanRow` 166–213; TimelineView graph render ~215–440), `observability/*`, `DetailPanel.jsx`.
- **Blast radius:** MED (two page files; per-view localized).
- **Risks:** editing the same file as the freeze (A6 — line-range discipline); `BarChart` off-screen table doubles data render (low); CompareView collapse must reuse existing `useViewportWidth` (stay consistent, no CSS media queries for layout, A? consistency).
- **Acceptance:** every select labeled + visibly focusable; BarChart has `role="img"`+summary+off-screen table; tables have `th scope`+caption; SubNav wraps at 360px with a group label; CompareView single-column ≤768px; Evals suite rows are keyboard-operable buttons; **`git diff` shows zero hunks in frozen ranges**.
- **Deterministic tests:** vitest-axe per sub-view; user-event test that Evals suite rows activate via keyboard; test that selects have accessible names.
- **Playwright:** phone — Compare stacks, SubNav wraps, tables scroll-in-container, no page overflow.
- **A11y:** AT pass of the chart alternative + tab semantics.
- **Responsive:** 360/768/1280 across all 4 editable Observability sub-views + 4 Evals tabs.
- **Regression:** frozen Timeline byte-identical; existing Observability/Evals tests green.

### M6.4 — Memories & onboarding
- **Objective:** fix the destructive Delete affordance (focus/name/focus-management) + onboarding autofocus.
- **Files:** `App.jsx` (MemoriesPage region ONLY), `ApiKeyPrompt.jsx`. Tests: add a Memories delete-focus test.
- **Must NOT change:** anything M6.2 touched (sequence after M6.2 — A7).
- **Blast radius:** LOW.
- **Risks:** focus-after-delete timing; App.jsx sequencing (A7).
- **Acceptance:** Delete has focus-ring + per-row `aria-label`; focus moves to a stable target after row removal; `th scope`; onboarding input autofocused.
- **Deterministic tests:** user-event test deleting a row asserts focus lands on a defined element (not `<body>`); axe on Memories + onboarding.
- **Playwright:** phone — memories table scrolls-in-container; onboarding card fits.
- **A11y:** AT check of per-row Delete names + post-delete focus.
- **Responsive:** 360/768 for Memories + onboarding.
- **Regression:** memory delete/refresh flow unchanged functionally.

### M6.5 — Verification, CI & regression hardening
- **Objective:** flip the gate on, prove the whole thing, lock against regressions.
- **Files:** `.oxlintrc.json` (warn→error), test files (axe + user-event suites), CI workflow only if a step needs adding (jsx-a11y already rides `npm run lint`; axe rides `npm test` — likely no CI-yaml change needed).
- **Must NOT change:** production source (this milestone is tests/config only); frozen areas.
- **Blast radius:** LOW (config/tests).
- **Risks:** flipping jsx-a11y to error surfaces a stray unfixed finding → triage/waive.
- **Acceptance:** jsx-a11y `error` gate green (0 unwaived); axe 0 violations on all audited surfaces; full suite green (65 + new); Playwright 3-width walkthrough clean; documented manual contrast+AT pass; scope audit clean; protected files byte-identical; build+lint exit 0.
- **Tests/Playwright/A11y/Responsive:** the consolidated runs described above.
- **Regression:** the whole existing suite + a final `git diff main` scope audit.

---

## Part D — Task breakdown (each independently reviewable)

Fields per task: **Purpose · Files · Diff size · Risk · Dependencies · Verification · Rollback.**

### M6.1
- **M6.1.1 — Input/Label label association.** Purpose: `useId`-associate every field label (G1). Files: `Input.jsx`, `Label.jsx`, `components.test.jsx`. Diff: ~40 lines. Risk: MED (shared, test breakage). Deps: none. Verify: axe label rule passes; user-event label-click focuses field; suite green. Rollback: revert 2 files (self-contained).
- **M6.1.2 — Global `:focus-visible` fallback.** Purpose: safety-net focus ring (G2). Files: `index.css`. Diff: ~6 lines. Risk: LOW (specificity keeps `.focus-ring` winning; does not affect inline `outline:none` — A3). Deps: none. Verify: Playwright focus-ring visible on a control lacking `.focus-ring`. Rollback: revert CSS block.
- **M6.1.3 — Contrast fixes (consumer-side).** Purpose: fix nav AA fail + metadata (G3/G5). Files: `NavItem.jsx`, `App.jsx` (Brand + TopBar metadata lines). Diff: ~6 lines. Risk: LOW (token ref swap; no tokens.js change). Deps: none. Verify: manual/Playwright contrast ≥4.5:1. Rollback: revert token refs.
- **M6.1.4 — Skip-to-content link + `id="main"`.** Purpose: bypass nav (G4). Files: `index.css` (visually-hidden-until-focus), `AppShell.jsx` (skip anchor + `id="main"`). Diff: ~15 lines. Risk: LOW. Deps: none. Verify: keyboard Tab reaches skip → focus main. Rollback: revert both.
- **M6.1.5 — jsx-a11y @warn + vitest-axe harness.** Purpose: stand up gates (A1). Files: `.oxlintrc.json` (+`jsx-a11y`, warn), `package.json`/lock (`vitest-axe`/`axe-core` devDep), `src/test/axe.js` helper. Diff: ~30 lines + lockfile. Risk: LOW (warn non-blocking). Deps: none. Verify: `npm run lint` passes (warnings only); a sample axe test runs. Rollback: revert config + remove devDep.

### M6.2 (sequence after M6.1; App.jsx line-range boundaries)
- **M6.2.1 — SSE live status line + TopBar aria-live.** Purpose: announce streaming (P1/P3, A10). Files: `App.jsx`. Diff: ~30 lines. Risk: MED (UX). Deps: M6.1. Verify: mocked-EventSource test asserts status text per phase; AT pass. Rollback: remove live-region + status state.
- **M6.2.2 — Approval focus + announcement; ApprovalCard focus-ring + reject autofocus.** Purpose: P2/P6. Files: `App.jsx`, `components.test.jsx`. Diff: ~25 lines. Risk: MED (focus timing). Deps: M6.2.1 (shared live region). Verify: test focus→ApprovalCard on `approval_required`; reject-note autofocus. Rollback: revert ApprovalCard + focus effect.
- **M6.2.3 — Final-Result markdown fix.** Purpose: P4 inline bug + h1→h2 + table overflow (P5). Files: `App.jsx`. Diff: ~20 lines. Risk: LOW (port proven `reviewMarkdownComponents`). Deps: none. Verify: test inline backtick→`<code>`; Playwright wide-table scroll-in-container. Rollback: revert the `components` object.
- **M6.2.4 — Glyphs aria-hidden; message box focusable; ToolCallRow label; section headings.** Purpose: P7/P8. Files: `App.jsx`, tests. Diff: ~25 lines. Risk: LOW-MED (getByText queries — A5). Deps: none. Verify: axe; suite green (update glyph-text queries). Rollback: revert per-region.
- **M6.2.5 — Graph responsive height + container aria-label + axis aria-hidden.** Purpose: P9. Files: `PlaygroundExecutionGraph.jsx`, its test. Diff: ~12 lines. Risk: LOW (must not change P2-M5 fit behavior). Deps: none. Verify: Playwright phone graph usable; existing graph tests green. Rollback: revert height/aria.
- **M6.2.6 — Workspace grid `minWidth:0` + de-dup `56px`.** Purpose: P10 clipping. Files: `App.jsx`. Diff: ~6 lines. Risk: LOW. Deps: none. Verify: Playwright desktop — wide right-pane content not clipped. Rollback: revert grid style.

### M6.3 (after M6.1; ObservabilityPage editable-only + EvalsPage; frozen-range diff audit each task)
- **M6.3.1 — Shared select label + focus.** Purpose: O3. Files: `ObservabilityPage.jsx` (TaskPicker), `EvalsPage.jsx` (4 selects). Diff: ~25 lines. Risk: LOW. Deps: M6.1. Verify: axe names; focus visible. Rollback: revert selectStyle + labels.
- **M6.3.2 — SubNav responsive + group label (DOWNGRADED, A2).** Purpose: O5 responsive. Files: `ObservabilityPage.jsx`, `EvalsPage.jsx` (+ `NavItem.jsx` `Nav` `flexWrap` if done at the Nav level). Diff: ~10 lines. Risk: LOW (no NavItem role change). Deps: none. Verify: Playwright phone — bar wraps, no overflow. Rollback: revert flexWrap/group.
- **M6.3.3 — CompareView responsive collapse.** Purpose: O2 (worst break). Files: `ObservabilityPage.jsx` (Compare region). Diff: ~15 lines. Risk: LOW (reuse `useViewportWidth`). Deps: none. Verify: Playwright 360 — single column, no overflow. Rollback: revert layout branch.
- **M6.3.4 — BarChart a11y.** Purpose: O1 (biggest a11y gap). Files: `ObservabilityPage.jsx` (Dashboard region). Diff: ~25 lines. Risk: LOW. Deps: none. Verify: axe `role=img`+name; off-screen table present. Rollback: revert chart wrapper.
- **M6.3.5 — Table semantics (scope+caption).** Purpose: O6. Files: `ObservabilityPage.jsx` (Dashboard/Errors), `EvalsPage.jsx` (Results/Post-Hoc). Diff: ~20 lines. Risk: LOW. Deps: none. Verify: axe table rules. Rollback: revert th/caption.
- **M6.3.6 — Evals suite rows → buttons + headings + async aria-live.** Purpose: O4/O7. Files: `EvalsPage.jsx`, `EvalsPage.test.jsx`. Diff: ~25 lines. Risk: MED (interaction change + tests). Deps: none. Verify: user-event keyboard-activate row; axe. Rollback: revert row markup.

### M6.4 (after M6.2 — App.jsx sequencing, A7)
- **M6.4.1 — Memories Delete focus-ring + aria-label + focus-management + th scope.** Purpose: M1/M2. Files: `App.jsx` (MemoriesPage), test. Diff: ~20 lines. Risk: MED (focus-after-delete). Deps: M6.2 (same file). Verify: user-event delete → focus on defined target. Rollback: revert MemoriesPage region.
- **M6.4.2 — Onboarding autofocus.** Purpose: M3. Files: `ApiKeyPrompt.jsx`. Diff: ~3 lines. Risk: LOW. Deps: none. Verify: mount → input focused. Rollback: revert autofocus.

### M6.5
- **M6.5.1 — jsx-a11y warn→error.** Files: `.oxlintrc.json`. Diff: ~2 lines. Risk: LOW (all fixed by now). Verify: `npm run lint` exit 0. Rollback: back to warn.
- **M6.5.2 — axe suites per surface.** Files: `*.test.jsx`. Diff: ~120 lines tests. Verify: 0 violations. Rollback: remove tests.
- **M6.5.3 — user-event keyboard/focus suites.** Files: `*.test.jsx`. Diff: ~120 lines. Verify: pass. Rollback: remove.
- **M6.5.4 — Playwright 3-width walkthrough (local).** Files: scratchpad only (not repo). Verify: no overflow, focus reachable at 375/768/1280. Rollback: n/a.
- **M6.5.5 — Manual contrast + AT pass (documented).** Files: a short verification note (scratchpad or docs). Verify: documented. Rollback: n/a.
- **M6.5.6 — Final scope audit + full suite + build/lint.** Verify: `git diff main` = only intended files; frozen byte-identical; 65+new green; build/lint 0. Rollback: n/a.

---

## Part E — Ordered execution sequence

1. **M6.1** (all 5 tasks; foundation + tooling@warn) → **Checkpoint 1**.
2. **M6.2** (2.1→2.6, in order; App.jsx line-range discipline) → **Checkpoint 2**.
3. **M6.3** (3.1→3.6; can interleave with M6.2 ONLY if different files — but 3 touches ObservabilityPage/EvalsPage, disjoint from App.jsx, so M6.3 MAY run parallel to M6.2 as separate tasks; M6.4 may not) → **Checkpoint 3**.
4. **M6.4** (after M6.2 — same App.jsx) → **Checkpoint 4**.
5. **M6.5** (flip gate, verify, audit) → **Checkpoint 5 (milestone close / Go-No-Go)**.

## Part F — Complexity & risk assessment

- **Overall complexity: MEDIUM.** No novel architecture; ~24 small, well-bounded tasks; the hard part is discipline (App.jsx concentration, frozen-block-within-file, shared-primitive test breakage), not algorithmic difficulty. Highest-novelty item is the SSE live-region UX (needs a real AT pass).
- **Effort (not compressed):** M6.1 ~0.5–1 unit; M6.2 ~1–1.5; M6.3 ~1–1.5; M6.4 ~0.25; M6.5 ~0.5–1. Total ≈ 3.5–5.5 units.
- **Top risks (ranked):** (1) App.jsx blast/sequencing (mitigate: line ranges + sequence M6.4 after M6.2); (2) frozen-block-in-file violation (mitigate: per-task diff audit); (3) shared-primitive test breakage (mitigate: update+rerun per task); (4) live-region UX correctness (mitigate: AT pass); (5) backend-unreachable ceiling for live announcements (mitigate: mocked-SSE fixtures, genuine run optional).

## Part G — Recommended checkpoints (after each milestone)

- **CP1 (post-M6.1):** foundation review — labels/focus/contrast/skip-link correct before rollout; jsx-a11y noise triaged. Cheapest correction point.
- **CP2 (post-M6.2):** Playground a11y+responsive review incl. AT pass of streaming/approval; graph unchanged behavior.
- **CP3 (post-M6.3):** Observability/Evals review incl. **frozen-block byte-identical confirmation**.
- **CP4 (post-M6.4):** Memories/onboarding review; App.jsx fully settled.
- **CP5 (post-M6.5, milestone close):** flip gate, full verification, scope audit, merge Go/No-Go.

---
**End of plan. No code written. Awaiting approval to begin M6.1 (or spec/plan revisions first).**
