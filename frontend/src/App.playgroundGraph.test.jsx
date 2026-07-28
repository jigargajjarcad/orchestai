import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { setApiKey, clearApiKey } from './apiKey'

// jsdom cannot measure ReactFlow — mock it to a probe that exposes the derived
// node count (same technique as PlaygroundExecutionGraph.test.jsx). This keeps
// the test on App's wiring (toggle, additive dispatch, handoff), not ReactFlow.
vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }) => <div>{children}</div>,
  ReactFlow: ({ nodes, edges }) => (
    <div data-testid="rf" data-node-count={nodes.length} data-edge-count={edges.length} />
  ),
  Background: () => null,
  Controls: () => null,
  BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => ({ fitView: () => {} }),
}))

const { default: App } = await import('./App')

const TASK_ID = '11111111-2222-3333-4444-555555555555'
const T = (s) => `2026-07-28T08:00:${String(s).padStart(2, '0')}.000Z`

// A controllable EventSource: App does `new EventSource(url)`, sets `.onmessage`;
// the test drives the stream by calling instance.emit(event).
let esInstances
class MockEventSource {
  constructor(url) { this.url = url; this.onmessage = null; this.onerror = null; this.closed = false; esInstances.push(this) }
  close() { this.closed = true }
  emit(evt) { this.onmessage?.({ data: JSON.stringify(evt) }) }
}

function jsonOk(body) { return Promise.resolve({ ok: true, json: async () => body }) }

beforeEach(() => {
  esInstances = []
  globalThis.EventSource = MockEventSource
  globalThis.fetch = vi.fn((url, opts = {}) => {
    if (url.endsWith('/memories')) return jsonOk([])
    if (url.endsWith('/tasks') && opts.method === 'POST') return jsonOk({ id: TASK_ID })
    if (url.endsWith(`/tasks/${TASK_ID}/start`)) return jsonOk({})
    if (url.endsWith(`/tasks/${TASK_ID}/stream-ticket`)) return jsonOk({ ticket: 'tkt' })
    if (url.includes(`/tasks/${TASK_ID}?`)) return jsonOk({ finalResult: 'The final answer.' })
    return jsonOk({})
  })
  setApiKey('test-key') // mount past the API-key gate (in-memory, no storage)
})

afterEach(() => {
  clearApiKey()
  vi.restoreAllMocks()
})

async function runTask() {
  const before = esInstances.length
  fireEvent.change(screen.getByPlaceholderText(/Research LangGraph/i), { target: { value: 'My Task' } })
  fireEvent.change(screen.getByPlaceholderText(/Describe what you need/i), { target: { value: 'Do the thing' } })
  await act(async () => { fireEvent.click(screen.getByText('Run Agents')) })
  await waitFor(() => expect(esInstances.length).toBe(before + 1))
  return esInstances[esInstances.length - 1]
}

describe('App — Playground Graph/List toggle + Observability handoff (P2-M5)', () => {
  it('defaults to the Graph view and toggles to the List result view (state preserved)', async () => {
    render(<App />)
    // Default = graph -> the graph empty state, not the list empty state.
    expect(await screen.findByText('No execution yet')).toBeInTheDocument()
    expect(screen.queryByText('No results yet')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('radio', { name: 'List' }))
    expect(screen.getByText('No results yet')).toBeInTheDocument()
    expect(screen.queryByText('No execution yet')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('radio', { name: 'Graph' }))
    expect(screen.getByText('No execution yet')).toBeInTheDocument()
  })

  it('additively folds the SSE stream into the graph without disturbing the card feed', async () => {
    render(<App />)
    const es = await runTask()

    await act(async () => {
      es.emit({ event: 'task_started', payload: {}, timestamp: T(0) })
      es.emit({ event: 'agent_started', payload: { agentExecutionId: 'a-data', agentType: 'Data' }, timestamp: T(1) })
      es.emit({ event: 'tool_started', payload: { agentExecutionId: 'a-data', agentType: 'Data', toolName: 'db_query' }, timestamp: T(2) })
    })

    // Graph got orchestrator + agent + tool = 3 nodes.
    await waitFor(() => expect(screen.getByTestId('rf')).toHaveAttribute('data-node-count', '3'))
    // Card feed still rendered the agent card (existing reducer untouched).
    expect(screen.getByText('Agent Executions')).toBeInTheDocument()
  })

  it('reveals the Observability handoff button only after task_completed', async () => {
    render(<App />)
    const es = await runTask()

    await act(async () => {
      es.emit({ event: 'task_started', payload: {}, timestamp: T(0) })
      es.emit({ event: 'agent_started', payload: { agentExecutionId: 'a-data', agentType: 'Data' }, timestamp: T(1) })
    })
    expect(screen.queryByText(/View full trace in Observability/i)).not.toBeInTheDocument()

    await act(async () => {
      es.emit({ event: 'task_completed', payload: { totalCostUsd: 0.017 }, timestamp: T(2) })
    })
    await waitFor(() => expect(screen.getByText(/View full trace in Observability/i)).toBeInTheDocument())
  })

  it('resets the graph on a fresh run (no carryover from the previous stream)', async () => {
    render(<App />)
    const es1 = await runTask()
    await act(async () => {
      es1.emit({ event: 'task_started', payload: {}, timestamp: T(0) })
      es1.emit({ event: 'agent_started', payload: { agentExecutionId: 'a-data', agentType: 'Data' }, timestamp: T(1) })
      es1.emit({ event: 'task_completed', payload: { totalCostUsd: 0.01 }, timestamp: T(2) })
    })
    await waitFor(() => expect(screen.getByTestId('rf')).toHaveAttribute('data-node-count', '2'))

    // Second run: reset path clears the graph before the new stream arrives, so
    // the graph returns to its empty state (no ReactFlow surface) — no carryover.
    await runTask()
    await waitFor(() => expect(screen.getByText('No execution yet')).toBeInTheDocument())
    expect(screen.queryByTestId('rf')).not.toBeInTheDocument()
  })
})
