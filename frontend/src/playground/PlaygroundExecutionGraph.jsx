// frontend/src/playground/PlaygroundExecutionGraph.jsx
//
// P2-M5 — the Playground's live execution graph. Presentational only: it feeds
// the live span array through the REUSED, UNMODIFIED observability layout engine
// and node/edge primitives. No observability/* file is modified.

import { useMemo, useEffect } from 'react'
import { ReactFlow, ReactFlowProvider, Background, Controls, BackgroundVariant, useReactFlow } from '@xyflow/react'
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

// fitView options shared by the initial fit (fitViewOptions prop) and the live
// AutoFitView refit. The `minZoom` here is the floor FITTING may use — deliberately
// far below the ReactFlow component's interactive `minZoom={0.2}` (which only caps
// user scroll/pinch). Without it, the imperative fitView() inherits 0.2 as its floor
// and cannot zoom out enough to frame tall traces: a genuine 23-node run needs ~0.14,
// so ~7 nodes (Orchestrator/Data/Research/Manager Review) fell outside the 460px
// canvas. 0.05 frames very large traces whole (the reference Observability graph
// likewise fits below 0.2 via its default initial fit); `padding: 0.1` leaves a small
// margin without wasting fitting room. Normal traces stay readable — fitView never
// zooms out MORE than needed to frame the content.
const FIT_VIEW_OPTIONS = { padding: 0.1, minZoom: 0.05 }

function formatMs(ms) {
  if (ms == null) return ''
  if (ms < 1000) return `${Math.round(ms)}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

// ReactFlow's `fitView` PROP only fits on the initial mount. In a LIVE graph the
// time-scaled layout keeps growing as SSE events stream in (a ~8s trace expands
// to ~98s), so the once-fit viewport stops containing the nodes and the canvas
// goes visually blank by completion (the P2-M5 genuine-run defect). This child
// lives inside the ReactFlow provider and re-runs fitView whenever the framing-
// relevant layout changes (`signature` = node count + spatial extent), keeping
// the whole graph in view during streaming and at completion — no user toggle
// required. `duration: 0` = instant reframe, so there's no per-event animation
// jitter. Keyed on the coarse signature (not raw spans) so pure status/cost
// updates that don't move nodes don't trigger a needless refit.
function AutoFitView({ signature }) {
  const { fitView } = useReactFlow()
  useEffect(() => {
    // Defer one frame so ReactFlow has measured the newly-added nodes before we
    // fit to them (fall back to a macrotask where rAF is unavailable, e.g. tests).
    const run = () => { fitView({ duration: 0, ...FIT_VIEW_OPTIONS }) }
    if (typeof requestAnimationFrame === 'function') {
      const raf = requestAnimationFrame(run)
      return () => cancelAnimationFrame(raf)
    }
    const t = setTimeout(run, 0)
    return () => clearTimeout(t)
  }, [signature, fitView])
  return null
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
  // Framing-relevant fingerprint of the layout: node count + the graph's spatial
  // extent. Changes exactly when a node is added or the time-scaled positions
  // grow — i.e. when the viewport needs re-fitting — and NOT on status/cost-only
  // updates. Drives AutoFitView below.
  const fitSignature = useMemo(() => {
    if (!graphLayout || graphLayout.nodes.length === 0) return ''
    const xs = graphLayout.nodes.map(n => n.position.x)
    const ys = graphLayout.nodes.map(n => n.position.y)
    return `${graphLayout.nodes.length}:${Math.round(Math.max(0, ...xs))}:${Math.round(Math.max(0, ...ys))}`
  }, [graphLayout])

  if (!graphLayout) {
    return (
      <EmptyState
        title="No execution yet"
        description="Run a task on the left — the orchestrator, its agents, their tool calls, and the manager review will appear here as a live graph."
      />
    )
  }

  return (
    <div
      data-testid="playground-execution-graph"
      role="group"
      aria-label="Live execution graph: the orchestrator, its agents, their tool calls, and the manager review"
    >
      {/* Decorative time-axis ruler — the same durations are conveyed by each
          node's own label, so hide the tick row from assistive tech. */}
      <div
        aria-hidden="true"
        style={{
          display: 'flex', justifyContent: 'space-between', padding: '0 14px 6px',
          fontFamily: 'monospace', fontSize: 10, color: colors.overlay0,
        }}
      >
        {ticks.map((tick, i) => <span key={i}>{tick}</span>)}
      </div>
      <div
        style={{
          // P2-M6.2 — responsive height: caps at the desktop 460px (unchanged
          // when the viewport is tall), but shrinks toward 300px on short/phone
          // screens so the graph doesn't eat the whole viewport. fitView adapts.
          width: '100%', height: 'clamp(300px, 60svh, 460px)', borderRadius: radii.lg, overflow: 'hidden',
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
            fitViewOptions={FIT_VIEW_OPTIONS}
            nodesFocusable
            edgesFocusable
            minZoom={0.2}
          >
            <Background variant={BackgroundVariant.Dots} gap={16} size={1} color={timelineSurface.panelBorder} />
            <Controls />
            <AutoFitView signature={fitSignature} />
          </ReactFlow>
        </ReactFlowProvider>
      </div>
    </div>
  )
}
