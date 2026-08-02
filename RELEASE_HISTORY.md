# Release History — OrchestAI

---

## 2026-08-02 — Accessibility & Verification Release

**Commit:** `8fab095` · **Previous:** `09fc818` · **17 commits, fast-forward, 0 merge commits**

Three stacked milestones released together, plus one blocker fix found during independent
pre-merge audit.

| Milestone | Commits | Theme |
|---|---|---|
| **F2** — Verification Durability | 7 | Playwright harness + offline stub backend, wired into CI |
| **F1** — Accessibility Conformance | 4 | Keyboard operability, WCAG AA contrast, touch targets, landmarks |
| **F1.1** — Human Accessibility Verification | 5 | Manual keyboard/screen-reader review, 6 fixes, regression coverage |
| **H-1 fix** | 1 | Release blocker caught pre-merge |

---

### Major features

**F2 — a real browser test layer.** Playwright suite covering what jsdom structurally cannot:
real layout and overflow, real focus movement, and a genuinely dropped SSE connection. Runs
against an **offline stub backend** (`tests/playwright/fixtures/stub-api.mjs`) — no live
service, no API keys, no cost — which is what makes it cheap enough to gate every commit. Added
as a CI job with browser install and failure-artifact upload.

**F1 — accessibility conformance.** Timeline span expand/collapse converted from a click-only
`<span>` to a real `<button>` with `aria-expanded` (it was the last keyboard-inoperable control
in the app). A new `textMuted` token repointed 36 text uses to meet AA. Touch targets raised
from 22–23px to a 44px hit area via an invisible overlay that leaves visual density unchanged.
Sidebar wrapped in an `<aside>` landmark, eliminating a `region` violation on all eleven
surfaces.

**F1.1 — human verification.** A manual keyboard and screen-reader pass across every surface at
375/768/1280px, plus 320px and 200% zoom. Six Critical/High findings fixed; 18 Medium/Low
catalogued as recommendations.

---

### Accessibility work

**Fixed in F1.1:**

| ID | Finding | Measured |
|---|---|---|
| C1 | Inline `code` in agent output effectively invisible on light-scheme systems — `index.css` inherited `--text-h`, which flips with `prefers-color-scheme`, while the app chrome is hard-coded dark | 1.60:1 → fixed |
| H1 | Every status badge and pill below AA as text | 2.59–3.85:1 → new `*-text` token tier |
| H2 | Graph edges keyboard-focusable with **no visible focus ring**, announcing raw span IDs | `outline: none` → removed from tab order *and* accessibility tree |
| H3 | Pressing Enter on "Run Agents" dropped focus to `<body>` permanently | → `aria-disabled` + handler guard |
| H4 | Observability and Evals exposed **zero** in-page headings across 9 sub-views | → 14 captions promoted, pixel-identical |
| H5 | React Flow attribution link illegible | 1.23:1 → restyled |

**Blocker found by independent pre-merge audit:**

