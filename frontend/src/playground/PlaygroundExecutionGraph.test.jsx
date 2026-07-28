import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import {
  emptyGraphState, applyEvent, toSpanArray,
} from './playgroundSpanModel'

// Shared spy for the reused ReactFlow `fitView()` (Defect 1 regression). Hoisted
// so it is usable inside the vi.mock factory below.
const fitViewSpy = vi.hoisted(() => vi.fn())

// jsdom cannot measure ReactFlow's viewport (no ResizeObserver layout), so we
// mock @xyflow/react to a thin stand-in that surfaces the nodes/edges our
// component feeds it. This keeps the test on OUR contract — "does the component
// derive and hand the correct graph to ReactFlow" — while ReactFlow's own
// rendering stays covered by its library tests + the Playwright visual pass.
// useReactFlow is stubbed to expose the fitView spy AutoFitView calls.
vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }) => <div data-testid="rf-provider">{children}</div>,
  ReactFlow: ({ nodes, edges, children }) => (
    <div data-testid="rf" data-node-count={nodes.length} data-edge-count={edges.length}>
      {nodes.map(n => <div key={n.id} data-testid="rf-node" data-node-id={n.id} />)}
      {children}
    </div>
  ),
  Background: () => <div data-testid="rf-background" />,
  Controls: () => <div data-testid="rf-controls" />,
  BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => ({ fitView: fitViewSpy }),
}))

// Import AFTER the mock is registered.
const { PlaygroundExecutionGraph } = await import('./PlaygroundExecutionGraph')

const ev = (event, payload = {}, timestamp) => ({ event, payload, timestamp })
const T = (s) => `2026-07-28T08:00:${String(s).padStart(2, '0')}.000Z`
const spansOf = (events) => toSpanArray(events.reduce(applyEvent, emptyGraphState()))

const DATA = 'a-data', RESEARCH = 'a-research'

// A realistic parallel trace: orchestrator → Data + Research (parallel) → tools → review.
const liveSpans = spansOf([
  ev('task_started', {}, T(0)),
  ev('orchestrator_plan', {}, T(1)),
  ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(2)),
  ev('agent_started', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(2)),
  ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(3)),
  ev('tool_completed', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query', success: true }, T(4)),
  ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data' }, T(5)),
  ev('manager_review_started', {}, T(6)),
  ev('manager_review_completed', {}, T(7)),
])

describe('PlaygroundExecutionGraph (P2-M5 live graph render)', () => {
  it('renders the empty state when there are no spans', () => {
    render(<PlaygroundExecutionGraph spans={[]} />)
    expect(screen.getByText('No execution yet')).toBeInTheDocument()
    expect(screen.queryByTestId('playground-execution-graph')).not.toBeInTheDocument()
  })

  it('renders the graph container and the reused ReactFlow surface when spans exist', () => {
    render(<PlaygroundExecutionGraph spans={liveSpans} />)
    expect(screen.getByTestId('playground-execution-graph')).toBeInTheDocument()
    expect(screen.getByTestId('rf-provider')).toBeInTheDocument()
    expect(screen.getByTestId('rf-background')).toBeInTheDocument()
    expect(screen.getByTestId('rf-controls')).toBeInTheDocument()
    expect(screen.queryByText('No execution yet')).not.toBeInTheDocument()
  })

  it('feeds every derived span to ReactFlow as a node (orchestrator + 2 agents + tool + review)', () => {
    render(<PlaygroundExecutionGraph spans={liveSpans} />)
    const rf = screen.getByTestId('rf')
    // orchestrator, Data, Research, db_query, manager-review = 5 spans
    expect(liveSpans).toHaveLength(5)
    expect(rf).toHaveAttribute('data-node-count', '5')
    expect(Number(rf.getAttribute('data-edge-count'))).toBeGreaterThan(0)
    expect(screen.getAllByTestId('rf-node')).toHaveLength(5)
  })

  it('renders a time-axis tick row derived from the trace duration', () => {
    render(<PlaygroundExecutionGraph spans={liveSpans} />)
    // 5 ticks; the first is always 0ms, the last is the full duration.
    expect(screen.getByText('0ms')).toBeInTheDocument()
    // total trace = T(0)..T(7) = 7s -> final tick formatted in seconds
    expect(screen.getByText('7.0s')).toBeInTheDocument()
  })
})

describe('PlaygroundExecutionGraph — auto-refit as the trace grows (Defect 1 regression)', () => {
  beforeEach(() => {
    fitViewSpy.mockClear()
    // Run AutoFitView's deferred fit synchronously so the observable effect
    // (fitView invoked) is deterministic in jsdom.
    vi.stubGlobal('requestAnimationFrame', (cb) => { cb(); return 1 })
    vi.stubGlobal('cancelAnimationFrame', () => {})
  })
  afterEach(() => vi.unstubAllGlobals())

  // Short early layout: orchestrator + one agent, ~1s trace.
  const smallSpans = spansOf([
    ev('task_started', {}, T(0)),
    ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
  ])
  // Materially larger layout: more nodes + a ~30s trace (bigger time-scaled X).
  const grownSpans = spansOf([
    ev('task_started', {}, T(0)),
    ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
    ev('agent_started', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(1)),
    ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(2)),
    ev('tool_completed', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search', success: true }, T(30)),
    ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data' }, T(30)),
    ev('task_completed', {}, T(30)),
  ])

  it('fits the viewport on initial render (nodes visible without interaction)', () => {
    render(<PlaygroundExecutionGraph spans={smallSpans} />)
    expect(fitViewSpy).toHaveBeenCalled()
  })

  it('re-fits when the layout grows materially — the streaming-goes-blank defect', () => {
    const { rerender } = render(<PlaygroundExecutionGraph spans={smallSpans} />)
    const afterInitial = fitViewSpy.mock.calls.length
    expect(afterInitial).toBeGreaterThan(0)

    // Simulates the live trace expanding (~1s -> ~30s, more nodes): the framing
    // signature changes, so fitView runs again to bring the new nodes into view.
    rerender(<PlaygroundExecutionGraph spans={grownSpans} />)
    expect(fitViewSpy.mock.calls.length).toBeGreaterThan(afterInitial)
  })

  it('completion frame is fitted — a completed graph is never left off-canvas', () => {
    // Mount directly with the final, grown+completed layout (the exact state the
    // genuine run ended in): fitView must have run so the canvas is not blank.
    render(<PlaygroundExecutionGraph spans={grownSpans} />)
    expect(fitViewSpy).toHaveBeenCalled()
  })

  it('does NOT re-fit on a status/cost-only update that does not move nodes (no jitter)', () => {
    const base = spansOf([
      ev('task_started', {}, T(0)),
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
    ])
    const { rerender } = render(<PlaygroundExecutionGraph spans={base} />)
    const calls = fitViewSpy.mock.calls.length
    // New array, identical node geometry -> identical framing signature -> no refit.
    rerender(<PlaygroundExecutionGraph spans={base.map(s => ({ ...s }))} />)
    expect(fitViewSpy.mock.calls.length).toBe(calls)
  })
})
