# Lessons Learned — F2 / F1 / F1.1 Release

**Release:** `8fab095`, 2026-08-02

> Written from what actually happened in this milestone, including the mistakes. Every lesson
> below is tied to a specific incident in this repository so a future reader can check it.

---

## The central lesson

**An audit is only as good as the state its fixtures render.**

The same defect class escaped three consecutive accessibility audits, each more rigorous than
the last:

| Pass | Scope | Why it missed |
|---|---|---|
| F1 | 11 surfaces, axe clean | Ran against **empty** states — no task selected, no run executed |
| F1.1 | Populated screens | Never **selected** an item |
| Independent pre-merge audit | Selected state | Caught it — `textMuted` on `surface0`, 4.45:1 |

The audits improved every time. The *fixtures* never did. Rigor applied to the wrong input
space is wasted rigor.

**What to do instead:** enumerate *states*, not screens — `empty / loading / populated /
selected / error / partial` — and drive the matrix across every surface. Had that existed in
F1, all three escapes would have been one finding.

**The deeper version of this lesson:** the root cause was not carelessness, it was **a list
that had to be remembered**. F1 validated tokens against a hardcoded four-element array of
"surface tiers" — how the palette is *organised*, not what the product *paints*. Three real
backgrounds were missing. The fix is not a longer list; it is a derived list with an
exhaustiveness guard, so an unclassified token fails the build.

---

## Architecture

**Interfaces in the dependency-free project is the whole framework claim.** `IAgent`,
`IMcpTool` and `ILlmProvider` live in `Domain`. That single constraint is what lets someone
extend the system without depending on `Infrastructure`. It survived twelve weeks of feature
work intact. Protect it.

**A demonstrated abstraction beats a declared one.** `ILlmProvider` has three implementations
(Anthropic, OpenAI, AzureOpenAI) resolved from DI by `ProviderId`. An abstraction with one
implementation is a guess; with three it is a verified seam.

**Rigor applied unevenly is a maturity gap, not a strength.** The backend is cleanly layered
with 17 ADRs. The frontend, over the same period, grew `App.jsx` to 1,208 lines containing five
components and never adopted a type system. Three milestones went into accessibility while
that sat unaddressed. Knowing *when to stop polishing one axis and switch* is the skill that
was missing.

---

## Accessibility

**Fix the tier, not the value.** Twice a colour failed AA, and twice the remedy was a new token
tier rather than mutating the existing hue — because those hues also paint fills, borders and
edge strokes that were not failing. `statusBadgeColors` gained an `accent` field purely so
brightening the glyph would not lighten every badge border. Skipping that discipline is exactly
when a contrast fix becomes an unplanned redesign.

**`aria-hidden` and `tabindex` are different problems.** Removing graph edges from the tab
order fixed WCAG 2.4.7 and left `"Edge from s1 to s2"` in the accessibility tree for
virtual-cursor users. Keyboard access and screen-reader access are separate axes; fixing one
does not fix the other.

**Contrast is a property of a *pairing*, not a token.** `textMuted` is fine on six backgrounds
and fails on the seventh. Any validation that reasons about tokens in isolation will miss this
class of defect permanently.

**Automation proves presence, not usefulness.** axe confirmed live regions existed. It could
not tell us the Memories delete announced nothing useful, or that a graph edge announced a raw
GUID. Those needed a human reading the output as a user would.

---

## Testing

**A test that has never failed is not evidence.** Every regression test in this milestone was
verified RED against stashed source before being accepted. That caught two of my own tests
passing for the wrong reason: `toHaveStyle` against a jsdom-normalised hex, and an assertion on
`font: inherit` — which jsdom does not implement at all and silently drops. Both would have
shipped as green, meaningless tests.

**Assert the subject is present before measuring it.** `expect(violations).toEqual([])` passes
beautifully when the element under test is not on screen. Presence assertions are the
difference between a gate and a decoration — and their absence is precisely how F1 certified
screens it was not measuring.

**Validate the instrument before trusting the measurement.** Pinning the contrast
implementation to two published WCAG reference pairs (`#000/#fff` = 21.00, `#777/#fff` = 4.48)
cost one line and made every downstream number defensible.

