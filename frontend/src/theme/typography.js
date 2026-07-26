// frontend/src/theme/typography.js
//
// R1 (Design-System Foundation) Task 2 — typography tokens for the
// self-hosted two-face type system (master plan §3.1,
// docs/superpowers/plans/2026-07-26-full-platform-redesign-master-plan.md,
// DECISION — APPROVED (2026-07-26), Option B: self-hosted IBM Plex Sans (UI
// face) + JetBrains Mono (data face)). The @font-face declarations that
// actually load these faces live in ../index.css; this module only exports
// the family stacks + a documented type scale for consumers.
//
// Scope boundary (per the Task 2 brief — do not expand):
//   - This file DEFINES the tokens. It is not imported by theme/tokens.js
//     yet, and no page consumes it yet — Task 3 folds these exports into
//     tokens.js (the single source of truth every component reads from),
//     and Phase 2 is where each page/element is assigned to the correct
//     face per-element (mirrors tokens.js's own header note: "Nothing in
//     this file is imported by any component yet" was true of tokens.js at
//     its Task 1, and is true of this file now).
//   - Do NOT apply the ui/data split to any page from this file directly.
//     JetBrains Mono remains the only face actually rendered anywhere in
//     the app today (App.jsx's shell + index.css's body `--mono`) until
//     Phase 2's per-element work lands.
//
// ---------------------------------------------------------------------------
// Font families
// ---------------------------------------------------------------------------

// UI face — proportional, for headings, labels, navigation, prose, and
// empty-state copy. Fallback stack mirrors the system-font stack that
// observability/timelineTheme.js already used as a stand-in for this role
// (§7 of the competitive-benchmark investigation), now with the real
// self-hosted face as the primary.
export const uiFontFamily =
  "'IBM Plex Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif"

// Data face — monospace, reserved for IDs, timestamps, durations, cost
// figures, code/JSON, span IDs. Same family name + fallback stack the app
// already references (tokens.js's `fontStack`, App.jsx:578, index.css's
// `--mono`) — this task makes the named face actually self-hosted instead
// of relying on the visitor having JetBrains Mono installed locally.
export const dataFontFamily = "'JetBrains Mono', 'Fira Code', monospace"

// ---------------------------------------------------------------------------
// Type scale
//
// Spans display -> caption per master plan §3.1 ("retire the current
// mono-only heading/title/body/label-only set in favor of a scale that
// spans display -> caption, with the proportional/mono roles annotated per
// step"). Every step is tagged with:
//   - role: 'ui' | 'data' — which face the step is FOR.
//   - fontFamily: the literal stack to apply (not inferred from role, so a
//     consumer can use a step directly without a role -> family lookup).
//   - fontWeight: always one of the six weights self-hosted in index.css
//     (UI: 400/500/600/700 — body/labels/headings/display; data:
//     400/500/700 — default/medium-emphasis/bold data figures). No step
//     below introduces a seventh weight.
//
// Sizes are audited from real current usage across src/**/*.jsx (not
// invented): theme/tokens.js's existing heading(20/700)/title(14/700)/
// body(13/400)/label(11/700); observability/timelineTheme.js's
// nodeTitle(13/600)/nodeMeta(11/500)/nodeData(11/500)/panelHeading(15/600)/
// panelBody(13/400)/panelLabel(10/600); ObservabilityPage.jsx's stat values
// (18/700) and status-pill text (10/700); EvalsPage.jsx's summary numbers
// (20/700). `display` (28px) is the one genuinely new step, added because
// no current step reaches "display" scale and the plan explicitly requires
// the scale to span that far.
//
// Each UI step has a `data*` counterpart sized to the same visual rank, so
// a UI label and an adjoining data value (e.g. a stat tile's caption +
// figure) can line up visually even though they render in different faces.
// ---------------------------------------------------------------------------

export const typeScale = {
  // --- UI (proportional) steps ---------------------------------------------
  display: {
    role: 'ui',
    fontFamily: uiFontFamily,
    fontSize: '28px',
    fontWeight: 700,
    lineHeight: 1.15,
    letterSpacing: '-0.01em',
  },
  heading: {
    role: 'ui',
    fontFamily: uiFontFamily,
    fontSize: '20px',
    fontWeight: 700,
    lineHeight: 1.2,
    letterSpacing: 'normal',
  },
  subheading: {
    role: 'ui',
    fontFamily: uiFontFamily,
    fontSize: '15px',
    fontWeight: 600,
    lineHeight: 1.3,
    letterSpacing: '-0.01em',
  },
  title: {
    role: 'ui',
    fontFamily: uiFontFamily,
    fontSize: '14px',
    fontWeight: 600,
    lineHeight: 1.3,
    letterSpacing: 'normal',
  },
  body: {
    role: 'ui',
    fontFamily: uiFontFamily,
    fontSize: '13px',
    fontWeight: 400,
    lineHeight: 1.5,
    letterSpacing: 'normal',
  },
  label: {
    role: 'ui',
    fontFamily: uiFontFamily,
    fontSize: '11px',
    fontWeight: 500,
    lineHeight: 1.2,
    letterSpacing: '0.07em',
  },
  caption: {
    role: 'ui',
    fontFamily: uiFontFamily,
    fontSize: '10px',
    fontWeight: 500,
    lineHeight: 1.3,
    letterSpacing: '0.06em',
  },

  // --- Data (monospace) steps, sized to match the UI step of equivalent
  //     visual rank ---------------------------------------------------------
  dataValue: {
    role: 'data',
    fontFamily: dataFontFamily,
    fontSize: '18px',
    fontWeight: 700,
    lineHeight: 1.2,
    letterSpacing: 'normal',
  },
  dataBody: {
    role: 'data',
    fontFamily: dataFontFamily,
    fontSize: '13px',
    fontWeight: 400,
    lineHeight: 1.5,
    letterSpacing: 'normal',
  },
  dataLabel: {
    role: 'data',
    fontFamily: dataFontFamily,
    fontSize: '11px',
    fontWeight: 500,
    lineHeight: 1.2,
    letterSpacing: 'normal',
  },
  dataCaption: {
    role: 'data',
    fontFamily: dataFontFamily,
    fontSize: '10px',
    fontWeight: 700,
    lineHeight: 1.3,
    letterSpacing: 'normal',
  },
}
