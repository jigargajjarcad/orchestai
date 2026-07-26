// frontend/src/observability/DensePaletteHarness.jsx
//
// R1 Task 1 — live verification harness for the palette-density stress test
// (master plan §3.2/§11). Renders `densePaletteFixture.js`'s 47-span dense
// graph through the REAL, unmodified layout engine (`computeGraphLayout`)
// and the REAL, unmodified `AgentNode`/`ToolCallNode`/`TraceEdge` components
// — same components `GraphHarness.jsx` (Task 2's verification harness) and
// the shipped `ObservabilityPage.jsx` use. Nothing here is wired into any
// product route (App.jsx/routing untouched); this is standalone verification
// scaffolding, importable either by:
//   - a temporary swap into `src/main.jsx` for a live dev-server screenshot
//     pass (revert before committing, same discipline `GraphHarness.jsx`
//     documents), or
//   - an `ssrLoadModule`/`renderToStaticMarkup` isolated-render script.
//
// Unlike `GraphHarness.jsx` (hand-positioned nodes), every node position,
// edge, status, and span-extent bar here is DERIVED from the fixture's
// span timestamps by the real `computeGraphLayout` — exactly the same code
// path a real backend trace would drive through `ObservabilityPage.jsx`.

import { ReactFlow, ReactFlowProvider, Background, Controls, BackgroundVariant, MiniMap } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { AgentNode } from './AgentNode'
import { ToolCallNode } from './ToolCallNode'
import { TraceEdge } from './TraceEdge'
import { computeGraphLayout } from './layout'
import { denseSpans, denseFixtureSummary } from './densePaletteFixture'
import { surface, agentColors } from './timelineTheme'

const nodeTypes = { agentNode: AgentNode, toolCallNode: ToolCallNode }
const edgeTypes = { traceEdge: TraceEdge }

const { nodes, edges } = computeGraphLayout(denseSpans)

// eslint-disable-next-line no-console
console.info('[DensePaletteHarness] fixture summary', denseFixtureSummary)

export function DensePaletteHarness() {
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
          minZoom={0.1}
        >
          <Background variant={BackgroundVariant.Dots} gap={16} size={1} color={surface.panelBorder} />
          <Controls />
          <MiniMap
            nodeColor={(n) => (n.type === 'agentNode' ? agentColors[n.data.agentName] ?? '#666' : '#888')}
            maskColor="rgba(0,0,0,0.6)"
          />
        </ReactFlow>
      </ReactFlowProvider>
    </div>
  )
}
