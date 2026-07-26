# R1 — Design-System Foundation — Completion Report (Go/No-Go A)

> **Status: implementation complete, NOT merged, NOT pushed.** Branch
> `worktree-r1-design-system-foundation`, base `main` @ `829546e`, tip `1ee604d` (12 commits).
> This is the Go/No-Go A checkpoint — a **deliberate partial-baseline**, not the finished platform
> look. The Go/No-Go A *evaluation/decision* has NOT been made here; it is the human's next step.
> Milestone 3 remains paused and untouched. No Phase 2, no deep page redesign, no merge, no push.

---

## 1. What R1 delivered

R1 evolved OrchestAI's **shared frontend design system** — tokens, the 7 shared primitives, 3 new
primitives, self-hosted fonts, the two design-source-of-truth docs, and a verification-only
gallery — **without redesigning any page**. It establishes the visual foundation (two-face type
roles, a real surface/elevation hierarchy, focus/interactive-state tokens, a validated palette) that
Phase 2 will apply per-surface. Backend-independent throughout.

---

## 2. The 20 report items

### A. Typeface (item 1) — Option B, self-hosted, **measured**
- **Decision:** self-host **IBM Plex Sans** (UI face) + **JetBrains Mono** (data face), per the
  approved Option B.
- **Measured font payload (not estimated):** **7 WOFF2 files, 158,764 bytes = 155.04 KB total**,
  latin subset — IBM Plex Sans 400/500/600/700 (22,588 / 24,184 / 24,252 / 22,832 B) + JetBrains
  Mono 400/500/700 (21,168 / 21,832 / 21,908 B). `font-display: swap`. **OFL-1.1** licensed (license
  file shipped per family). Served from `frontend/public/fonts/` as **separate static assets** — so
  the **JS bundle is unaffected** by fonts (JS gzip stays ~179.9 kB, the pre-existing React-Flow-slice
  size). No runtime dependency: the `@fontsource/*` packages were used only to source the WOFF2, then
  uninstalled; `@font-face` is hand-written in `index.css`.
- **Every loaded weight is justified** by a type role it backs (UI 400 body / 500 label-medium /
  600 subheading-title / 700 heading-display; data 400 body / 500 label / 700 value-caption).
- **Approved reasoning (recorded, not the forbidden claim):** JetBrains Mono was already established,
  so IBM Plex Sans is a coherent companion; the UI/mono split is Phase 2 regardless, so loading both
  faces in Phase 1 has low marginal cost; IBM Plex Sans is a deliberate technical-context fit — a
  design/positioning choice. **This is explicitly NOT justified by any claim that system fonts inherently
  look generic or less credible** (that claim was ruled insufficiently supported; the final review
  confirmed the docs are free of it).
- **Verification: LIVE** — controller CDP-verified both faces actually load and render on the real app
  (`document.fonts.check` true for both) and in the gallery (7 faces, 129 leaf elems rendering IBM Plex
  Sans + 84 rendering JetBrains Mono).

