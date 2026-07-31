import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act, within } from '@testing-library/react'
import { setApiKey, clearApiKey } from './apiKey'

// jsdom can't measure ReactFlow — mock it (same technique as the P2-M5 suite).
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

const TASK_ID = '99999999-2222-3333-4444-555555555555'
const T = (s) => `2026-07-31T09:00:${String(s).padStart(2, '0')}.000Z`
// Final result with an INLINE `code` span and a GFM table (P2-M6.2 targets).
const FINAL_MD = 'Use the `db_query` tool.\n\n| Game | Points |\n|---|---|\n| 1 | 13 |\n| 2 | 12 |\n'

let esInstances
class MockEventSource {
  constructor(url) { this.url = url; this.onmessage = null; this.onerror = null; esInstances.push(this) }
  close() {}
  emit(evt) { this.onmessage?.({ data: JSON.stringify(evt) }) }
}
const jsonOk = (b) => Promise.resolve({ ok: true, json: async () => b })

beforeEach(() => {
  esInstances = []
  globalThis.EventSource = MockEventSource
  globalThis.fetch = vi.fn((url, opts = {}) => {
    if (url.endsWith('/memories')) return jsonOk([])
    if (url.endsWith('/tasks') && opts.method === 'POST') return jsonOk({ id: TASK_ID })
    if (url.endsWith(`/tasks/${TASK_ID}/start`)) return jsonOk({})
    if (url.endsWith(`/tasks/${TASK_ID}/stream-ticket`)) return jsonOk({ ticket: 'tk' })
    if (url.includes(`/tasks/${TASK_ID}?`)) return jsonOk({ finalResult: FINAL_MD })
    return jsonOk({})
  })
  setApiKey('test-key')
})
afterEach(() => { clearApiKey(); vi.restoreAllMocks() })

async function runToCompletion() {
  fireEvent.change(screen.getByPlaceholderText(/Research LangGraph/i), { target: { value: 'T' } })
  fireEvent.change(screen.getByPlaceholderText(/Describe what you need/i), { target: { value: 'P' } })
  await act(async () => { fireEvent.click(screen.getByText('Run Agents')) })
  await waitFor(() => expect(esInstances).toHaveLength(1))
  const es = esInstances[0]
  await act(async () => {
    es.emit({ event: 'task_started', payload: {}, timestamp: T(0) })
    es.emit({ event: 'agent_started', payload: { agentExecutionId: 'a', agentType: 'Data' }, timestamp: T(1) })
    es.emit({ event: 'task_completed', payload: { totalCostUsd: 0.01 }, timestamp: T(2) })
  })
  await waitFor(() => expect(screen.getByText(/View full trace in Observability/i)).toBeInTheDocument())
}

describe('M6.2 — Playground final-result markdown (P4/P5)', () => {
  it('renders inline backticks as <code>, never <pre> (react-markdown v10 fix)', async () => {
    render(<App />)
    await runToCompletion()
    fireEvent.click(screen.getByRole('radio', { name: 'List' }))
    const codeEl = await screen.findByText('db_query', { selector: 'code' })
    expect(codeEl.tagName).toBe('CODE')
    // The inline code must NOT be wrapped in a <pre> (the old inline-prop bug).
    expect(codeEl.closest('pre')).toBeNull()
  })

  it('wraps GFM tables in an overflow-x container so the page never overflows', async () => {
    render(<App />)
    await runToCompletion()
    fireEvent.click(screen.getByRole('radio', { name: 'List' }))
    const table = await screen.findByRole('table')
    const wrapper = table.parentElement
    expect(wrapper.style.overflowX).toBe('auto')
  })

  it('offsets model-output headings so no second <h1> competes with the page h1', async () => {
    render(<App />)
    await runToCompletion()
    // finalResult has no markdown heading here; assert the renderer emits no <h1>
    // inside the result region (the offset maps markdown h1 -> <h2>).
    fireEvent.click(screen.getByRole('radio', { name: 'List' }))
    await screen.findByRole('table')
    const resultRegion = screen.getByText(/Game/).closest('div')
    expect(within(resultRegion).queryByRole('heading', { level: 1 })).toBeNull()
  })
})
