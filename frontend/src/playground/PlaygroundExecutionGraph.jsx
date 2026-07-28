// frontend/src/playground/PlaygroundExecutionGraph.jsx
//
// P2-M5 — the Playground's live execution graph. Presentational only: it feeds
// the live span array through the REUSED, UNMODIFIED observability layout engine
// and node/edge primitives. No observability/* file is modified.

import { useMemo } from 'react'
import { ReactFlow, ReactFlowProvider, Background, Controls, BackgroundVariant } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { computeGraphLayout, computeTraceBounds } from '../observability/layout'
import { AgentNode } from '../observability/AgentNode'
import { ToolCallNode } from '../observability/ToolCallNode'
import { TraceEdge } from '../observability/TraceEdge'
import { surface as timelineSurface, focusRing } from '../observability/timelineTheme'
import { colors, radii } from '../theme/tokens'
import { EmptyState } from '../components/EmptyState'

const NODE_TYPES = { agentNode: AgentNode, toolCallNode: ToolCallNode }
const EDGE_TYPES = { traceEdge: TraceEdge }
const TICK_COUNT = 5

function formatMs(ms) {
  if (ms == null) return ''
  if (ms < 1000) return `${Math.round(ms)}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

/**
 * @param {{ spans: Array }} props - live TimelineSpanDto-shaped spans from
 *   usePlaygroundExecutionGraph. Re-renders as spans are appended/updated.
 */
export function PlaygroundExecutionGraph({ spans }) {
  const graphLayout = useMemo(
    () => (spans && spans.length > 0 ? computeGraphLayout(spans) : null),
    [spans],
  )
  const ticks = useMemo(() => {
    if (!spans || spans.length === 0) return []
    const { traceDurationMs } = computeTraceBounds(spans)
    return Array.from({ length: TICK_COUNT }, (_, i) =>
      formatMs((traceDurationMs * i) / (TICK_COUNT - 1)))
  }, [spans])

  if (!graphLayout) {
    return (
      <EmptyState
        title="No execution yet"
        description="Run a task on the left — the orchestrator, its agents, their tool calls, and the manager review will appear here as a live graph."
      />
    )
  }

  return (
    <div data-testid="playground-execution-graph">
      <div
        style={{
          display: 'flex', justifyContent: 'space-between', padding: '0 14px 6px',
          fontFamily: 'monospace', fontSize: 10, color: colors.overlay0,
        }}
      >
        {ticks.map((tick, i) => <span key={i}>{tick}</span>)}
      </div>
      <div
        style={{
          width: '100%', height: 460, borderRadius: radii.lg, overflow: 'hidden',
          background: timelineSurface.canvas, border: `1px solid ${timelineSurface.panelBorder}`,
          '--observability-focus-ring': focusRing,
        }}
      >
        <ReactFlowProvider>
          <ReactFlow
            nodes={graphLayout.nodes}
            edges={graphLayout.edges}
            nodeTypes={NODE_TYPES}
            edgeTypes={EDGE_TYPES}
            fitView
            nodesFocusable
            edgesFocusable
            minZoom={0.2}
          >
            <Background variant={BackgroundVariant.Dots} gap={16} size={1} color={timelineSurface.panelBorder} />
            <Controls />
          </ReactFlow>
        </ReactFlowProvider>
      </div>
    </div>
  )
}
