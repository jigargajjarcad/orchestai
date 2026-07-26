# P2-M2 Task A1 — Type-role foundation: report

Worktree confirmed before any edit: `git rev-parse --show-toplevel` ->
`.../worktrees/p2-m2-type-migration`, branch `worktree-p2-m2-type-migration`.

TDD: N/A — this task is CSS/docs/gallery-only, no logic to unit-test.

## 1. `frontend/src/index.css`

**Before:**
```css
--sans: system-ui, 'Segoe UI', Roboto, sans-serif;
--mono: ui-monospace, Consolas, monospace;
...
h1,
h2 {
  font-family: var(--mono);
  font-weight: 500;
  color: var(--text-h);
}
```

**After:**
```css
--sans: system-ui, 'Segoe UI', Roboto, sans-serif;
--mono: ui-monospace, Consolas, monospace;
/* P2-M2 Task A1 — the self-hosted UI face (mirrors theme/typography.js's
 * `uiFontFamily` exactly). Used by bare headings below; components/roles
 * adopt it via A2+. Does NOT touch #root's mono default or --mono itself. */
--ui:
  'IBM Plex Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto,
  'Helvetica Neue', Arial, sans-serif;
...
h1,
h2 {
  /* P2-M2 Task A1 — bare headings now default to the UI face. A heading is
   * never a rendered data value (DESIGN.md §3 Hierarchy), so it's safe to
   * move off mono; #root's mono default (above) is unchanged and remains
   * the data-safe fallback for anything not yet assigned a role. */
  font-family: var(--ui);
  font-weight: 500;
  color: var(--text-h);
}
```

Unchanged (verified by reading the post-edit file): `#root` (line 162, still
`'JetBrains Mono', 'Fira Code', monospace`), `code, .counter { font-family:
var(--mono) }` (line ~197), all `@font-face` rules, h1/h2's font-weight and
color.

## 2. `DESIGN.md` + `.impeccable/design.json` — convention + rollout note

Added to DESIGN.md §3 (Typography), directly under the existing "Named
Rules" / Two-Face Rule paragraph, a new **"Canonical element -> face
mapping"** block listing the two element lists verbatim from the brief (UI
face: nav/headings/section titles/labels/buttons/instruction-prose/status
messages/chrome; data face: IDs/timestamps/durations/costs/numeric/code-
JSON/technical-trace-span IDs/data table cells), followed by the **four
standing decisions** (encoded, not re-litigated):
1. `#root` stays JetBrains Mono — the data-safe fallback.
2. `typography.body` (tokens.js) stays data-face, unchanged.
3. UI prose renders via a UI-face role/token (`typeScale.body` or
   equivalent), never legacy mono `typography.body`, once migrated.
4. Input/TextArea: field content mono, label UI face.

Rewrote the "Rollout note (R1 partial-baseline)" blockquote (DESIGN.md,
was ~line 289) as "Rollout note (R1 partial-baseline -> P2-M2 completing the
split)": states Task A1 flips bare `h1`/`h2` to the UI face via the new
`--ui` var (headings are never data values), that `#root`'s mono default is
deliberately unchanged, and that the remaining mono-inheriting shared
controls (Button, NavItem, Input/TextArea, StateText, Panel/Card chrome)
still inherit `#root`'s mono face today — migrating them is Task A2, with
Task B completing the per-surface sweep. So component-catalog samples
showing the UI face still describe the target, not the current rendering,
until A2 lands.

`.impeccable/design.json`: mirrored both edits for DESIGN.md<->JSON
consistency —
- `narrative.overview`: appended the canonical mapping + four decisions,
  and rewrote the embedded rollout-note sentence to match DESIGN.md's
  updated note (same "P2-M2 completing the split" framing, same A1/A2/B
  task breakdown).
- `narrative.rules` (`"name": "The Two-Face Rule"`): extended `body` with
  the canonical mapping and the four standing decisions in condensed form.

