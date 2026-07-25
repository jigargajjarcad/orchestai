// frontend/src/observability/timelineTheme.js
//
// Scoped visual system for the Observability Timeline vertical slice
// (docs/superpowers/plans/2026-07-24-observability-vertical-slice.md, Task 1).
//
// Deliberately NOT part of frontend/src/theme/tokens.js — this is a self-contained,
// parallel visual language for exactly one view, per the plan's own scope boundary
// (§3/§4: "new files, not modifications to the shared theme/tokens.js or
// components/*"). Nothing here is imported by any other page.
//
// What's reused vs. new (plan §4):
// - Agent Identity Palette and status colors: reused verbatim from
//   theme/tokens.js / ObservabilityPage.jsx's own AGENT_COLORS/STATUS_COLORS —
//   same hex values, re-exported here under this module's own names so the
//   graph components (Tasks 2+) have one clearly-scoped place to import from.
// - Typography, elevation, node/edge/detail-panel styling: new, testing the
//   competitive-benchmark investigation's §7 recommendations (a readable
//   proportional face for prose/labels, monospace reserved for IDs/timestamps/
//   cost figures; real elevation instead of flat-by-default) on this one
//   surface only.

import { colors as sharedColors } from '../theme/tokens'

// ---------------------------------------------------------------------------
// Color — reused, unchanged
// ---------------------------------------------------------------------------

// Agent Identity Palette, verbatim from ObservabilityPage.jsx's AGENT_COLORS.
export const agentColors = {
  Orchestrator: sharedColors.beaconYellow,
  Research: sharedColors.traceBlue,
  Writer: sharedColors.signalGreen,
  Code: sharedColors.agentMauve,
  Data: sharedColors.agentSky,
  Browser: sharedColors.agentPeach,
}

// Status palette, verbatim from ObservabilityPage.jsx's STATUS_COLORS. No
// WaitingForApproval entry — confirmed absent from the real data model this
// view consumes (plan §1/§3); not invented here either.
export const statusColors = {
  Pending: sharedColors.statusPending,
  Running: sharedColors.statusRunning,
  Completed: sharedColors.statusCompleted,
  Failed: sharedColors.statusFailed,
  Skipped: sharedColors.overlay0,
}

// Tool-call spans use the existing mauve "tool" identity color regardless of
// which agent dispatched them (matches ObservabilityPage.jsx's SpanRow:
// `span.spanType === 'ToolCall' ? colors.agentMauve : ...`).
export const toolCallColor = sharedColors.agentMauve

// ---------------------------------------------------------------------------
// Color — new (elevation/surface layer for this view only)
// ---------------------------------------------------------------------------

export const surface = {
  // Page background stays the shared crust — this view doesn't diverge on
  // the base canvas color, only on what sits above it.
  canvas: sharedColors.crust,
  // Node/panel background: one step up from the shared `base`, giving this
  // view's cards slightly more presence than the flat panels elsewhere in
  // the product, without introducing a whole new neutral ramp.
  card: '#232336',
  cardHover: '#2a2a40',
  panelBorder: 'rgba(205, 214, 244, 0.08)',
}

// Real elevation — the investigation's §7 finding, tested here first. Kept
// deliberately restrained (soft, dark-mode-appropriate shadows, not a
// dramatic drop-shadow) rather than a wholesale departure from this
// product's understated visual identity.
export const elevation = {
  node: '0 1px 2px rgba(0, 0, 0, 0.4), 0 4px 12px rgba(0, 0, 0, 0.35)',
  nodeSelected: '0 1px 2px rgba(0, 0, 0, 0.4), 0 0 0 1px currentColor, 0 8px 20px rgba(0, 0, 0, 0.45)',
  panel: '0 8px 30px rgba(0, 0, 0, 0.5)',
}

// ---------------------------------------------------------------------------
// Typography — new. Proportional face for UI chrome/prose, monospace
// reserved for IDs/timestamps/cost — testing the investigation's own §7
// recommendation on one surface before any product-wide change.
// ---------------------------------------------------------------------------

export const typography = {
  // System font stack standing in for a licensed/hosted humanist sans
  // (e.g. Inter) — avoids a webfont-loading decision this plan doesn't own
  // (that question stays deferred per Milestone 1/2's own decision 10).
  uiFontFamily:
    "-apple-system, BlinkMacSystemFont, 'Segoe UI', Inter, Roboto, 'Helvetica Neue', Arial, sans-serif",
  dataFontFamily: "'JetBrains Mono', 'Fira Code', monospace",

  nodeTitle: { fontSize: 13, fontWeight: 600, letterSpacing: '-0.01em' },
  nodeMeta: { fontSize: 11, fontWeight: 500 },
  nodeData: { fontSize: 11, fontWeight: 500 }, // monospace, for duration/cost
  panelHeading: { fontSize: 15, fontWeight: 600, letterSpacing: '-0.01em' },
  panelBody: { fontSize: 13, fontWeight: 400, lineHeight: 1.55 },
  panelLabel: { fontSize: 10, fontWeight: 600, letterSpacing: '0.06em', textTransform: 'uppercase' },
}

// ---------------------------------------------------------------------------
// Node / edge geometry — new
// ---------------------------------------------------------------------------

export const node = {
  agentWidth: 220,
  agentHeight: 84,
  toolCallWidth: 176,
  toolCallHeight: 56,
  radius: 10,
}

export const edge = {
  // Heavier than the Task 1 first draft (was 1.5 / 2) — the review found the
  // relationship edges too thin/quiet for how important the structure they
  // convey is. See plan §3b.
  strokeWidth: 2.5,
  strokeWidthActive: 3,
  // Explicit directional arrowhead at the child (target) end, conveying call
  // direction parent -> child. Restrained size, deliberately styled (not React
  // Flow's generic default marker). Inherits the edge's own color.
  arrowSize: 6,
  // Reduced-opacity suffix (hex alpha) applied to the source node's identity
  // color for a completed edge; failed edges use the status-failed color at
  // full opacity plus a dashed stroke, independent of agent identity —
  // matching the Two-Palette Rule (state overrides identity when a path
  // failed, exactly as DESIGN.md already establishes for badges).
  completedAlphaSuffix: 'd9', // ~85% (was ~70% b3 — lifted with the heavier stroke, §3b)
}
