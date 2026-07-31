# P2-M6 — Responsive + Accessibility Hardening — Design Spec

- **Milestone:** P2-M6 (platform redesign, master-plan Phase 4 robustness pass, at Go/No-Go C)
- **Base commit:** `ca743d2d04aa4463eccd7dfd3a579883e8cc3260` (P2-M5 merged to `main`)
- **Branch / worktree:** `worktree-p2-m6-responsive-a11y-hardening`
- **Date:** 2026-07-31
- **Status:** Design approved (scope + acceptance confirmed). Spec pending user review → implementation plan.

---

## 1. Objective

Bring the **entire OrchestAI frontend** to a **WCAG 2.1 AA + fully-responsive (≈360px → desktop)** production-readiness bar. This is a **hardening / polish milestone only**: responsive behavior, accessibility, and UX polish. **No new product features**, no structural information-architecture changes, no backend work.

This completes the master plan's Phase-4 "responsive + a11y hardening" pass (the product-wide equivalent of the Observability slice's Task 6), building on the clean Phase-2 baseline and the Phase-3 Playground execution graph (P2-M5).

## 2. Scope

**In scope:** every editable frontend surface — application shell/nav, Playground (form, live agent feed, ApprovalCard, ManagerReviewCard, execution graph, Graph/List toggle, results), Observability editable sub-views (Summary, Cost Dashboard, Error Rates, Compare, SubNav, TaskPicker), Evals (Suites/Run/Results/Post-Hoc), Memories, onboarding (ApiKeyPrompt), and the shared component primitives + global CSS.

**Out of scope (protected — see §7):** the frozen Timeline graph + `SpanRow` list and all `observability/*` renderers; `DetailPanel` (reference model, not modified); `theme/tokens.js` **values** and `typography.js` (type-scale→rem work is explicitly deferred, §5); backend `src/**`, SSE contract, migrations/schema. No new product features of any kind.

## 3. Design principle — fix at the highest blast radius first

The audit (§4) found the biggest wins are **primitive/global**, not per-page: one `Input` fix labels every form in the app; one global focus rule protects every control; one `NavItem` token swap fixes a real AA failure across all navigation. The milestone therefore front-loads shared/global fixes (M6.1), then does per-surface passes (M6.2–M6.4) on a hardened foundation, then consolidates verification (M6.5). This mirrors the program's established consistency-first discipline.

The codebase already contains the **correct reference patterns** to replicate rather than invent:
- **`DetailPanel.jsx`** — the correct overlay focus contract (focus-in, focus-trap, Escape, focus-return, `aria-modal`, responsive drawer→bottom-sheet). Template for any overlay/announcement focus work.
- **The frozen Timeline graph** — per-node `ariaLabel` with relationship text: the template for making the Cost-Dashboard chart screen-reader accessible.
- **`Toggle.jsx`** — exemplary roving-tabindex radiogroup: the standard the other segmented/tab controls should match.
- **`Spinner`/`ErrorBanner`/`EmptyState`** — correct `role="status"`/`role="alert"`/`<h3>` semantics; reduced-motion already honored in `index.css` + `TraceEdge`.

## 4. Consolidated audit (evidence base)

Read-only audit of the whole frontend at `ca743d2` across four dimensions (responsive / keyboard / focus-management / ARIA-screen-reader). Overall verdict: **good structural shape — this is hardening, not a rebuild.** Shell landmarks, the mobile drawer, DetailPanel, and most primitives are already correct.

### 4.1 Highest blast-radius (primitive / global)
| # | Finding | Dim | Sev | Location |
|---|---|---|---|---|
| G1 | `Input`/`TextArea` render label as a non-associated `<span>` (no `htmlFor`↔`id`) → **every form field is announced unlabeled**; `Label` hardcodes `<span>` (structural enabler) | aria | HIGH | `components/Input.jsx` L60–105; `components/Label.jsx` L42–44 |
| G2 | No global `:focus-visible` fallback — focus is opt-in via `.focus-ring`; controls that omit it have invisible keyboard focus | focus | HIGH | `index.css` L226 |
| G3 | Inactive `NavItem` text uses `overlay0` (~3.36:1) — **real WCAG-AA failure on primary nav** | aria/contrast | HIGH | `components/NavItem.jsx` L134 |
| G4 | No skip-to-content link and no `id="main"` — keyboard/SR users tab the whole nav on every view | focus/aria | MED | `index.css` (global) + `AppShell.jsx` L429 |
| G5 | Sub-threshold metadata text (`surface2` ~2.5:1, `overlay0`) in Brand subtitle + TopBar task-id/cost | aria/contrast | LOW | `App.jsx` L107, L755, L758 |

