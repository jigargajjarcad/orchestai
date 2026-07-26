// frontend/src/theme/tokens.js
//
// Single source of truth for the "Trace Console" design system's raw values.
// Transcribed directly from DESIGN.md (frontmatter + prose) and
// .impeccable/design.json — do not hand-edit a hex/px value here without
// updating (or re-checking against) those two files first.
//
// Plain JS constants only (§8 decision 1, final): no tokens.css, no CSS
// custom properties, no CSS Modules/CSS-in-JS. Consumed by the seven shared
// components (Task 2) and, as of R1 (Design-System Foundation) Task 3, by
// the new surface/elevation/state/semantic/type-scale system described
// below — see docs/superpowers/plans/2026-07-26-full-platform-redesign-
// master-plan.md §3 and docs/superpowers/plans/2026-07-26-r1-palette-
// decision.md for the specs this file now implements.

// ---------------------------------------------------------------------------
// Colors
// Source: DESIGN.md frontmatter `colors:` block (lines 4-25), cross-checked
// against .impeccable/design.json `extensions.colorMeta[*].canonical` (and,
// for the oklch-canonical tertiary/primary/secondary tones, `tonalRamp[4]`,
// which is byte-for-byte identical to the DESIGN.md hex in every case).
// ---------------------------------------------------------------------------
export const colors = {
  // Neutrals (background layering + text tiers)
  crust: '#11111b',
  base: '#1e1e2e',
  mantle: '#181825',
  surface0: '#313244',
  surface2: '#585b70',
  overlay0: '#6c7086',
  subtext0: '#a6adc8',
  text: '#cdd6f4',

  // Primary / secondary / tertiary (Catppuccin "identity" tones)
  traceBlue: '#89b4fa',
  signalGreen: '#a6e3a1',
  alertRed: '#f38ba8',
  beaconYellow: '#f9e2af',
  agentMauve: '#cba6f7',
  agentSky: '#89dceb',
  agentPeach: '#fab387',

  // Status ("state") palette — a separate, harder-saturated set, used
  // exclusively for execution/task status badges. Never blend with the
  // Catppuccin identity tones above (DESIGN.md "Two-Palette Rule").
  statusRunning: '#2563eb',
  statusCompleted: '#16a34a',
  statusFailed: '#dc2626',
  statusPending: '#6b7280',
  statusWarning: '#f59e0b',

  // Matching dark-red banner background for inline error messages.
  errorBg: '#2d1b1b',
}

// ---------------------------------------------------------------------------
// Semantic tokens (R1 Task 3, master plan §3.2 / palette-decision doc)
//
// The palette-density investigation (docs/superpowers/plans/2026-07-26-r1-
// palette-decision.md) validated the existing 6 identity + 5 status colors
// as sufficient at realistic density and explicitly rejected adding new
// hues. For non-status semantic UI (a success toast, a validation warning
// banner, a destructive-confirmation accent — i.e. meaning that isn't a
// span's execution status but still reads as "good/warn/critical"), this
// aliases the *existing* status hues rather than introducing new colors,
// per the decision doc's Task-3 directive: "Define semantic good/warn/
// critical tokens that ALIAS the existing status hues (green/amber/red),
// not new colors."
// ---------------------------------------------------------------------------
export const semantic = {
  good: colors.statusCompleted, // #16a34a
  warn: colors.statusWarning, // #f59e0b
  critical: colors.statusFailed, // #dc2626
}

