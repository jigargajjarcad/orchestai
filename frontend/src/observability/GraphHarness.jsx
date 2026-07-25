// frontend/src/observability/GraphHarness.jsx
//
// Local verification harness for Task 2's custom React Flow node/edge
// components. NOT wired into the running app's routes (App.jsx/routing are
// untouched, per the task's scope boundary) — this is a standalone,
// self-contained component, importable either by:
//   - a temporary swap into src/main.jsx for a live dev-server check
//     (reverted before committing, same discipline as the isolated-render
//     technique's "revert any temporary export"), or
//   - an `ssrLoadModule`/`renderToStaticMarkup` isolated-render script.
//
// Renders `statusMatrixGraph` (all five statuses x both node types, stacked
// in a grid) above `composedTraceGraph` (a small realistic parent/child
// trace exercising the completed/running/failed edge states and the
// span-extent mini-bar's overlap semantics) in one scrollable/pannable
// React Flow canvas — mirroring Task 1's style-guide artifact (states grid
// + composed example) but with the real, wired components.

import { ReactFlow, ReactFlowProvider, Background, Controls, BackgroundVariant } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { AgentNode } from './AgentNode'
import { ToolCallNode } from './ToolCallNode'
import { TraceEdge } from './TraceEdge'
import { statusMatrixGraph, composedTraceGraph } from './sampleGraphData'
import { surface } from './timelineTheme'

const nodeTypes = { agentNode: AgentNode, toolCallNode: ToolCallNode }
const edgeTypes = { traceEdge: TraceEdge }

const nodes = [...statusMatrixGraph.nodes, ...composedTraceGraph.nodes]
const edges = [...statusMatrixGraph.edges, ...composedTraceGraph.edges]

export function GraphHarness() {
  return (
    <div style={{ width: '100vw', height: '100vh', background: surface.canvas }}>
      <ReactFlowProvider>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          edgeTypes={edgeTypes}
          fitView
          nodesFocusable
          edgesFocusable
          minZoom={0.2}
        >
          <Background variant={BackgroundVariant.Dots} gap={16} size={1} color={surface.panelBorder} />
          <Controls />
        </ReactFlow>
      </ReactFlowProvider>
    </div>
  )
}
