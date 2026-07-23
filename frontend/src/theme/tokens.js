// frontend/src/theme/tokens.js
//
// Single source of truth for the "Trace Console" design system's raw values.
// Transcribed directly from DESIGN.md (frontmatter + prose) and
// .impeccable/design.json — do not hand-edit a hex/px value here without
// updating (or re-checking against) those two files first.
//
// Plain JS constants only (§8 decision 1, final): no tokens.css, no CSS
// custom properties, no CSS Modules/CSS-in-JS. Nothing in the app consumes
// this file yet — that starts in Task 2.

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
// Typography
// Source: DESIGN.md frontmatter `typography:` block (lines 26-44) for
// heading/body/label verbatim. `title` is not present in the frontmatter or
// design.json's typographyMeta (which only carries a `purpose` string, no
// numeric values) — it is derived from DESIGN.md prose §3 ("Title (700,
// 14–15px, 1.3): section-level headers...", line 168) with the exact
// font-size pinned to the low end of that range (14px), which is also the
// only concrete numeric instance of a title style anywhere in either source
// file: .impeccable/design.json's "Status-Accented Card" component CSS sets
// `.ds-status-card-title { color: #cdd6f4; font-size: 14px; }`. `title`'s
// letter-spacing isn't documented anywhere (unlike label's explicit 0.07em),
// so it defaults to 'normal', matching the heading/body pattern.
// ---------------------------------------------------------------------------
const fontStack = "'JetBrains Mono', 'Fira Code', monospace"

export const typography = {
  heading: {
    fontFamily: fontStack,
    fontSize: '20px',
    fontWeight: 700,
    lineHeight: 1.2,
    letterSpacing: 'normal',
  },
  title: {
    fontFamily: fontStack,
    fontSize: '14px',
    fontWeight: 700,
    lineHeight: 1.3,
    letterSpacing: 'normal',
  },
  body: {
    fontFamily: fontStack,
    fontSize: '13px',
    fontWeight: 400,
    lineHeight: 1.5,
    letterSpacing: 'normal',
  },
  label: {
    fontFamily: fontStack,
    fontSize: '11px',
    fontWeight: 700,
    lineHeight: 1.2,
    letterSpacing: '0.07em',
  },
}

// ---------------------------------------------------------------------------
// Radii
// Source: DESIGN.md frontmatter `rounded:` block (lines 45-49).
// ---------------------------------------------------------------------------
export const radii = {
  sm: '3px',
  md: '4px',
  lg: '6px',
  xl: '8px',
}

// ---------------------------------------------------------------------------
// Spacing
// Source: DESIGN.md frontmatter `spacing:` block (lines 50-55).
// ---------------------------------------------------------------------------
export const spacing = {
  xs: '4px',
  sm: '8px',
  md: '12px',
  lg: '16px',
  xl: '24px',
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
}
