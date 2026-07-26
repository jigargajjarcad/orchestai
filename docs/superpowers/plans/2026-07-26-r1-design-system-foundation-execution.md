# R1 — Design-System Foundation — Execution Plan (task breakdown)

> Operationalizes Phase 1 / §13 of `2026-07-26-full-platform-redesign-master-plan.md` into
> discrete `subagent-driven-development` tasks. Backend-independent throughout. **No page
> files touched** (App.jsx/ObservabilityPage.jsx/EvalsPage.jsx/MemoriesPage/ApiKeyPrompt and
> routing/nav are PROTECTED in R1 — R1 changes only the shared design system, the new shared
> primitives, the font assets, DESIGN.md/design.json, and verification-only gallery/fixtures).
> Stops at Go/No-Go A (a **partial-baseline** checkpoint — the Phase-2 UI/mono per-element split
> is explicitly NOT done in R1).

## Global constraints (every task inherits these)
- **Protected — do NOT modify:** `App.jsx`, routing, header/nav, `MemoriesPage`/`EvalsPage.jsx`/
  `ObservabilityPage.jsx` and the other Observability sub-views, `ApiKeyPrompt.jsx`, backend/API,
  data models, existing product behavior. The **Observability slice** (`observability/*`) is
  read/reused for the palette stress test but its shipped components are NOT restyled in R1.
  If a task appears to need a protected file, STOP and report — do not expand scope.
- Backend-independent: no backend calls; verification is isolated-render + live-gallery + code
  comparison. No production data-contract change.
- No unverified claims; classify every claim live / isolated-render / code-comparison / not-verified.
- The Phase-2 UI/mono split is NOT performed. R1 *loads* the fonts and *defines* the tokens/roles;
  it does not reassign page elements to UI-vs-mono.
- Working-dir discipline: confirm `pwd`/`git rev-parse --show-toplevel` ==
  `.../worktrees/r1-design-system-foundation` before any commit.

---

### Task 1: Palette density stress-test → palette decision

**Scope:** build a **dense**, backend-independent synthetic graph fixture (a verification-only
file, e.g. under a gallery/scratch area — NOT wired into any product route) exercising, at
minimum: many agent nodes; multiple instances of the same identity; all 6 identity colors;
all 5 statuses; tool-call nodes; successful + failed paths; parallel branches; nested/deep
hierarchy; enough simultaneous elements to expose color competition/ambiguity/noise. Render it
through the existing Observability graph components (`AgentNode`/`ToolCallNode`/`TraceEdge`/
`layout.js` — reuse, do not modify) and evaluate the palette at density. Produce a **documented
palette decision**: keep the current 6-identity + status + single-accent breadth, or make a
controlled, evidence-justified adjustment. **Do not expand the palette because sparse looks
empty; do not keep it restrictive if density shows added semantic differentiation materially
aids comprehension.**

**Acceptance:** a dense fixture exists and renders; a written evaluation at density with
screenshots; a final palette decision with the evidence supporting it; zero modification to the
shipped `observability/*` components or `tokens.js` (this task decides, T3 implements).

**Verification:** live (render the dense fixture in a browser via CDP, screenshot at density).

**Depends on:** nothing. Gates T3.

---

### Task 2: Self-host fonts + typography tokens

**Scope:** self-host **IBM Plex Sans** (UI) and **JetBrains Mono** (data) — the latter currently
is NOT self-hosted (only a font-family name relying on system install; confirmed). Self-hosted
(files in-repo, e.g. `frontend/public/fonts/` or an assets dir), **subsetted WOFF2**,
`@font-face` with appropriate `font-display`, **no external CDN**. Load **only genuinely
necessary weights** (map to the type roles — likely IBM Plex Sans 400/500/600/700, JetBrains
Mono 400/500/700; justify the final set). `@fontsource/ibm-plex-sans` + `@fontsource/jetbrains-mono`
v5.3.0 are available OFL sources (either import the specific per-weight files, or copy the needed
WOFF2 into the repo + hand-write `@font-face` for tightest control — implementer's call, but the
result must be self-hosted with only-necessary weights). Establish the new **typography tokens**
in a form `tokens.js` (T3) will consume: `uiFontFamily` (IBM Plex Sans stack), `dataFontFamily`
(JetBrains Mono stack), and a documented type scale with per-step role annotation (UI vs data).
**Do NOT apply the UI/mono split to any page** (Phase 2).

**Acceptance / required MEASUREMENTS (not estimates):** exact font files used; exact weights
loaded; actual per-file WOFF2 sizes + total; measured production bundle/network impact (build
before/after); loading behavior (`font-display`, FOUT/FOIT); licensing/source (OFL-1.1);
explicit justification that every loaded weight is necessary. Report all of these as measured
numbers from this repo.

**Verification:** live (render a page/gallery showing both faces actually loading, not
system-fallback — confirm via computed `font-family` + a visible specimen); build measurement.

**Depends on:** nothing. Gates T3.

---

### Task 3: Rebuild `theme/tokens.js` + `DESIGN.md` + `.impeccable/design.json`

