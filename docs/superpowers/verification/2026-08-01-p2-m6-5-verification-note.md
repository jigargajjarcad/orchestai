# P2-M6.5 — Verification Note (contrast, AT, and the jsx-a11y gate)

- **Branch:** `worktree-p2-m6-responsive-a11y-hardening` · **Base:** `main` @ `bda9ddb`
- **Plan:** `docs/superpowers/plans/2026-07-31-p2-m6-implementation-plan.md` (M6.5.1 / M6.5.5)
- Records the two M6.5 deliverables that are findings rather than code, plus the
  gate decision. Everything here was measured, not estimated.

---

## 1. jsx-a11y gate: promoted to error, with two rules held back

`.oxlintrc.json` now sets **33 of 36** jsx-a11y rules to `error`. The gate is
real: a probe file containing a missing `img` alt and an invalid `aria-*`
attribute produced two `error` diagnostics and exit 1; removing it returned
exit 0.

Two rules remain at `warn`:

| Rule | Fires at | Classification |
|---|---|---|
| `click-events-have-key-events` | `ObservabilityPage.jsx:213` (`SpanRow`) | **Frozen** |
| `no-static-element-interactions` | `ObservabilityPage.jsx:213` (`SpanRow`), `:431` (TimelineView graph render) | **Frozen** |

A blanket promotion was tried and rejected on evidence: setting
`categories.correctness = error` exits 1 on eight findings.

These three are **genuine defects, not false positives** — those timeline rows
and graph nodes are click-only, so a keyboard user cannot operate them. They are
left at `warn` so every lint run keeps printing them. A file-scoped override for
`ObservabilityPage.jsx` was deliberately **not** added: it would silence them
rather than merely leave them unpromoted, and would also blind the non-frozen
parts of that file.

**To finish the gate, the Timeline must be unfrozen.** That is a scope decision
for the maintainer, not a lint configuration problem.

### Remaining non-jsx-a11y warnings (8 total, all pre-existing)

`no-unused-vars` ×4 (App.jsx ×3 catch params, EvalsPage `suites`), and
`react-hooks/exhaustive-deps` ×1 (ObservabilityPage `BarChart` useMemo). None are
accessibility issues; none are in M6.5's scope. The `exhaustive-deps` one is not
a trivial fix — `keyFn`/`valueFn` arrive as inline arrows, so adding them to the
dependency array would change identity every render and defeat the memo.

---

## 2. Colour contrast (WCAG 2.1 AA) — measured, FAILING, frozen-blocked

Measured in Chrome by compositing each text node's effective foreground over its
effective background (walking ancestors through alpha), at 375 / 768 / 1280 on
all four surfaces. Findings are viewport-independent, as expected.

| Token | Value | Measured | Required | Example |
|---|---|---|---|---|
| `overlay0` | `#6c7086` | **3.84:1** on `crust`, **3.36:1** on `base` | 4.5:1 | "Task Title", "Eval Suites", column headers |
| `surface2` | `#585b70` | **2.81:1** | 4.5:1 | "Agent Memories" eyebrow, empty-state descriptions |

**This is a known, deliberate, documented design-system position, not an
oversight.** `theme/tokens.js` (lines 98–107) already states that `overlay0` is
"already below 4.5:1 against `crust`/`base` in the shipped app today (3.84 /
3.36)". My independent measurement reproduces those two numbers exactly, which
corroborates both the method and the record. `DESIGN.md:279` then *mandates* the
pairing: labels are "always paired with `overlay0` or `surface2` color, never
full-brightness text."

**Assessment, stated plainly:** WCAG AA has no exemption for "eyebrow" or
"decorative" text. Strings like "Agent Memories", "Task Title", and the table
column headers are real UI text conveying meaning at small sizes, so 4.5:1
applies and the app does not currently meet it for those roles.

**Why it is not fixed here.** Both the values (`theme/tokens.js`) and the rule
(`DESIGN.md`) are frozen, and M6.5 forbids visual redesign. Fixing it consumer-
side would mean repointing every label in the app from `overlay0`/`surface2` to
`subtext0` — a product-wide visual change, not a hardening pass.

**When it is addressed**, `subtext0` (`#a6adc8`) is the ready answer: tokens.js
already records it at ≥6.2:1 against all four surface tiers. The change is a
design decision (labels get visibly brighter), so it needs sign-off, not a
silent swap.

---

## 3. Touch targets — measured, BELOW the spec's stated intent

The design spec confirmed "44px touch targets (where practical)". Measured
heights of primary controls:

| Control | Height |
|---|---|
| Playground `Auto-run` / `Require approval` / `Graph` / `List` toggles | **23px** |
| Memories per-row `Delete` | **22px** |

All are ≥24px-adjacent but far below 44px, at every width including phone. No
M6.5 task covers control sizing, and changing padding is a visual change, so
this is **documented, not fixed**. It is a real gap against the spec's own
stated decision and should be scheduled deliberately.

---

## 4. Assistive-technology pass — NOT performed

The plan calls for a real screen-reader pass (VoiceOver/NVDA). **This has not
been done and cannot be automated away.** What *is* verified deterministically:
accessible names on every control, single-`h1` and no-skipped-heading structure
per view, axe clean on every surface, live regions announcing exactly once
(M6.3 removed a double-announcement), and post-delete focus landing on a named
element rather than `<body>`.

What only a human with a screen reader can confirm: whether the streaming
announcements are *useful* rather than merely present, whether the Timeline is
navigable in practice, and whether the announcement cadence during a live run is
tolerable. **Treat this as an open item, not a completed one.**

---

## 5. What the automated suite structurally cannot see

Recorded because M6.4 was caught out by exactly this: the Memories table
compressed to roughly one character per line on a phone while **every overflow
assertion passed**, since a table pinned to a narrow container always "fits".
Overflow checks and compression checks are different checks. The Playwright pass
now asserts both, plus a screenshot review of every surface at every width.