// ---------------------------------------------------------------------------
// Surface hierarchy (master plan §3.3)
//
// Generalizes observability/timelineTheme.js's validated `surface` export
// (read, not modified — see that file) into a real product-wide 4-tier
// ramp: canvas (page) -> surface (panel/card resting) -> raised (hover/
// active/selected) -> overlay (drawers/modals/menus). This is a
// reinterpretation, not a verbatim copy: the slice's `card`/`cardHover`
// were scoped to one view's node styling; here they're re-derived as the
// two upper rungs of a monotonic-lightness ramp that starts from the
// existing, unchanged `crust`/`base` neutrals so the whole ramp reads as
// one coherent progression rather than two unrelated systems:
//   canvas  #11111b (crust, unchanged)   ~9%  relative luminance
//   surface #1e1e2e (base,  unchanged)   ~15% relative luminance
//   raised  #232336 (validated: timelineTheme `card`)       ~17.6%
//   overlay #2a2a40 (validated: timelineTheme `cardHover`)  ~20.8%
// Verified (isolated luminance/contrast check, not eyeballed): `text`
// (#cdd6f4) stays >=9.6:1 and `subtext0` (#a6adc8) stays >=6.2:1 against
// every one of the four tiers above — both comfortably clear the 4.5:1
// body-text bar. `overlay0` (#6c7086), by contrast, is already below 4.5:1
// against `crust`/`base` in the shipped app today (3.84 / 3.36) and drops
// further on the new `raised`/`overlay` tiers (3.15 / 2.86) — a pre-
// existing neutral-tier limitation this task does not expand scope to fix.
// `overlay0` remains reserved for small decorative/eyebrow-only text (its
// documented DESIGN.md role); anything that must be read as content should
// use `subtext0` or `text`, which stay safe on all four tiers.
// ---------------------------------------------------------------------------
export const surface = {
  canvas: colors.crust,
  surface: colors.base,
  raised: '#232336',
  overlay: '#2a2a40',
  // Hairline border for raised/overlay surfaces — generalized verbatim from
  // timelineTheme's validated `panelBorder` (not an identity/status color,
  // so it lives here rather than in `colors`).
  border: 'rgba(205, 214, 244, 0.08)',
  // Modal/drawer scrim, generalized from timelineTheme's `backdrop`.
  backdrop: 'rgba(0, 0, 0, 0.35)',
}

// ---------------------------------------------------------------------------
// Elevation (master plan §3.4)
//
// A small, disciplined 3-step scale — generalizing timelineTheme's
// validated `elevation.node`/`elevation.panel` shadows product-wide.
// Deliberately restrained (soft, dark-mode-appropriate shadows only): this
// is a dense technical instrument panel, not a consumer app with dramatic
// drop-shadows. `flat` names the "no shadow" resting state explicitly
// (replacing DESIGN.md's old blanket "Flat-By-Default" rule, which is now
// one rung of a real scale rather than the only rung).
// ---------------------------------------------------------------------------
export const elevation = {
  flat: 'none',
  raised: '0 1px 2px rgba(0, 0, 0, 0.4), 0 4px 12px rgba(0, 0, 0, 0.35)',
  overlay: '0 8px 30px rgba(0, 0, 0, 0.5)',
}

// ---------------------------------------------------------------------------
// Focus ring (master plan §3.7 / §3.12 — keyboard focus is mandatory)
//
// A pure interaction affordance, deliberately a distinct channel from both
// agent-identity and status color (Two-Palette Rule) — reuses the shared
// Trace Blue accent, matching the value already hardcoded in index.css's
// `.focus-ring:focus-visible` rule (`2px solid #89b4fa`, offset `2px`) and
// timelineTheme.js's `focusRing` export. Centralized here as the token
// source of truth for any *new* consumer (plain CSS can't import a JS
// value directly, which is why index.css/timelineFocus.css still carry
// their own literal/CSS-custom-property copies — this doesn't change that,
// only gives future JS-driven consumers one place to read the value from).
// ---------------------------------------------------------------------------
export const focusRing = {
  color: colors.traceBlue,
  width: '2px',
  offset: '2px',
  outline: `2px solid ${colors.traceBlue}`,
  // For elements where `outline` is suppressed by a third-party library
  // (e.g. React Flow's own CSS zeroes it on its node class) and a
  // box-shadow-based ring is required instead — matches timelineFocus.css's
  // pattern.
  boxShadow: `0 0 0 2px ${colors.traceBlue}`,
}