**Scope:** evolve `frontend/src/theme/tokens.js` **in place** to the master-plan §3 spec:
- typography roles/scale from T2 (`uiFontFamily`/`dataFontFamily` + scale) — replacing the
  current mono-only `heading/title/body/label` set, but **without changing what any page renders
  yet** (pages consume tokens; T3 changes token *values/exports*, Phase 2 reassigns elements);
- an **elevation** scale (soft, restrained — generalize the slice's `elevation`);
- a **surface hierarchy** ramp (canvas → surface → raised → overlay);
- **focus** + interactive-**state** tokens (focus-ring color, hover/selected patterns);
- the **palette** per Task 1's decision (identity + status kept/adjusted; accent; semantic good/
  warn/critical if T1 justified them; neutrals hue-biased);
- extended spacing/radius as specified.
Then update `DESIGN.md` and `.impeccable/design.json` to describe the new system consistently
(the impeccable hook checks against them — they must move together with the tokens).

**Acceptance:** `tokens.js` exports the new system; `DESIGN.md`/`design.json` describe it with no
internal contradiction; the impeccable design hook passes against the updated docs; **no page
file changed**; build/lint clean (or changes to the 5-warning baseline explicitly justified).
Because pages import token *values*, some page colors/surfaces will shift automatically — that is
expected Phase-1 propagation and is NOT a page redesign; confirm no page *file* was edited.

**Verification:** isolated-render/code-comparison (token exports); build/lint; impeccable-hook pass.

**Depends on:** T1 (palette) + T2 (type tokens). Gates T4/T5.

---

### Task 4: Migrate the 7 shared primitive files to the new tokens

**Scope:** update `frontend/src/components/*` (Button, Input [+TextArea], Label, NavItem [+Nav],
Panel, StateText, StatusBadge) to consume the new tokens (elevation, surfaces, focus/state,
type roles, palette). APIs stay stable (internals-only change). Every primitive gains the new
visual language + the full interactive-state matrix (default/hover/active/focus-visible/selected/
disabled where applicable), with visible keyboard focus (accessibility).

**Acceptance:** all 7 files/9 primitives render in the new system across their full state matrix;
public APIs unchanged (no consumer breakage); build/lint clean.

**Verification:** isolated component render (every primitive × every state, before/after); code
comparison (API stability).

**Depends on:** T3. Can run alongside T5.

---

### Task 5: Add new shared primitives — `EmptyState`, `Toggle`, `Card`

**Scope:** create three new shared components in `frontend/src/components/`:
- `EmptyState` — orientation + optional action + appropriate weight (retires the "one sentence in
  a void" pattern; consumed by pages in Phase 2, not here);
- `Toggle` / segmented control — generalize the inline one the slice built in `TimelineView`
  (do NOT modify TimelineView; build the shared version);
- `Card` / surface wrapper — encodes the new elevation/surface hierarchy.
All token-driven, accessible (keyboard + ARIA), with a documented API.

**Acceptance:** three new primitives render across their states; accessible; documented APIs;
not yet consumed by any page (Phase 2); build/lint clean.

**Verification:** isolated component render + live gallery.

**Depends on:** T3. Can run alongside T4.

---

### Task 6: Component gallery (Go/No-Go A review surface)

**Scope:** a comprehensive, verification-only gallery (not wired into product routes) rendering:
every shared primitive × every state; the typography scale in both faces (UI + data) actually
loaded; the palette (identity/status/accent/semantic) including the dense stress fixture from
T1; elevation/surface layers. This is the visual artifact for the Go/No-Go A review.

**Acceptance:** the gallery renders the entire new system coherently; usable for a human visual
review; no product route/page touched.

**Verification:** live (CDP screenshots of the gallery, wide + narrow).

**Depends on:** T4 + T5 (+ T2 fonts, T1 palette). Gates T7.

---

### Task 7: Final verification, diff-scope audit, R1 report

**Scope:** no new feature work. Confirm R1 end-to-end; full diff-scope audit vs `main` (`829546e`)
proving **zero** change to every protected surface (App.jsx/routing/nav/MemoriesPage/EvalsPage/
ObservabilityPage + other sub-views/ApiKeyPrompt/backend/data models); build + lint; write the
R1 completion report answering the 20 required report items (fonts measured, palette decision +
evidence, gallery results, verification classification, the partial-baseline/no-Phase-2-split/
accepted-backend-trace confirmations).

**Acceptance:** all prior tasks' acceptance re-confirmed; diff confined to shared-system + new
primitives + fonts + design docs + gallery/fixtures; protected surfaces byte-unchanged; honest
live/isolated-render/code-comparison classification for every claim.

**Verification:** code comparison (diff-scope) + a final live gallery pass.

**Depends on:** all. Then STOP at Go/No-Go A.

---

## Sequencing
```
T1 (palette stress → decision) ─┐
                                ├─► T3 (tokens + DESIGN.md + design.json) ─► T4 (migrate 7 primitives) ─┐
T2 (self-host fonts + type tokens)┘                                        └─► T5 (EmptyState/Toggle/Card)┴─► T6 (gallery) ─► T7 (report) ─► STOP @ Go/No-Go A
```
T1 and T2 are independent (run first, either order). T4 and T5 are independent given T3.
