// frontend/src/observability/NodeParts.jsx
//
// Shared building blocks for AgentNode/ToolCallNode (Task 2): the status
// pill and the per-node span-extent mini-bar (plan §3a.3, item 2 — the
// direct per-node equivalent of ObservabilityPage.jsx's SpanRow Gantt bar).
//
// Two-Palette Rule (DESIGN.md; reaffirmed for this view by the plan's Task 2
// acceptance criteria): identity tones (agentColors/toolCallColor) and status
// tones (statusColors) never blend. StatusPill is the one place status color
// is rendered as a fill; SpanExtentBar is deliberately identity-colored only
// (never status-colored, including for Failed) so duration/overlap stays
// legible without re-introducing a second status signal outside the pill +
// selection ring (see AgentNode.jsx's selection-ring comment).

import { statusColors, statusTextColors, surface, typography } from './timelineTheme'

export function StatusPill({ status }) {
  const color = statusColors[status] ?? statusColors.Pending
  // F1.1 — the fill and border stay on the status hue; only the glyph moves to
  // the AA-safe text tier. Measured before: Completed 3.85, Failed 2.89 against
  // this pill's own 15% tint — both short of WCAG 1.4.3 AA's 4.5:1.
  const textColor = statusTextColors[status] ?? statusTextColors.Pending
  return (
    <span
      style={{
        display: 'inline-block',
        flexShrink: 0,
        background: `${color}26`, // ~15% alpha fill, text at full opacity
        color: textColor,
        border: `1px solid ${color}80`,
        borderRadius: 4,
        padding: '1px 7px',
        fontFamily: typography.uiFontFamily,
        fontSize: 9,
        fontWeight: 700,
        letterSpacing: '0.06em',
        textTransform: 'uppercase',
        lineHeight: '14px',
        whiteSpace: 'nowrap',
      }}
    >
      {status}
    </span>
  )
}

// `offsetPct`/`widthPct` are trace-relative percentages (0-100) — supplied
// directly via node data for this task's hand-constructed samples; Task 3's
// layout math feeds the same two numbers from real StartedAt/DurationMs vs.
// traceStart/traceDurationMs (identical math to SpanRow's own
// offsetPct/widthPct). Nothing about this component's contract changes when
// that happens.
export function SpanExtentBar({ offsetPct, widthPct, color, height = 5 }) {
  const safeOffset = Math.min(Math.max(offsetPct ?? 0, 0), 100)
  const maxWidth = Math.max(100 - safeOffset, 0)
  const safeWidth = Math.min(Math.max(widthPct ?? 0, 0), maxWidth || 100)

  return (
    <div
      aria-hidden="true"
      style={{
        position: 'relative',
        height,
        borderRadius: height / 2,
        background: surface.panelBorder,
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          position: 'absolute',
          top: 0,
          left: `${safeOffset}%`,
          width: `${Math.max(safeWidth, 1.5)}%`,
          minWidth: 2,
          height: '100%',
          borderRadius: height / 2,
          background: color,
        }}
      />
    </div>
  )
}
