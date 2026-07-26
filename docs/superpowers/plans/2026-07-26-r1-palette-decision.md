# R1 Task 1 — Palette Density Decision (evidence-based)

**Status: DECIDED.** Evidence: dense synthetic fixture (`densePaletteFixture.js` — 47 spans, 22
agent + 25 tool-call, all 6 identities WITH repetition [Research×5, Code×6, Data×4, Writer×3,
Browser×3], all 5 statuses, 16-way fan-out, depth-5 nesting, failed→retried→succeeded path)
rendered LIVE via CDP through the real Observability graph, plus a readable-density node grid
(identity×status matrix + repeated-identity cluster + tool/Code hue-collision test) rendered
live and screenshotted. Controller design evaluation against the impeccable color guidance
(contrast, color competition) and `PRODUCT.md`'s "engineer's instrument panel / identity-
preservation" posture (the 6 identity + 5 status colors are committed brand colors).

## Verdict: KEEP current breadth — neither expand nor restrict.

The density evidence shows the existing palette stays **legible, hierarchical, and intentional**
at realistic density. Concretely:

1. **6 agent identity colors — KEEP exactly as-is, do NOT add more.** All 6 (Orchestrator=
   Beacon Yellow, Research=Trace Blue, Writer=Signal Green, Code=Agent Mauve, Data=Agent Sky,
   Browser=Agent Peach) stay distinguishable at density. 6 is already at the healthy ceiling for
   categorical color distinction; the density evidence **actively argues against expansion** —
   more identity colors would increase competition, not aid comprehension. (Directly honors the
   instruction: "do not expand the palette simply because the sparse fixture feels empty" — it
   isn't sparseness driving a keep; density confirms 6 is right.)
2. **Repeated same-identity at density is a STRENGTH, not competition.** 5 adjacent Research
   (all blue) + 6 adjacent Code (all mauve) correctly read as "all the same agent type" for
   at-a-glance grouping; individual state is carried by the status pill. No ambiguity introduced.
3. **5-status palette + tinted-pill pattern — KEEP.** Reads clearly, stays visually separate
   from identity (Two-Palette Rule holds under density).
4. **One primary interactive accent (Trace Blue) — KEEP.** For NON-status semantic needs
   (good/warn/critical outside a span's execution status — e.g. a success toast, a validation
   warning), a small semantic set is justified, but it must **reuse the existing status hues**
   (green/amber/red) rather than introduce NEW colors — anchoring semantics to colors already in
   the system, avoiding palette inflation. The destructive-action treatment (statusFailed red,
   from Milestone 3's direction) stays. (Honors: "controlled semantic palette, never arbitrary
   decorative color"; and "don't keep it restrictive if density shows differentiation helps" —
   density shows the current controlled set is sufficient, so no broadening of *hues* is
   warranted, only the documented reuse of status hues for non-status semantics.)

## Two flagged proximities — accepted as-is (documented, not changed):
- **Research (Trace Blue #89b4fa) vs Data (Agent Sky #89dceb):** closest identity pair (redmean
  ~83). Real but distinguishable at density (saturated periwinkle vs lighter cyan); rarely
  adjacent in a real trace; validated in the shipped Observability slice without issue.
  Identity-preservation wins — no change.
- **Tool-call nodes share Code's exact mauve:** intentional existing convention (tool calls are
  mauve regardless of dispatching agent, matching `SpanRow`). Differentiated by node shape +
  🔧 label + smaller size, not color alone. Accepted; no change.

## What Task 3 implements from this:
- Keep the 6 identity + 5 status color tokens unchanged in `tokens.js`.
- Keep Trace Blue as the single primary interactive/focus accent.
- Define semantic good/warn/critical tokens that ALIAS the existing status hues (green/amber/
  red), not new colors — for non-status semantic UI (toasts, banners).
- Document the two accepted conventions (Research/Data proximity; tool/Code shared mauve) in
  DESIGN.md so they read as intentional, not drift.