No other keys touched (`extensions.typographyMeta`/`typeScaleMeta`, the
`components` catalog, etc. — those already describe the roles correctly
per the brief; role faces were not to be changed).

## 3. `frontend/src/gallery/ComponentGallery.jsx` — "Two-Face content" specimen

Added as new `Section index={5}` (after the existing Primitives section),
rendered from `<ComponentGallery>`'s root as `<TwoFaceSection />` last in
the page. Structure:

- `TwoFaceColumn` — shared `Card level="surface"` wrapper with a
  `typeScale.label`-styled title.
- `UiFaceSpecimen` (left column, "UI face — IBM Plex Sans"): an `<h2>`
  styled from `typeScale.heading`, a `Nav`/`NavItem` (nav-style label), a
  `Button variant="primary"` ("Run task"), an instruction paragraph styled
  from `typeScale.body`, and a `StateText tone="muted"` status message.
- `DataFaceSpecimen` (right column, "Data face — JetBrains Mono"): an ID
  (`task_9f2a1c7e`, `typeScale.dataLabel`), a timestamp
  (`2026-07-26T14:32:07Z`, `typeScale.dataBody`), a duration (`812 ms`) and
  cost (`$0.0091`) in `typeScale.dataValue`, a `<pre>` JSON snippet
  (`typeScale.dataBody`/`dataCaption`), and a `<table>` of two span-ID rows
  (`span_88f3a1`/`span_c02e77` with duration + cost columns, all
  `typeScale.dataBody`/`dataCaption`).

Every `font-family`/`fontSize`/`fontWeight` in the new code reads from
`typeScale.*` (already imported at the top of the file) — no hardcoded
font-family literal was added.

## Verification (measured)

1. `npm run build` — succeeded. Output: `dist/assets/index-CfxqCUQt.css`
   18.60 kB (gzip 3.53 kB), `dist/assets/index-DLGI12WS.js` 606.45 kB (gzip
   182.57 kB), built in 208ms. (Standard >500kB chunk-size advisory only,
   pre-existing, not a new warning.)
2. `npm run lint` — exactly 5 warnings, all pre-existing baseline, zero new:
   `EvalsPage.jsx:167` (unused `suites` param), `ObservabilityPage.jsx:531`
   (exhaustive-deps), `App.jsx:438/600/618` (unused catch `_` params).
3. `node -e "JSON.parse(...design.json...)"` -> `valid json`.
4. `git diff --stat`:
   ```
    .impeccable/design.json                   |   4 +-
    DESIGN.md                                 |  49 ++++++--
    frontend/src/gallery/ComponentGallery.jsx | 193 ++++++++++++++++++++++++++++++
    frontend/src/index.css                    |  12 +-
    4 files changed, 245 insertions(+), 13 deletions(-)
   ```
   No page (`App.jsx`/`EvalsPage.jsx`/`ObservabilityPage.jsx`/
   `ApiKeyPrompt.jsx`), no `components/*` or `components/shell/*` file, no
   `tokens.js`/`typography.js` touched. (`.superpowers/` shows as an
   untracked dir in `git status` — pre-existing brief/report location, not
   part of this diff.)
5. Live CDP gallery two-face verification: left to the controller, as
   instructed — not run by this task.

## Explicit confirmations

- `#root`'s font-family is still `'JetBrains Mono', 'Fira Code', monospace`
  — unchanged.
- No role face changed in `theme/tokens.js` or `theme/typography.js` — both
  files untouched (not in the diff).
- No page or `components/*`/`components/shell/*` file touched.
- Only design-layer files changed: `frontend/src/index.css`, `DESIGN.md`,
  `.impeccable/design.json`, `frontend/src/gallery/ComponentGallery.jsx`.

## Commit

Committed on `worktree-p2-m2-type-migration` (not pushed). See commit SHA
in the final chat reply.
