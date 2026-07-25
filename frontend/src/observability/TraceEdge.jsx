// frontend/src/observability/TraceEdge.jsx
//
// Custom React Flow edge type for parent -> child span relationships
// (Task 2, plan §3b — review-refined edge visual language).
//
// Rules (verbatim from §3b, unchanged from Task 1's color/identity logic,
// only the stroke weight and arrowhead are new in this review pass):
//   - Heavier stroke: `edge.strokeWidth` (2.5) idle/completed, or
//     `edge.strokeWidthActive` (3) while running.
//   - Explicit directional arrowhead at the target (child) end, deliberately
//     styled (not React Flow's generic default marker), inheriting the
//     edge's own color.
//   - Color/identity rules: a completed edge takes the SOURCE node's
//     identity color at reduced opacity (`edge.completedAlphaSuffix`); a
//     running edge is `statusColors.Running` with an animated dashed flow;
//     a failed edge is always `statusColors.Failed`, dashed, full opacity,
//     regardless of identity — state overrides identity, matching the
//     Two-Palette Rule the badge system already establishes.
//   - The running-edge dash animation respects `prefers-reduced-motion`:
//     reduced-motion users get the same dashed running treatment, just
//     static (no animation).
//
// `data.identityColor` (a resolved hex, e.g. `agentColors[agentName]` or
// `toolCallColor`) and `data.edgeStatus` (`'completed' | 'running' |
// 'failed' | 'pending'`) are supplied by the caller — for this task, the
// hand-constructed sample data; Task 3's layout module resolves the same
// two values from the real span tree it already builds.

import { useEffect, useState } from 'react'
import { BaseEdge, getBezierPath } from '@xyflow/react'
import { statusColors, edge as edgeGeometry } from './timelineTheme'

function usePrefersReducedMotion() {
  const [reduced, setReduced] = useState(() =>
    typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
      : false
  )

  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return undefined
    const mql = window.matchMedia('(prefers-reduced-motion: reduce)')
    const onChange = () => setReduced(mql.matches)
    mql.addEventListener('change', onChange)
    return () => mql.removeEventListener('change', onChange)
  }, [])

  return reduced
}

export function TraceEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  data,
  markerEnd: _ignoredMarkerEnd,
}) {
  const reducedMotion = usePrefersReducedMotion()
  const { edgeStatus = 'pending', identityColor = statusColors.Pending } = data ?? {}

  const isRunning = edgeStatus === 'running'
  const isFailed = edgeStatus === 'failed'

  // State overrides identity for a failed path; otherwise identity (at
  // reduced opacity for a completed/idle edge) or the running-blue.
  const strokeColor = isFailed
    ? statusColors.Failed
    : isRunning
      ? statusColors.Running
      : `${identityColor}${edgeGeometry.completedAlphaSuffix}`

  const strokeWidth = isRunning ? edgeGeometry.strokeWidthActive : edgeGeometry.strokeWidth
  const dashed = isFailed || isRunning
  const animate = isRunning && !reducedMotion

  const [path] = getBezierPath({ sourceX, sourceY, sourcePosition, targetX, targetY, targetPosition })

  const markerId = `trace-arrow-${id}`

  return (
    <>
      <defs>
        <marker
          id={markerId}
          viewBox="0 0 10 10"
          refX="8.5"
          refY="5"
          markerWidth={edgeGeometry.arrowSize}
          markerHeight={edgeGeometry.arrowSize}
          orient="auto-start-reverse"
        >
          <path d="M 0 0 L 10 5 L 0 10 z" fill={strokeColor} />
        </marker>
        {animate && (
          <style>{`
            @keyframes trace-edge-flow-${id} {
              to { stroke-dashoffset: -20; }
            }
          `}</style>
        )}
      </defs>
      <BaseEdge
        id={id}
        path={path}
        markerEnd={`url(#${markerId})`}
        style={{
          stroke: strokeColor,
          strokeWidth,
          strokeDasharray: dashed ? '6 4' : undefined,
          animation: animate ? `trace-edge-flow-${id} 0.6s linear infinite` : undefined,
        }}
      />
    </>
  )
}