// ---------------------------------------------------------------------------
// Interactive state matrix (master plan §3.7)
//
// Tokens for the hover/active/selected/disabled states Task 4 will apply
// consistently across every primitive (button, input, nav item, toggle,
// selectable card/row, link). Hover/active/selected follow the same
// hex-alpha-suffix idiom already established below for status badges
// (`statusBadgeAlphaSuffix`) — applied as a background tint over whichever
// identity/accent/neutral color is contextually relevant, not a fixed
// color themselves. `disabled` intentionally does NOT invent a new alpha
// figure: it points at the exact solid-color convention DESIGN.md's
// Buttons section already documents ("Disabled state drops to `surface0`
// background with `overlay0` text"), so Task 4 has one source for the
// convention that already ships today.
//   hover:    8% alpha  -> 0.08 * 255 = 20.4  -> round -> 20  = 0x14
//   active:  14% alpha  -> 0.14 * 255 = 35.7  -> round -> 36  = 0x24
//   selected:18% alpha  -> 0.18 * 255 = 45.9  -> round -> 46  = 0x2e
// ---------------------------------------------------------------------------
export const interactiveState = {
  hoverAlphaSuffix: '14', // 8% alpha
  activeAlphaSuffix: '24', // 14% alpha
  selectedAlphaSuffix: '2e', // 18% alpha
  disabledBackground: colors.surface0,
  disabledText: colors.overlay0,
}

// ---------------------------------------------------------------------------
// Typography (master plan §3.1 — folds in T2's theme/typography.js)
//
// T2 (docs/superpowers/plans/2026-07-26-full-platform-redesign-master-
// plan.md §3.1, DECISION — APPROVED Option B) built the self-hosted
// two-face system (`uiFontFamily` = IBM Plex Sans, `dataFontFamily` =
// JetBrains Mono, `typeScale` = display->caption UI steps + matching data*
// steps). Re-exported here so tokens.js remains the single import surface
// every component/page reads from — see theme/typography.js for the full
// rationale and per-step weight/size audit; that file is not modified by
// this task, only imported.
// ---------------------------------------------------------------------------
export { uiFontFamily, dataFontFamily, typeScale } from './typography'
import { uiFontFamily as _uiFontFamily, dataFontFamily as _dataFontFamily } from './typography'

// Source (legacy, pre-T2): DESIGN.md frontmatter `typography:` block (lines
// 26-44) for heading/body/label verbatim. `title` is not present in the
// frontmatter or design.json's typographyMeta (which only carries a
// `purpose` string, no numeric values) — it is derived from DESIGN.md prose
// §3 ("Title (700, 14–15px, 1.3): section-level headers...", line 168) with
// the exact font-size pinned to the low end of that range (14px), which is
// also the only concrete numeric instance of a title style anywhere in
// either source file: .impeccable/design.json's "Status-Accented Card"
// component CSS sets `.ds-status-card-title { color: #cdd6f4; font-size:
// 14px; }`. `title`'s letter-spacing isn't documented anywhere (unlike
// label's explicit 0.07em), so it defaults to 'normal', matching the
// heading/body pattern.
//
// R1 Task 3 update — per-key face assignment, NOT a blanket flip:
// `heading`/`title`/`label` are reassigned to the UI face (`uiFontFamily`).
// Each is, by DESIGN.md's own definition, pure UI chrome that is never a
// rendered data value: `heading` is the once-per-view page title, `title`
// is a panel's section header, `label` is an uppercase eyebrow/column
// header (not the data value beneath it). A repo-wide grep (frontend/src)
// confirms `heading`/`title`.fontFamily have zero current consumers, and
// `label`.fontFamily has exactly two (StatusBadge, Label) — both rendering
// short status/eyebrow words ("RUNNING", "TASK TITLE"), never IDs/numbers/
// JSON, so the flip is verified safe for what's actually on screen today.
//
// `body` deliberately stays on the DATA face (`dataFontFamily`), unchanged
// from before. DESIGN.md itself describes `body` as "the default for
// prompts, outputs, table cells, descriptions" — the most data-adjacent of
// the four (raw prompts/outputs/table cells can be exactly the monospace-
// sensitive content the brief warns against silently reflowing). Per the
// brief's explicit escape hatch ("if unsure whether changing a token's font
// breaks a page's data text... keep the safe path"), `body` is left
// resolving to the mono face; the new `typeScale.body` (UI face) and
// `typeScale.dataBody` (mono, matching this key's values) are the correct
// NEW exports for Phase 2's per-element split to choose between.
// ---------------------------------------------------------------------------
export const typography = {
  heading: {
    fontFamily: _uiFontFamily,
    fontSize: '20px',
    fontWeight: 700,
    lineHeight: 1.2,
    letterSpacing: 'normal',
  },
  title: {
    fontFamily: _uiFontFamily,
    fontSize: '14px',
    fontWeight: 700,
    lineHeight: 1.3,
    letterSpacing: 'normal',
  },
  body: {
    fontFamily: _dataFontFamily,
    fontSize: '13px',
    fontWeight: 400,
    lineHeight: 1.5,
    letterSpacing: 'normal',
  },
  label: {
    fontFamily: _uiFontFamily,
    fontSize: '11px',
    fontWeight: 700,
    lineHeight: 1.2,
    letterSpacing: '0.07em',
  },
}