**Negative controls earn their keep.** Asserting the reconnect banner does *not* appear on a
healthy stream catches a whole class of "always shows the error UI" regressions that positive
assertions never will.

**Hard sleeps are a smell, and inconsistency within a suite is worse.** Every F2 suite uses
web-first assertions and zero `waitForTimeout`. The F1.1 spec introduced 14. With CI
`retries: 1`, a marginal flake would go green with no signal.

---

## Verification

**Distinguish PROVEN from INFERRED, explicitly.** Writing that separation into the audit is
what stopped an overclaim: graph edges were genuinely removed from the tab order, but three
unnamed `<svg>` wrappers remained in the accessibility tree. Without the distinction, "edges
are gone from the a11y tree" would have entered the record as fact.

**Do not inherit conclusions across roles.** The independent audit re-derived everything from
the repository and found a blocker three prior sign-offs had missed. Trusting the previous
report would have shipped it.

**Re-derive important numbers by an independent method.** The final contrast figure was
computed three ways — a spec implementation in Node, in-page from rendered computed styles, and
by axe — all agreeing to four decimals. That is what makes a number safe to put in a commit
message.

---

## Release engineering

**Fast-forward only.** One merge commit in 337 is why `git log` is readable and why the release
was a single verifiable ancestry check rather than a merge-conflict negotiation.

**Verify the remote before an irreversible action.** `git fetch` failed twice against a flaky
network. An early retry loop reported a false `exit 0` because a pipe masked git's status.
Catching that mattered — the fast-forward ancestry check would otherwise have run against a
stale ref.

**Blockers escalate; they do not get waved through.** The release was stopped one step from
merge for a 4.45:1 contrast failure — 0.05 below the bar, on secondary 11px text. It was
stopped because the release's own headline claim was WCAG AA conformance, and shipping a known
counterexample under that banner is a documentation lie, not a rounding error.

**Name accepted risk in writing.** `CLAUDE.md` states plainly that external validation was
"deliberately deferred, not completed — accepted risk". A deferral that is written down is a
decision; one that is silent is a gap someone else discovers.

---

## Mistakes made this milestone

Recorded because they are the useful part.

1. **F1 declared WCAG AA conformance from empty-state sweeps.** The claim was not
   dishonest — it was under-evidenced, and nobody asked "what does this screen look like with
   data?"
2. **F1.1's regression tests initially asserted nothing was on screen.** Fixed during review by
   adding presence assertions — after the very same failure mode had already been diagnosed in
   F1. The lesson was written down before it was applied.
3. **Two of my own tests passed for the wrong reason** (jsdom hex normalisation, `font: inherit`).
   Only the RED verification exposed them.
4. **A blanket false-positive classification.** Three `side-tab` design-hook findings were
   dismissed together by citing one DESIGN.md rule. Two were genuinely covered; the third uses
   an *identity* colour, which that rule does not sanction. Lumping findings together to
   dispose of them faster is how real ones get lost.
5. **A wrong figure in a code comment** (6.99:1 vs the actual 7.89:1), caught only because the
   number was recomputed before finalising. A wrong number in a comment justifying an
   accessibility fix erodes trust in every other number nearby.
6. **Planning and documenting from memory instead of from the repository.** Twice: a roadmap
   recommendation to "add a second `ILlmProvider`" when three already existed, and an ADR count
   of 19 propagated across four closure documents when `DECISIONS.md` contains 17. Both were
   caught only by grepping the repository before publishing. Confident, wrong numbers are worse
   than absent ones, because nobody re-checks them.

---

## Repeat

- Fast-forward-only merges; conventional commits with real bodies (99% / 77% here).
- RED/GREEN verification as a hard rule.
- Comments that cite measurements and record *rejected* alternatives — this codebase's single
  best maintainability asset.
- ADRs that name the reopening condition, not just the decision.
- The offline stub backend: deterministic, free, and fast enough to gate every commit.
- Token tiers over value mutation.
- Separating proven from inferred in every report.

## Avoid

- Hardcoded lists that must be remembered. Derive them, and guard the derivation.
- Auditing default states and calling it coverage.
- Page-wide assertions with no presence check.
- Fixed sleeps where a web-first assertion exists.
- Classifying findings in batches.
- Polishing one axis to exhaustion while a known structural problem grows next to it.
- Writing plans from memory rather than from the repository.
