# Technical Debt — OrchestAI

**Baseline:** `main` = `8fab095` (2026-08-02)
**Source:** independent pre-merge audit + release retrospective. Nothing here is speculative —
every item was observed in the repository.

> Effort estimates are calibrated to this project's demonstrated AI-assisted throughput
> (337 commits in 5.5 weeks), not generic team velocity.

---

## Summary

| Severity | Count | Total effort |
|---|---|---|
| Critical | 0 | — |
| High | 3 | ~0.7 day |
| Medium | 4 | ~6 days |
| Low | 5 | ~1.5 days |
| Nice-to-have | 3 | ~0.3 day |

*Totals are the sum of the per-item estimates below, at 8h/day.*

**There are no Critical items.** The release is production-quality. The High items are cheap
and unblock everything downstream.

---

## Critical

None. Nothing is broken, data-losing, or a security concern.

---

## High

### H-DEBT-1 — `frontend/README.md` is the unmodified Vite template

- **Evidence:** file begins `# React + Vite` / *"This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules."*
- **Priority:** P0 · **Effort:** 15 minutes
- **Business impact:** High. It is among the first files a reviewer or contributor opens, and it silently contradicts the quality of the 485-line root README beside it.
- **Engineering impact:** None functional.
- **Milestone:** v1.1

### H-DEBT-2 — The framework is not installable

- **Evidence:** `grep "PackageId|RepositoryUrl|<Authors>"` across all four `.csproj` returns **zero matches**. ADR-017 defined the library packaging boundary and Phase 2 validated the *approach*; the metadata was never added.
- **Priority:** P0 · **Effort:** 0.5 day
- **Business impact:** **Critical to the project's thesis.** A framework whose premise is ".NET has no LangGraph" cannot be consumed via `dotnet add package`. Every adoption, docs and demo ambition is downstream of this.
- **Engineering impact:** No consumer can depend on the library without cloning the repo.
- **Milestone:** v1.1

### H-DEBT-3 — Token-contract validation omits `surface0` *(audit ID: M-1; remediation in flight)*

- **Evidence:** `f1-accessibility.test.jsx` and `f1-1-accessibility.test.jsx` both hardcode `['#11111b','#1e1e2e','#232336','#2a2a40']`. The product paints seven text backgrounds; `mantle`, `errorBg` and `surface0` were missing. `textMuted` measures 4.45:1 on `surface0` — this is the root cause of the H-1 release blocker.
- **Priority:** P0 · **Effort:** 1 hour (complete on `feature/design-system-hardening`, uncommitted)
- **Business impact:** Low today — the one live instance was fixed at release.
- **Engineering impact:** High. Silent AA regressions pass green. Three of six status-text tokens would fail on `surface0` if ever placed there (Running 4.27, Completed 4.29, Pending 4.45).
- **Status:** A derived role map with an exhaustiveness guard exists on `feature/design-system-hardening`, verified RED/GREEN, **not yet committed or merged.**
- **Milestone:** v1.1

---

## Medium

### M-DEBT-1 — `f1-1.spec.js` is the only suite using fixed sleeps

- **Evidence:** 14 `waitForTimeout` calls; every F2 suite has **0**. `playwright.config.js` sets `retries: 1` in CI.
- **Priority:** P1 · **Effort:** 2 hours
- **Business impact:** Low.
- **Engineering impact:** Latent CI flakiness on slower runners, and a retry would mask it — in the milestone named "Verification Durability". Stable across 3 local runs, so this is a risk, not a live defect.
- **Milestone:** v1.1

### M-DEBT-2 — `CONTRIBUTING.md` is 20 lines

- **Priority:** P1 · **Effort:** 2 hours
- **Business impact:** High *if* open-sourced; none otherwise.
- **Engineering impact:** No stated expectations for tests, commit format, or the RED/GREEN rule this project actually enforces.
- **Milestone:** v1.2

### M-DEBT-3 — God components in the frontend