### 4.2 Playground
| # | Finding | Dim | Sev | Location |
|---|---|---|---|---|
| P1 | Live SSE agent feed mutates with **zero `aria-live`** — SR users hear nothing from Run→completion | aria | HIGH | `App.jsx` feed L867–880; SSE L596–676 |
| P2 | `approval_required` moves no focus and makes no announcement — run halts for the user silently | aria/focus | HIGH | `App.jsx` L610–612, L858–865 |
| P3 | TopBar status/cost transitions (Pending→Running→Completed, total cost) not announced | aria | MED | `App.jsx` L751–768 |
| P4 | Final-Result `code` renderer still destructures removed react-markdown v10 `inline` prop → **real render bug**: every inline backtick becomes `<pre>` in `<p>` + horizontal overflow | aria/correctness | MED | `App.jsx` L918–920 |
| P5 | Final-Result markdown renders `<h1>` (competes with per-view h1) and tables have no `overflow-x` wrapper | aria/responsive | MED | `App.jsx` L912–928 |
| P6 | ApprovalCard reject-note textarea loses focus to `<body>` when revealed; its 4 buttons omit `.focus-ring` | focus | MED | `App.jsx` L248–295 |
| P7 | Section labels ("Agent Executions"/"Final Result") are `<Label>` not headings; agent names are `<strong>` — flat heading outline | aria | MED | `App.jsx` L869, L909, L169 |
| P8 | Decorative glyphs (`⏸`, `🎯`, tool status glyphs) not `aria-hidden`; scrollable message box not keyboard-focusable; `ToolCallRow` no `aria-label` | aria/keyboard | LOW/MED | `App.jsx` L129, L179–195, L237, L356 |
| P9 | `PlaygroundExecutionGraph` fixed 460px canvas (awkward on mobile); container has no `role`/`aria-label`; time-axis decorative | responsive/aria | MED | `PlaygroundExecutionGraph.jsx` L103–135 |
| P10 | Playground workspace grid `1fr` right track has no `minWidth:0` → wide right-pane content **clips** under AppShell `overflowX:hidden`; `56px` TopBar height magic-numbered | responsive | MED | `App.jsx` L796–803 |

### 4.3 Observability (editable sub-views) & Evals
| # | Finding | Dim | Sev | Location |
|---|---|---|---|---|
| O1 | Cost-Dashboard **`BarChart` invisible to screen readers** (div-pile + mouse-only `title`) — biggest a11y gap | aria | HIGH | `ObservabilityPage.jsx` L537–566 |
| O2 | **CompareView** two-column layout has no wrap/collapse → ~160px panels on phone — worst responsive break | responsive | HIGH | `ObservabilityPage.jsx` L800, L792 |
| O3 | Shared `TaskPicker` `<select>` unlabeled + `outline:none` invisible focus (3 call sites); 4 Evals selects same | aria/focus | HIGH | `ObservabilityPage.jsx` L73, L109–125; `EvalsPage.jsx` L23–32, L182, L255, L418–440 |
| O4 | Evals **SuitesView rows are `div onClick`** — mouse-only primary interaction | keyboard | HIGH | `EvalsPage.jsx` L81–88 |
| O5 | `SubNav` tab bars: no `tablist`/`tab`/`tabpanel` roles; `Nav` has no `flexWrap` → 5-item bar overflows phone | aria/responsive | MED | `ObservabilityPage.jsx` L83–107; `EvalsPage.jsx` L34–52; `NavItem.jsx` L190 |
| O6 | Data tables lack `th scope="col"` + `<caption>` (Dashboard, Error-Rates, Evals Results/Post-Hoc). *Tables already scroll in `overflowX:auto` — responsive is fine; this is semantics only* | aria | MED | multiple |
| O7 | Neither page emits semantic headings; no `aria-live` around async-loaded results | aria | MED | both files |

### 4.4 Memories & onboarding
| # | Finding | Dim | Sev | Location |
|---|---|---|---|---|
| M1 | Memories Delete button: missing `.focus-ring`; identical "Delete" accessible name for every row; focus lost to `<body>` on row removal | focus/aria | HIGH/MED | `App.jsx` (MemoriesPage) L441–446, L393–401 |
| M2 | Memories table `<th>` lack `scope="col"` | aria | LOW | `App.jsx` L423–429 |
| M3 | `ApiKeyPrompt` input not autofocused (single-purpose entry screen); label fixed by G1 | focus | LOW | `ApiKeyPrompt.jsx` L54–66 |

### 4.5 Already-correct (verified passes — do not touch)
Shell landmarks (`main`/`nav`/`header`); AppShell mobile drawer (full focus trap/return/Escape/scroll-lock); `DetailPanel` overlay; `Toggle` radiogroup; `Spinner`/`ErrorBanner`/`EmptyState`/`StatusBadge`/`Button`/`Panel`/`Card` semantics; `prefers-reduced-motion` (spinner + `TraceEdge`); data tables already wrapped in `overflowX:auto`; `PageContainer`/`Sidebar`/`TopBar` responsive guards; the `fontFamily:'sans-serif'` onboarding leftover (already removed).

