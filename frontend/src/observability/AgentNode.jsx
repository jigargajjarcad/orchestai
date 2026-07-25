// frontend/src/observability/AgentNode.jsx
//
// Custom React Flow node type for an agent-execution span (Task 2, plan
// §3a.3/§3b/§3c). Visual contract, reproducing the Task 1 style-guide
// reference exactly:
//   - a left identity rail + small identity dot + agent name + status pill
//   - a meta row carrying the required duration figure (§3a.3 item 1) and,
//     when present, cost
//   - a span-extent mini-bar (§3a.3 item 2) beneath the meta row
//
// Two-Palette Rule (DESIGN.md, reaffirmed by the plan's Task 2 acceptance
// criteria): identity color (the rail/dot, keyed by agentName into
// `agentColors`) NEVER changes with status. Status is expressed in exactly
// two places: the StatusPill's fill/text, and — when the node is selected —
// the selection ring's color (`elevation.nodeSelected`'s `currentColor`,
// driven here by setting the node's own CSS `color` to the status color).
// Every other color on the card (rail, dot, mini-bar fill) stays pure
// identity. Hover/focus get a neutral lift (surface.cardHover + a small
// upward translate) that carries no status or identity meaning of its own.

import { memo, useState } from 'react'
import { Handle, Position } from '@xyflow/react'
import { colors as sharedColors } from '../theme/tokens'
import { agentColors, statusColors, surface, elevation, typography, node as nodeGeometry } from './timelineTheme'
import { StatusPill, SpanExtentBar } from './NodeParts'
import { formatDuration, formatCost } from './format'

function AgentNodeImpl({ data, selected }) {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)

  const {
    label,
    agentName,
    status = 'Pending',
    durationMs = null,
    costUsd = null,
    extentPct = { offset: 0, width: 0 },
  } = data ?? {}

  const identityColor = agentColors[agentName] ?? statusColors.Pending
  const statusColor = statusColors[status] ?? statusColors.Pending
  const lifted = hovered || focused
  const cost = formatCost(costUsd)

  const ariaLabel = `${label ?? agentName}, ${status}${
    durationMs != null ? `, ${formatDuration(durationMs)}` : ''
  }`

  return (
    <div
      role="button"
      aria-label={ariaLabel}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
      style={{
        position: 'relative',
        width: nodeGeometry.agentWidth,
        minHeight: nodeGeometry.agentHeight,
        background: lifted ? surface.cardHover : surface.card,
        borderRadius: nodeGeometry.radius,
        border: `1px solid ${surface.panelBorder}`,
        boxShadow: selected ? elevation.nodeSelected : elevation.node,
        // Carries the selection ring's color only (see comment above) — text
        // elements below all set their own explicit `color`, so this never
        // leaks into node content.
        color: statusColor,
        cursor: 'pointer',
        transform: lifted ? 'translateY(-2px)' : 'none',
        transition: 'transform 120ms ease, box-shadow 120ms ease, background 120ms ease',
        overflow: 'hidden',
        boxSizing: 'border-box',
      }}
    >
      <Handle type="target" position={Position.Left} style={{ background: identityColor, border: 'none', width: 8, height: 8 }} />
      <Handle type="source" position={Position.Right} style={{ background: identityColor, border: 'none', width: 8, height: 8 }} />

      {/* Identity rail — pure identity color, never status. */}
      <div
        aria-hidden="true"
        style={{ position: 'absolute', top: 0, left: 0, bottom: 0, width: 4, background: identityColor }}
      />

      <div style={{ padding: '10px 12px 10px 16px', display: 'flex', flexDirection: 'column', gap: 6 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
          <span
            aria-hidden="true"
            style={{ width: 8, height: 8, borderRadius: '50%', background: identityColor, flexShrink: 0 }}
          />
          <span
            style={{
              flex: 1,
              minWidth: 0,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              color: sharedColors.text,
              fontFamily: typography.uiFontFamily,
              fontSize: typography.nodeTitle.fontSize,
              fontWeight: typography.nodeTitle.fontWeight,
              letterSpacing: typography.nodeTitle.letterSpacing,
            }}
          >
            {label ?? agentName}
          </span>
          <StatusPill status={status} />
        </div>

        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            fontFamily: typography.dataFontFamily,
            fontSize: typography.nodeData.fontSize,
            fontWeight: typography.nodeData.fontWeight,
            color: sharedColors.subtext0,
          }}
        >
          <span>{formatDuration(durationMs)}</span>
          {cost && <span style={{ opacity: 0.75 }}>{cost}</span>}
        </div>

        <SpanExtentBar offsetPct={extentPct.offset} widthPct={extentPct.width} color={identityColor} />
      </div>
    </div>
  )
}

export const AgentNode = memo(AgentNodeImpl)