// ---------------------------------------------------------------------------
// Radii
// Source: DESIGN.md frontmatter `rounded:` block (lines 45-49). `xxl` is a
// R1 Task 3 addition (master plan §3.6: "likely add a larger radius for big
// surfaces (the slice used 10-14px on the canvas frame/nodes)") — 12px sits
// at the middle of that validated range.
// ---------------------------------------------------------------------------
export const radii = {
  sm: '3px',
  md: '4px',
  lg: '6px',
  xl: '8px',
  xxl: '12px',
}

// Standardizes which radius applies to which element class (master plan
// §3.6: "the redesign should standardize which radius applies to which
// element class... rather than the current ad-hoc per-site choices").
// Values reference `radii` above rather than repeating literals.
export const radiusForElement = {
  input: radii.lg, // 6px
  button: radii.lg, // 6px
  pill: radii.md, // 4px — status badges
  card: radii.xl, // 8px — top-level panels
  nestedRow: radii.lg, // 6px — rows/blocks nested inside a card
  bigSurface: radii.xxl, // 12px — canvas frames, large overlays/modals
}

// ---------------------------------------------------------------------------
// Spacing
// Source: DESIGN.md frontmatter `spacing:` block (lines 50-55). `xxl`/`xxxl`
// are R1 Task 3 additions (master plan §3.5: "extended at the top end
// (32/48) for the more generous page-level rhythm a modern product uses").
// ---------------------------------------------------------------------------
export const spacing = {
  xs: '4px',
  sm: '8px',
  md: '12px',
  lg: '16px',
  xl: '24px',
  xxl: '32px',
  xxxl: '48px',
}

// ---------------------------------------------------------------------------
// Status badge alpha
//
// DESIGN.md's own prose is internally inconsistent about the badge
// background alpha: two mentions state it exactly ("20%-alpha", colors §2
// line 141; "20% alpha bg", Do's §6 line 247) while the Components §5
// description hedges with a range ("~18–20% alpha", line 210) that matches
// .impeccable/design.json's concrete Status Badge CSS example
// (`rgba(37, 99, 235, 0.18)`, i.e. 18%) at its low end. We resolve this in
// favor of the round, twice-stated 20% figure as the intended canonical
// target (the 18% in the JSON's worked example is itself an instance of the
// exact cross-file alpha drift this token module exists to eliminate).
//
// The border alpha has no such conflict: every mention describes a
// "~50-60%" range, and design.json's concrete example (`rgba(37, 99, 235,
// 0.55)`) sits squarely inside it, so 55% is used as the precise value.
//
// Hex-suffix arithmetic (2-digit hex alpha suffix = round(alphaFraction *
// 255), expressed as uppercase-safe lowercase hex):
//   background: 20% of 255 = 0.20 * 255 = 51.0            -> 51 = 0x33
//   border:     55% of 255 = 0.55 * 255 = 140.25 -> round -> 140 = 0x8c
//
// Note: these suffixes are deliberately NOT the same as any of the
// hand-rolled suffixes currently hardcoded across the three page files
// (which use inconsistent, mathematically-incorrect pairs like `20`/`60`
// and `18`/`50` — hex `20` is 12.5%, hex `60` is 37.6%, not the intended
// 20%/55%). Task 2's StatusBadge is expected to replace all of those with
// the correct values below.
// ---------------------------------------------------------------------------
export const statusBadgeAlphaSuffix = {
  background: '33', // 20% alpha
  border: '8c', // 55% alpha
}