## 5. Design decisions (confirmed)

1. **Phone tier: FULL phone support to ≈360px.** All surfaces optimized; dense views adapt (CompareView collapses, SubNav wraps, graph height responsive). Tables may scroll horizontally in-container, but the **page itself must never overflow**.
2. **Type scale px→rem: DEFERRED to a follow-up.** Keeps `tokens.js` values and `typography.js` untouched (lower blast radius/risk). Browser zoom still works; only the user-font-size-preference channel is left for later. All contrast fixes are **consumer-side token swaps** (referencing existing tokens like `subtext0`), so `tokens.js` values remain unchanged.
3. **Verification: jsx-a11y CI gate + axe-core tests.** Enable oxlint's built-in `jsx-a11y` plugin as a CI lint gate, AND add `axe-core` (via `vitest-axe`, a small devDep) for automated per-surface violation assertions in the existing jsdom test suite.

## 6. Sub-milestone structure

### M6.1 — Foundation: shared primitives + global a11y *(first; unblocks & gates the rest)*
- `Input`/`Label` label association via `useId` (`htmlFor`↔`id`); document + enforce the label-or-`aria-label` contract (dev-warn when neither present). *(G1)*
- Global `:focus-visible` fallback in `index.css`, scoped so `.focus-ring` still wins where present. *(G2)*
- Consumer-side contrast fixes: `NavItem` inactive `overlay0`→`subtext0`; Brand subtitle + TopBar metadata → `subtext0`. **No `tokens.js` value changes.** *(G3, G5)*
- Skip-to-content link (visually-hidden-until-focused, first focusable) + `id="main"` on AppShell `<main>`. *(G4)*
- Enable **oxlint `jsx-a11y`** plugin + establish **axe-core / vitest-axe** harness (early, so all later work is gated).

### M6.2 — Playground a11y & responsive
- SSE live region: curated `aria-live="polite"` status line reflecting **phase transitions** (not every token) + `role="log"` on the feed; `aria-live` on TopBar status/cost. *(P1, P3)*
- `approval_required`: announce + move focus to ApprovalCard; `.focus-ring` on its buttons; autofocus reject-note textarea on reveal. *(P2, P6)*
- Fix Final-Result `inline`-code bug (port from `reviewMarkdownComponents`); offset output headings h1→h2; wrap result tables in an `overflow-x` container. *(P4, P5)*
- Decorative glyphs `aria-hidden`; scrollable message box focusable + labeled; `ToolCallRow` `aria-label`; section labels → `<h2>`/`<h3>`. *(P7, P8)*
- `PlaygroundExecutionGraph`: responsive canvas height + container `aria-label` + `aria-hidden` axis; workspace grid `minWidth:0` on children; de-dup `56px` magic number. *(P9, P10)*