**H-1** — selecting an Evals suite swaps the row background to `surface0` (#313244), lighter
than any of the four surface tiers `textMuted` was validated against. The secondary line
measured **4.45:1** against AA's 4.5:1. Zero violations before selection, one after — which is
why three prior sweeps of that page came back clean. Fixed with `subtext0` (5.65:1); no new
token, no value change.

**Verified at release** — 16 populated surface states, all clean; 48 tab stops each with a
visible focus indicator; drawer and dialog focus traps hold; zero horizontal overflow at 320px
and 200% zoom; nothing under 24×24.

---

### Testing work

| Layer | At `09fc818` | At `8fab095` |
|---|---|---|
| Backend | 408 methods | 408 methods |
| Frontend unit (Vitest) | 140 | **166** |
| Frontend browser (Playwright) | 0 | **356 passed**, 4 skipped (360 executions) |

New properties: negative controls (asserting the reconnect banner does *not* appear on a
healthy stream), a provable request-race test using task-id-derived values, and a real
dropped-connection test armed through a `/__control` endpoint.

---

### Verification work

**Every regression test was verified RED against reverted source before being accepted** — 20
of 21 Vitest and all Playwright tests failed without their fixes. This caught two tests passing
for the wrong reason: `toHaveStyle` against a jsdom-normalised hex, and an assertion on
`font: inherit`, which jsdom does not implement and silently drops.

The final contrast figure was derived three independent ways — a WCAG-spec implementation in
Node (self-tested against `#000/#fff` = 21.00 and `#777/#fff` = 4.48), in-page from rendered
computed styles, and by axe-core — agreeing to four decimal places.

Merge was performed fast-forward-only against a freshly fetched `origin/main`, with all gates
re-run on the merged result before push.

---

### Gate results at release

| Gate | Result |
|---|---|
| `npm test` | 166 passed, 20 files — exit 0 |
| `npm run test:e2e` | 356 passed, 4 skipped — exit 0 |
| `npm run lint` | exit 0 — 6 warnings (unchanged baseline) |
| `npm run build` | exit 0 — 631.64 kB / 188.30 kB gzip |
| Backend | 408 tests — exit 0 |

---

### Breaking changes

**None.** No API, schema, configuration or behavioural changes. The accessibility fixes are
text colours, ARIA attributes, focusability flags and semantic element wrappers with
`font: inherit`. Verified: 12 of 14 promoted headings inherit their context exactly, and the
two that carry explicit type match the elements they replaced byte-for-byte.

---

### Known limitations

**Accessibility (best-practice, not WCAG A/AA):**
- API-key gate has no `<main>` landmark and no `<h1>` — `landmark-one-main`, `page-has-heading-one`, `region` ×5
- Three unnamed `<svg>` wrappers remain in the graph accessibility tree — React Flow's own markup, rendered outside the edge object and unreachable from any edge prop
- 18 Medium/Low findings in [docs/f1-1-human-accessibility-report.md](docs/f1-1-human-accessibility-report.md), none a WCAG A/AA failure

**Engineering:**
- Token-contract tests validate against four hardcoded surface tiers — the root cause of H-1. Three of six status-text tokens would fail on `surface0` if placed there (latent, not currently reachable)
- `tests/playwright/a11y/f1-1.spec.js` uses 14 fixed sleeps; every F2 suite uses zero. With CI `retries: 1`, a marginal flake would be masked
- `frontend/README.md` is still the unmodified Vite template
- No NuGet package metadata — the framework cannot be consumed via `dotnet add package`
- 631 kB unsplit JS bundle
- `App.jsx` at 1,208 lines; no TypeScript on 10,044 lines of JSX

**Product:**
- No external adopters. Recorded as accepted risk in `CLAUDE.md`

---

### Future improvements

Next milestone is **v1.1 "Consumable"** (~1 week): land the derived token contract, add package
metadata and a CI publish job, replace `frontend/README.md`, remove the fixed sleeps, tag
`v1.0.0`. See [ROADMAP.md](ROADMAP.md).

---

### Post-release work in flight

**`feature/design-system-hardening`** — remediation of M-1 (= H-DEBT-3 in
[TECHNICAL_DEBT.md](TECHNICAL_DEBT.md)), the root cause behind H-1. Replaces
the hardcoded surface list with a role map covering all 29 colour tokens, guarded by an
exhaustiveness assertion so a new token fails the build until classified. Adds 31 contract
tests and 3 browser invariants; RED/GREEN demonstrated four ways, including proving the guard
catches the *omission itself*. Zero application code changed — the built bundle hash is
identical to the release.

**Status: complete, verified, uncommitted.** Not part of `8fab095`.

---

## Earlier history

Weeks 1–12 (foundation → delivery/operational safety), Phase 1 (architecture/product
validation), Phase 2 (NuGet packaging validation), Phase 3 (Sports domain + sample application),
and the P2 redesign milestones predate this document. Their decisions are recorded in
[DECISIONS.md](DECISIONS.md) (17 ADRs) and their plans in `docs/superpowers/plans/`.