export const statusBadgeColors = {
  running: {
    background: `${colors.statusRunning}${statusBadgeAlphaSuffix.background}`,
    border: `${colors.statusRunning}${statusBadgeAlphaSuffix.border}`,
    text: colors.statusRunning,
  },
  completed: {
    background: `${colors.statusCompleted}${statusBadgeAlphaSuffix.background}`,
    border: `${colors.statusCompleted}${statusBadgeAlphaSuffix.border}`,
    text: colors.statusCompleted,
  },
  failed: {
    background: `${colors.statusFailed}${statusBadgeAlphaSuffix.background}`,
    border: `${colors.statusFailed}${statusBadgeAlphaSuffix.border}`,
    text: colors.statusFailed,
  },
  pending: {
    background: `${colors.statusPending}${statusBadgeAlphaSuffix.background}`,
    border: `${colors.statusPending}${statusBadgeAlphaSuffix.border}`,
    text: colors.statusPending,
  },
  warning: {
    background: `${colors.statusWarning}${statusBadgeAlphaSuffix.background}`,
    border: `${colors.statusWarning}${statusBadgeAlphaSuffix.border}`,
    text: colors.statusWarning,
  },
  // 'Skipped' isn't part of DESIGN.md's Status palette (it's a Two-Palette-Rule
  // exception the running app already makes: ObservabilityPage.jsx's
  // STATUS_COLORS maps Skipped to overlay0 (#6c7086), a neutral text tier,
  // not a hard Status tone) — added here so StatusBadge (Task 2) can cover
  // all six execution-status values without inventing a color of its own.
  skipped: {
    background: `${colors.overlay0}${statusBadgeAlphaSuffix.background}`,
    border: `${colors.overlay0}${statusBadgeAlphaSuffix.border}`,
    text: colors.overlay0,
  },
}

// ---------------------------------------------------------------------------
// Panel status-accent border alpha (Task 2 addition)
//
// DESIGN.md §5 Cards/Panels (line 217) states the outer border on a
// status-accented panel ("an agent execution card border-tinted by its own
// status color") is the status color at "40% alpha". .impeccable/design.json's
// worked Status-Accented Card CSS example instead shows
// `rgba(22, 163, 74, 0.25)` (25%) — which traces back to the running app's
// `${statusColor}40` string-concatenation in AgentCard/ManagerReviewCard
// (App.jsx): the literal two-char suffix "40" is hex, not a percentage, so
// it renders as 64/255 = 25.1% alpha, not the documented 40%. This is the
// same class of hex-suffix-as-decimal-string transcription slip already
// documented above for the status badge (18% worked example vs. the
// twice-stated, round 20% figure) — except, unlike the badge alpha, this one
// is NOT being corrected in Milestone 1. The Milestone 1 acceptance rule
// authorizes exactly one intentional visual change: the StatusBadge
// standardization to 20% background alpha. Panel's accentStatus border is
// about to replace AgentCard/ApprovalCard/ManagerReviewCard's existing
// `${statusColor}40` border in Task 3, and that migration must render
// identically to what's shipped today — so this constant deliberately
// preserves the current (buggy) 25.1% rendering rather than "fixing" it to
// match DESIGN.md's prose. Fixing it to true 40% is deferred to a future
// milestone alongside the badge-alpha-style cleanup, not done here.
//   Current shipped literal: `${statusColor}40` -> hex "40" = 64/255 = 25.1%
// ---------------------------------------------------------------------------
export const panelAccentBorderAlphaSuffix = '40' // ~25.1% alpha (preserves today's shipped rendering; DESIGN.md's stated intent is 40%, not corrected here — see comment above)

// ---------------------------------------------------------------------------
// Button ghost-variant border alpha (Task 2 addition)
//
// DESIGN.md §5 Buttons: "Secondary / Ghost: transparent background, 1px
// colored border at 50% alpha ... text matches the border's base color."
// No badge-style drift here — 50% of 255 rounds to a clean value, and it
// matches the hex suffix already used correctly elsewhere in the app for a
// 50%-alpha border (e.g. App.jsx's Cancel-adjacent Reject button pattern).
//   50% of 255 = 0.50 * 255 = 127.5 -> round -> 128 = 0x80
// ---------------------------------------------------------------------------
export const buttonGhostBorderAlphaSuffix = '80' // 50% alpha