### M6.3 — Observability (editable sub-views) & Evals
- Shared `<select>` fix: `TaskPicker` label + focus (3 call sites) + the 4 Evals selects (`outline:none` → focus ring; associate labels). *(O3)*
- `SubNav`: `tablist`/`tab`/`tabpanel` roles + `aria-selected` + `aria-controls`; add `flexWrap` to the horizontal `Nav`. *(O5)*
- CompareView: collapse two-column → single at the 768px breakpoint; wrap the picker row. *(O2)*
- `BarChart`: `role="img"` + summarizing `aria-label` + off-screen equivalent data table (reuse the frozen graph's `ariaLabel` approach). *(O1)*
- Table semantics: `th scope="col"` + visually-hidden `<caption>` (Dashboard, Error-Rates, Evals Results/Post-Hoc). *(O6)*
- Evals SuitesView rows: `div onClick` → real `<button>` with `aria-pressed`/`aria-current` + focus-ring; semantic headings; async `aria-live`. *(O4, O7)*

### M6.4 — Memories & onboarding
- Memories Delete: `.focus-ring` + per-row `aria-label` (memory key + agent) + focus management on row removal (move focus to next row / stable anchor); `th scope="col"`. *(M1, M2)*
- `ApiKeyPrompt`: autofocus the input. *(M3)*

### M6.5 — Verification, CI & regression hardening *(woven throughout; final consolidation)*
- oxlint `jsx-a11y` gate green (findings fixed or narrowly waived — e.g. the intentional session-key `autoComplete`).
- axe-core / vitest-axe tests per major surface → 0 violations.
- `@testing-library/user-event` keyboard/focus tests: skip-link, tab order, Toggle, approval-focus, delete-focus, select-focus.
- Playwright responsive + keyboard walkthrough at **~375 / 768 / 1280**: no horizontal page overflow; correct 2-col→stack; every control reachable with visible focus.
- Manual contrast + AT spot-check pass, documented (project "live-verify" practice; live browser confirms the statically-cited contrast ratios).
- Extend the existing additive frontend CI job (jsx-a11y rides `npm run lint`; axe rides `npm test`).

## 7. Protected / out-of-scope (must remain byte-identical)

- `observability/*` renderers: `layout.js`, `AgentNode.jsx`, `ToolCallNode.jsx`, `TraceEdge.jsx`, `timelineTheme.js`.
- The frozen **Timeline graph block + `SpanRow` list** in `ObservabilityPage.jsx`. The `SpanRow` chevron keyboard issue (`span onClick`) stays **documented-deferred** inside the freeze.
- `DetailPanel.jsx` — reference model; replicate its patterns, do not modify it.
- `theme/tokens.js` **values** and `theme/typography.js` (rem deferred; contrast fixes are consumer-side).
- Backend `src/**`, SSE contract, migrations/schema.
- **No new product features.** Existing desktop/tablet UX must not regress.

## 8. Acceptance criteria

**Responsive (user-specified, verbatim):**
- No horizontal page overflow anywhere (down to ≈360px).
- CompareView adapts cleanly on phones.
- SubNav wraps or collapses appropriately.
- Playground graph remains usable with responsive height.
- Tables may scroll horizontally where appropriate, but the page itself must never overflow.
- Touch targets meet ≥44px where practical.
- Keyboard navigation and accessibility remain fully functional.
- Existing desktop and tablet UX must not regress.

**Accessibility (WCAG 2.1 AA):**
- All text meets 4.5:1 (3:1 large); no information conveyed by color alone.
- Every interactive element is keyboard-operable with a visible focus indicator.
- All form fields have programmatically-associated labels (or `aria-label`).
- Charts/non-text content have text alternatives.
- Correct landmarks + heading outline; skip-to-content link present.
- `prefers-reduced-motion` honored (verify; already largely true).

**Process/quality:**
- oxlint `jsx-a11y` gate passes; axe/vitest-axe = 0 violations on audited surfaces.
- All existing **65 tests still green** + new a11y/keyboard/responsive tests added.
- Protected files byte-identical to `main`; Timeline/`observability/*` renderers untouched.
- Build + lint pass; scope-audit clean; zero new product features.

## 9. Risks & mitigations

| Risk | Sev | Mitigation |
|---|---|---|
| jsx-a11y enablement surfaces a large batch at once (noisy/blocking) | Med | Enable in M6.1, triage into the milestone; narrow waivers only for intentional cases. |
| Live-region over-announcement (SSE is chatty) | Med | Curated single polite status line reflecting phase transitions, AT-tested — not every event. |
| Focus-management timing regressions (approval/delete) | Med | `user-event` tests + manual AT pass. |
| Full-phone inline-style churn → visual regression | Med | Playwright screenshots at 3 widths; incremental per-surface; desktop/tablet non-regression is an acceptance gate. |
| Global `:focus-visible` double-ringing `.focus-ring` elements | Low | Scope so `.focus-ring` wins / fallback applies only where absent. |
| Backend-unreachable ceiling for live SSE/approval flow | Low | Synthetic-SSE fixtures (P2-M5 replay pattern); genuine run optional, not required. |
| Accidentally touching the frozen Timeline/`observability/*` | Med | Per-task `git diff` scope audits; byte-stability is an acceptance criterion (proven P2-M5 discipline). |

## 10. Verification strategy (three layers)

1. **Static / CI:** oxlint `jsx-a11y` (rides `npm run lint`) — catches label association, roles, keyboard handlers, `aria-*` misuse automatically and prevents regressions.
2. **Component / unit:** `vitest-axe` (axe-core) violation assertions per surface + `@testing-library/user-event` keyboard/focus interaction tests, in the existing jsdom suite.
3. **Integration / manual:** Playwright headless walkthrough at ~375/768/1280 for real-viewport responsive + keyboard-reachability + visible-focus checks; a documented manual contrast + assistive-technology spot pass.

## 11. Sequencing & delivery

- Order: **M6.1 → (M6.2 ∥ M6.3 ∥ M6.4 as independent tasks) → M6.5 final gate.**
- Delivered on `worktree-p2-m6-responsive-a11y-hardening` off `main` (`ca743d2`), same worktree-isolation + per-task diff-scope-audit + protected-file byte-stability discipline used through P2-M5.
- Merge to `main` via fast-forward only after full verification, on explicit authorization (no push/merge without approval).

## 12. Explicitly NOT in this milestone

Type-scale px→rem (deferred follow-up); the frozen Timeline/`SpanRow` a11y fixes; any new product feature; backend/SSE/schema changes; IA/navigation restruct: none of these are authorized by this spec.
