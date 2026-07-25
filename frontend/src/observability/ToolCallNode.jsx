// frontend/src/observability/ToolCallNode.jsx
//
// Custom React Flow node type for a tool-call span (Task 2, plan §3a.3/§3b/
// §3c). Smaller and visually secondary to AgentNode — no identity rail, a
// compact single-row layout, no dispatching-agent color: tool calls always
// render in `toolCallColor` (mauve) regardless of which agent dispatched
// them, matching ObservabilityPage.jsx's SpanRow rule (`span.spanType ===
// 'ToolCall' ? colors.agentMauve : ...`) and timelineTheme.js's own comment
// on `toolCallColor`.
//
// Same Two-Palette Rule as AgentNode: identity (mauve) never changes with
// status; status lives only in the StatusPill and the selection ring.

import { memo, useState } from 'react'
import { Handle, Position } from '@xyflow/react'
import { colors as sharedColors } from '../theme/tokens'
import { toolCallColor, statusColors, surface, elevation, typography, node as nodeGeometry } from './timelineTheme'
import { StatusPill, SpanExtentBar } from './NodeParts'
import { formatDuration } from './format'

// Task 6, carry-forward #1 (see AgentNode.jsx's identical comment for the
// full explanation): the onFocus/onBlur handlers this component used to
// have on the div below were dead code (focus lands on React Flow's node
// wrapper one level up, never on this inner div) — removed, not re-wired.
// The scoped `.react-flow__node:focus-visible` CSS override in
// timelineFocus.css is the actual fix for visible keyboard-focus feedback.
function ToolCallNodeImpl({ data, selected }) {
  const [hovered, setHovered] = useState(false)

  const { label, status = 'Pending', durationMs = null, extentPct = { offset: 0, width: 0 } } = data ?? {}

  const statusColor = statusColors[status] ?? statusColors.Pending
  const lifted = hovered

  const ariaLabel = `Tool call ${label}, ${status}${durationMs != null ? `, ${formatDuration(durationMs)}` : ''}`

  return (
    <div
      role="button"
      aria-label={ariaLabel}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        position: 'relative',
        width: nodeGeometry.toolCallWidth,
        minHeight: nodeGeometry.toolCallHeight,
        background: lifted ? surface.cardHover : surface.card,
        borderRadius: nodeGeometry.radius,
        border: `1px solid ${toolCallColor}40`,
        boxShadow: selected ? elevation.nodeSelected : elevation.node,
        color: statusColor,
        cursor: 'pointer',
        transform: lifted ? 'translateY(-2px)' : 'none',
        transition: 'transform 120ms ease, box-shadow 120ms ease, background 120ms ease',
        boxSizing: 'border-box',
        padding: '7px 10px',
        display: 'flex',
        flexDirection: 'column',
        gap: 4,
      }}
    >
      <Handle type="target" position={Position.Left} style={{ background: toolCallColor, border: 'none', width: 6, height: 6 }} />
      <Handle type="source" position={Position.Right} style={{ background: toolCallColor, border: 'none', width: 6, height: 6 }} />

      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <span aria-hidden="true" style={{ width: 6, height: 6, borderRadius: 2, background: toolCallColor, flexShrink: 0 }} />
        <span
          style={{
            flex: 1,
            minWidth: 0,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            color: sharedColors.text,
            fontFamily: typography.uiFontFamily,
            fontSize: typography.nodeMeta.fontSize,
            fontWeight: typography.nodeTitle.fontWeight,
          }}
        >
          {label}
        </span>
        <StatusPill status={status} />
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          fontFamily: typography.dataFontFamily,
          fontSize: 10,
          fontWeight: typography.nodeData.fontWeight,
          color: sharedColors.subtext0,
        }}
      >
        {formatDuration(durationMs)}
      </div>

      <SpanExtentBar offsetPct={extentPct.offset} widthPct={extentPct.width} color={toolCallColor} height={4} />
    </div>
  )
}

export const ToolCallNode = memo(ToolCallNodeImpl)