### B. Palette (items 2–3) — decision + evidence
- **Decision:** KEEP the current breadth — **6 Agent-Identity + 5 status + 1 accent (traceBlue)**.
  Add semantic **good/warn/critical** ONLY as **aliases of existing status hues** (good→#16a34a,
  warn→#f59e0b, critical→#dc2626 — the exact Completed/Warning/Failed hexes; **no new colors**).
- **Evidence (per the do-not-lock-until-dense-stress-test instruction):** a **dense synthetic stress
  fixture** (47 spans: 22 agent + 25 tool-call, all 6 identities repeated, all 5 statuses on both node
  types, 16-way fan-out, depth-5 nesting, failed→retried→succeeded path) was built and evaluated at
  density via a readable node grid (documented in `2026-07-26-r1-palette-decision.md`). Density argued
  *against* expansion, not for it; two proximities accepted as intentional (Research #89b4fa / Data
  #89dceb blue-family; tool-calls share Code's mauve, differentiated by shape + 🔧). The One-Accent
  Rule and Two-Palette Rule were **re-validated, not just carried forward.**
- **Verification: LIVE** (dense fixture rendered via CDP) + **code-comparison** (semantic tokens confirmed
  exact aliases, final review verified no new hue anywhere).

### C. Tokens + docs rebuilt (item 4)
- `frontend/src/theme/tokens.js` evolved in place: added `semantic`, `surface` (canvas→surface→raised→
  overlay), `elevation` (flat/raised/overlay), `focusRing`, `interactiveState`, re-exported `uiFontFamily`/
  `dataFontFamily`/`typeScale`, extended `radii`/`spacing`; `typography.heading/title/label` → UI face,
  `typography.body` deliberately kept on the **data face**. `DESIGN.md` + `.impeccable/design.json` rewritten
  to match (Two-Face Rule supersedes No-Serif; Disciplined-Elevation supersedes Flat-By-Default).
- **Consistency: VERIFIED (code-comparison, final whole-branch review):** tokens.js ⇄ DESIGN.md ⇄ design.json
  agree on every type role/face, the surface + elevation ramps, the full palette, and radii/spacing — no
  token named in a doc is missing from tokens.js, no consumer-tripping contradiction.

### D. 7 shared primitives migrated (item 5)
- Button, Input(+TextArea), Label, NavItem(+Nav), Panel, StateText, StatusBadge migrated to consume
  surface/elevation/focus/interactive-state/palette tokens + the full interactive-state matrix
  (Button/NavItem gained hover/active via self-contained React state; Input/TextArea gained a disabled
  state). **Public APIs frozen** (internals-only; no consumer breakage). **No font-face flip** — the four
  mono-inheriting controls stay on JetBrains Mono (the intended residual). **Verification: isolated-render +
  code-comparison** (per-task review verified both hover-state machines leak-free and the disabled attribute
  forwarded); **LIVE** in the gallery.

### E. 3 new primitives (item 6)
- **EmptyState** (title + description + optional action/icon; real heading element), **Toggle** (segmented
  control; `role="radiogroup"` + `role="radio"`/`aria-checked`, roving tabindex, arrow/Home/End nav with
  wrap, `.focus-ring`), **Card** (surface/elevation tier wrapper). Strictly additive, not consumed by any
  page (Phase 2). **Verification: isolated-render + LIVE** (gallery: 2 radiogroups / 5 radios in live DOM,
  both Toggle instances clickable/keyboard-navigable).

### F. Component gallery (item 7)
- A **separate Vite HTML entry** (`gallery.html` + `src/gallery/*`) — **zero product blast-radius** (not in
  the product build; product build confirmed unchanged). Renders the full system: typography scale both
  faces, palette (identity/status/accent/semantic) + the dense stress fixture, surface/elevation layers,
  every primitive × state. Values read **live from tokens** (zero hand-copied hex). **Verification: LIVE**
  (CDP, wide 1440 + narrow 390; all sections present, both faces active, narrow reflows without overflow).

### G. Diff-scope audit (item 8) — protected surfaces byte-unchanged
- **Confirmed ZERO diff (code-comparison):** `App.jsx`, routing, header/nav, `MemoriesPage` (inline in
  App.jsx), `EvalsPage.jsx`, `ObservabilityPage.jsx` + the other Observability sub-views, `ApiKeyPrompt.jsx`,
  `main.jsx`, `index.html`, backend, data models. The shipped `observability/*` slice components are
  **byte-unchanged**; the only `observability/*` change is **2 additive verification-only files** (the dense
  fixture + harness). Full branch (33 files) sits entirely within R1's declared scope.

### H. Build / lint (item 9)
- `npm run build` succeeds (JS gzip ~179.9 kB — the pre-existing React-Flow-slice size; the chunk-size
  advisory pre-dates R1). `npm run lint` = **exactly the 5 pre-existing baseline warnings, zero new.**

### I. Verification classification (item 10) — honest, per project standard
- **LIVE (real headless Chrome via CDP):** both fonts load + render on the real app and gallery; the full
  gallery (all sections, both faces, every primitive×state, Toggle a11y roles, wide+narrow); the shared
  Label propagating to IBM Plex Sans on the real app while data text stays mono; the dense palette fixture
  rendering.
- **Isolated-render (SSR `renderToStaticMarkup` / `ssrLoadModule`, hand-props):** primitive state matrices;
  new-primitive states/ARIA; token export shapes.
- **Code-comparison:** tokens.js⇄DESIGN.md⇄design.json consistency; API stability; protected-surface diff;
  hover/roving-tabindex state-machine correctness; no-font-face-flip (grep-verified).
- **NOT verified against real backend data (unchanged structural limitation):** no genuine backend-sourced
  trace exists in this environment — every graph/fixture render uses hand-constructed data of the real
  `TimelineSpanDto` shape. No claim rests on a real agent run.

### J. Partial-baseline confirmation (item 11)
- Go/No-Go A is explicitly a **partial-baseline**. **Residual mono UI text is expected and present**:
  Button/NavItem labels, Input/TextArea field text, StateText, and Panel/Card chrome still render JetBrains
  Mono (visible in the gallery). Only `Label`/`StatusBadge` (via the `label` role token) and the new
  role-authored primitives render the UI face. This is intentional, not incomplete-in-error.

### K. No-Phase-2-split confirmation (item 12)
- The **UI/mono per-element type split was NOT performed.** R1 *defined* the two type roles as tokens and
  migrated the primitives' surface/elevation/state; it did **not** reassign page elements (or the
  mono-inheriting primitives) to the proportional face. That split is anchored at `App.jsx`'s shell literal
  + `index.css`'s `--mono` and is Phase-2 per-element work.

### L. Accepted backend-trace limitation (item 13)
- **Unchanged and re-affirmed.** No genuine backend trace is reachable (no valid Anthropic key; default
  Railway URL 404; no local API). The accepted synthetic-data limitation stands; no fixture is called "real."

### M. Per-task reviews + fixes (item 14)
- T1 palette (review clean). T2 fonts (review clean). T3 tokens/docs — **1 Important** (design.json samples
  stale vs new Two-Face Rule) **+ 1 Minor** (surface ramp hardcoded hex), both **fixed** (2804eec). T4
  primitives — **Approved**, 0 Critical/Important. T5 new primitives — **Approved**, 3 Minor all **fixed**
  (9a8d57b). T6 gallery — controller live-verified (skipped separate reviewer: zero-blast verification artifact).

### N. Final whole-branch review (item 15)
- **Merge-ready: YES with Minor follow-ups.** No Critical, no Important. Confirmed full three-file
  consistency, scope discipline (PASS), overclaim check (PASS — typeface rationale free of the forbidden
  claim, limitations stated plainly), type-face-boundary (PASS — grep-verified no mono primitive flipped),
  and no correctness bug in the new primitive logic.

### O. Final-review M1 fixed (item 16)
- **M1 (fixed, 1ee604d):** design.json/DESIGN.md component samples showed the canonical end-state UI face
  while the shipped controls remain mono — added a one-line rollout note to both docs clarifying the samples
  depict the target, and which controls stay mono until Phase 2 (so the mono rendering isn't misread as a defect).

### P. Accepted carry-forwards for Phase 2 (item 17) — documented, non-blocking
- **M2:** "selected" is drawn two ways — `NavItem` uses a solid `surface0` fill, `Toggle` uses traceBlue @
  `selectedAlphaSuffix`. Both preserve fidelity to their shipped source; reconcile toward one canonical
  selected treatment in Phase 2.
- **M3:** `EmptyState`/`StateText` prose renders on the mono/data face because `typography.body` is
  deliberately data-face (T3 conservative call). A legit Go/No-Go A discussion point; Phase-2's body-face
  split resolves it.
- **Panel/Card `accentStatus` `border-left: 3px`** = the impeccable "side-stripe" anti-pattern, **PRE-EXISTING**;
  R1 preserved it to keep pixel-parity with the shipped AgentCard/ApprovalCard/ManagerReviewCard. A candidate
  for Phase-2 reconsideration.
- **Dense-fixture gallery embed** frames small (React Flow fit-view on the tall waterfall); the readable
  density evidence lives in the T1 palette-decision doc.

### Q. What R1 did NOT do (item 18)
- No deep page redesigns; no Phase-2 per-element UI/mono split; no restyle of any shipped page or observability
  component; no Go/No-Go evaluation/decision; no new palette colors; no backend/data changes; no merge; no push.

### R. Merge/push status (item 19)
- **NOT merged, NOT pushed.** `main`/`origin/main` unchanged at `829546e`. Milestone 3 untouched.

### S. The Go/No-Go A decision (item 20) — the human's next step
- This report + the live gallery are the review surface. The decision to make (separately, not here): does the
  foundation warrant proceeding to **Phase 2** (per-surface Layer-1 alignment incl. the UI/mono per-element
  split), iterating on the foundation, or stopping? Awaiting explicit direction — no Phase-2 or merge action
  will be taken without it.

---

## 3. Branch commit log (base `829546e`)
```
1ee604d docs: note component samples show end-state face, not R1 partial-baseline (final review M1)
5010859 feat: add R1 Task 6 component gallery for Go/No-Go A review
9a8d57b fix: tighten token consumption in Toggle + Card (T5 review Minors)
0576924 feat: add EmptyState, Toggle, Card shared primitives (R1 Task 5)
325f0d5 feat: migrate 7 shared primitives to R1 design tokens (Task 4)
2804eec fix: align design.json component samples + tokens surface ramp to two-face system (T3 review)
ce55286 feat: rebuild design-system tokens for R1 surface/type/elevation model (Task 3)
3f2dbb7 feat: self-host IBM Plex Sans + JetBrains Mono, add typography tokens (R1 Task 2)
d3a8acf docs: record R1 palette density decision (keep 6-identity breadth, evidence-based)
dc3ca9a feat: add dense palette stress-test fixture + harness (R1 Task 1)
7b554c1 docs: add R1 execution plan (7-task design-system-foundation breakdown)
bb7d3c8 docs: add Full Platform Redesign Master Plan with R1-approved decisions
```

*Implementation complete. STOP at Go/No-Go A. Not merged, not pushed. Awaiting review + explicit direction.*