- **Evidence:** `App.jsx` 1,208 lines containing `MemoriesPage`, `ApprovalCard`, `AgentCard`, `ToolCallRow`, `ManagerReviewCard`. `ObservabilityPage.jsx` 987 lines holding six views.
- **Priority:** P1 · **Effort:** 1–2 days
- **Business impact:** None.
- **Engineering impact:** Concrete and already felt — the H-1 fix had to touch `App.jsx` for two unrelated reasons (markdown colour, submit focus) purely because they share a file, degrading commit hygiene in a project whose commit hygiene is otherwise its best asset.
- **Milestone:** v1.2

### M-DEBT-4 — No TypeScript on 10,044 lines of JSX

- **Evidence:** zero `.ts`/`.tsx` files. `DetailPanel.jsx` documents its expected `span` shape in a 20-line prose header — a type definition written as a comment.
- **Priority:** P2 · **Effort:** 3–5 days
- **Business impact:** None directly.
- **Engineering impact:** Prop contracts unenforceable; refactors unsafe; inconsistent with the strongly-typed C# backend.
- **Dependency:** do **after** M-DEBT-3, or the god components make it painful.
- **Milestone:** v2.0

---

## Low

### L-DEBT-1 — 631 kB unsplit JS bundle
Vite warns. React Flow + react-markdown load on every route. **P2 · 4h.** Real impact on slow connections only. Depends on M-DEBT-3 for clean route boundaries. → v1.2

### L-DEBT-2 — Single backend test project
408 tests in one assembly, no unit/integration split, so no fast pre-commit subset. **P2 · 1 day.** No business impact. → v2.0

### L-DEBT-3 — DESIGN.md side-stripe exception covers status but not identity
`DESIGN.md:455` sanctions the 3px `border-left` accent for "cards that represent a single dominant **status**". `ObservabilityPage.jsx:902` uses it with an *identity* colour (`AGENT_COLORS`), and `AgentNode.jsx:91` has an established "identity rail" the document never mentions. Doc/code divergence, not a design defect. **P3 · 15 min.** → v1.2

### L-DEBT-4 — Dead `border` field in `statusBadgeColors`
`StatusBadge` reads only `background`, `accent`, `text`. Dead before F1.1, but adding `accent` beside it means a reader must now work out which field draws the border. **P3 · 5 min.** → v1.2

### L-DEBT-5 — API-key gate has no `<main>` or `<h1>`
axe: `landmark-one-main`, `page-has-heading-one`, `region` ×5. **Best-practice rules, not WCAG A/AA.** The app's only entry surface. **P3 · 15 min.** → v1.2

---

## Nice-to-have

### N-DEBT-1 — `gallery.html` / `shell-demo.html` not built in CI
Dev-only harnesses (a Storybook substitute without the dependency — a good pattern) that `npm run build` never touches, so they can rot silently. **P3 · 1h.** → v1.2

### N-DEBT-2 — 6 tolerated lint warnings
3 unused `_` catch params, 1 unused `suites` param, 1 `no-static-element-interactions` false positive on the graph keydown delegator, 1 `exhaustive-deps` on `BarChart`'s `useMemo`. Stable baseline; drift risk only. **P3 · 1h.** → v1.2

### N-DEBT-3 — `error-bg` missing from the design sidecar
Present in `tokens.js` and `DESIGN.md` frontmatter, absent from `.impeccable/design.json` `colorMeta`. Pre-existing, confirmed against `origin/main`. **P3 · 10 min.** → v1.2

---

## Deliberate tradeoffs — *not* debt

Recorded so nobody "fixes" them later.

| Choice | Why it stays |
|---|---|
| `srOnly` / `srOnlyAnchor` duplicated across 3 files | Each copy carries a "mirrors App.jsx's" comment. Deliberate, to keep page files self-contained. |
| Playwright `workers: 1`, `fullyParallel: false` | Required — suites share one stub backend with armed state. Costs 4.4 min, buys determinism. |
| React Flow attribution restyled, not hidden | `hideAttribution` is a licensing question, correctly refused. |
| `interactiveState.disabledText` on the dim `overlay0` | WCAG 1.4.3 exempts inactive controls; brightening it would make disabled read as enabled. |
| `Button.ariaDisabled` does not guard its own `onClick` | Documented caller contract; today's only caller guards in `handleSubmit`. |
| 18 Medium/Low accessibility findings | None is a WCAG A/AA failure. Catalogued in `docs/f1-1-human-accessibility-report.md`. |
