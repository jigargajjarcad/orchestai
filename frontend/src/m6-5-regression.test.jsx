import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { a11yAxe as axe } from './test/axe'
import { setApiKey, clearApiKey } from './apiKey'

// P2-M6.5 — release-candidate regression suite. Two jobs the per-milestone
// suites do not do:
//   1. axe across EVERY surface in one place, including the app shell and the
//      Playground (which M6.2 asserted functionally but never ran axe over).
//   2. the keyboard paths a user actually needs to get around the app at all —
//      skip link, primary nav — as opposed to per-widget behaviour.
//
// Structural only. axe cannot see colour contrast or layout in jsdom; those are
// covered by the Playwright + visual layer.

vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }) => <div>{children}</div>,
  ReactFlow: () => <div data-testid="rf" />,
  Background: () => null,
  Controls: () => null,
  BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => ({ fitView: () => {} }),
}))

const { default: App } = await import('./App')

const TASK_ID = '99999999-2222-3333-4444-555555555555'
const T = (s) => `2026-08-01T09:00:${String(s).padStart(2, '0')}.000Z`
const jsonOk = (b) => Promise.resolve({ ok: true, json: async () => b })

const MEMORIES = [
  { id: 'm1', agentType: 'Research', key: 'roster', value: 'v', importance: 3, updatedAt: '2026-07-30T10:00:00Z' },
]

let esInstances
class MockEventSource {
  constructor(url) { this.url = url; this.onmessage = null; this.onerror = null; esInstances.push(this) }
  close() {}
  emit(evt) { this.onmessage?.({ data: JSON.stringify(evt) }) }
}

beforeEach(() => {
  // useRouting derives the view from window.location.pathname and navigate()
  // pushState()s. jsdom keeps the URL for the whole file, so without this reset
  // a test that navigated leaves the NEXT render on that view — which is
  // correct product behaviour, but breaks per-test independence.
  window.history.pushState(null, '', '/')
  esInstances = []
  globalThis.EventSource = MockEventSource
  globalThis.fetch = vi.fn((url, opts = {}) => {
    if (url.endsWith('/memories')) return jsonOk(MEMORIES)
    if (url.endsWith('/tasks') && opts.method === 'POST') return jsonOk({ id: TASK_ID })
    if (url.endsWith(`/tasks/${TASK_ID}/start`)) return jsonOk({})
    if (url.endsWith(`/tasks/${TASK_ID}/stream-ticket`)) return jsonOk({ ticket: 'tk' })
    if (url.includes(`/tasks/${TASK_ID}?`)) return jsonOk({ finalResult: 'Done. Use `db_query`.' })
    if (url.includes('/eval-suites')) return jsonOk({ suites: [] })
    if (url.includes('/tasks?limit')) return jsonOk([])
    return jsonOk({})
  })
  setApiKey('test-key')
})
afterEach(() => { clearApiKey(); vi.restoreAllMocks() })

const goTo = async (name) => {
  await act(async () => { fireEvent.click(screen.getByRole('button', { name })) })
}

describe('M6.5 — axe across every surface', () => {
  it.each([
    ['Playground (idle)', null],
    ['Memories', 'Memories'],
    ['Observability', 'Observability'],
    ['Evals', 'Evals'],
  ])('reports zero violations on %s', async (_label, nav) => {
    const { container } = render(<App />)
    if (nav) await goTo(nav)
    await waitFor(() => expect(container.querySelector('main')).toBeTruthy())
    expect(await axe(container)).toHaveNoViolations()
  })

  it('reports zero violations on the Playground after a streamed run', async () => {
    const { container } = render(<App />)
    fireEvent.change(screen.getByPlaceholderText(/Research LangGraph/i), { target: { value: 'T' } })
    fireEvent.change(screen.getByPlaceholderText(/Describe what you need/i), { target: { value: 'P' } })
    await act(async () => { fireEvent.click(screen.getByText('Run Agents')) })
    await waitFor(() => expect(esInstances).toHaveLength(1))
    await act(async () => {
      esInstances[0].emit({ event: 'task_started', payload: {}, timestamp: T(0) })
      esInstances[0].emit({ event: 'agent_started', payload: { agentExecutionId: 'a', agentType: 'Research' }, timestamp: T(1) })
      esInstances[0].emit({ event: 'tool_started', payload: { agentExecutionId: 'a', toolName: 'web_search' }, timestamp: T(2) })
      esInstances[0].emit({ event: 'tool_completed', payload: { agentExecutionId: 'a', toolName: 'web_search', success: true, durationMs: 12, outputPreview: 'ok' }, timestamp: T(3) })
      esInstances[0].emit({ event: 'task_completed', payload: { totalCostUsd: 0.01 }, timestamp: T(4) })
    })
    await waitFor(() => expect(screen.getByText(/View full trace in Observability/i)).toBeInTheDocument())
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('M6.5 — keyboard access to the app itself', () => {
  it('puts the skip link first in the tab order, targeting a focusable main', async () => {
    const user = userEvent.setup()
    render(<App />)
    await user.tab()
    const skip = screen.getByRole('link', { name: /skip to main content/i })
    expect(document.activeElement).toBe(skip)

    // The two halves of the contract that make activation actually move focus:
    // the link points at #main, and main can RECEIVE focus (a skip link to a
    // non-focusable target moves the sequential-navigation point in some
    // browsers but leaves focus behind in others).
    const main = document.getElementById('main')
    expect(skip.getAttribute('href')).toBe('#main')
    expect(main).toHaveAttribute('tabindex', '-1')
    // Activation itself is asserted in the real browser: jsdom does not
    // implement fragment-navigation focus, so testing it here would only prove
    // jsdom's limitation. See the Playwright skip-link check.
  })

  it('exposes primary navigation as keyboard-operable buttons that switch view', async () => {
    const user = userEvent.setup()
    render(<App />)
    const memories = screen.getByRole('button', { name: 'Memories' })
    memories.focus()
    await act(async () => { await user.keyboard('{Enter}') })
    expect(await screen.findByRole('heading', { name: 'Agent Memories', level: 2 })).toBeInTheDocument()
  })

  it('marks the active nav item with aria-current', async () => {
    render(<App />)
    await goTo('Observability')
    expect(screen.getByRole('button', { name: 'Observability' })).toHaveAttribute('aria-current')
    expect(screen.getByRole('button', { name: 'Evals' })).not.toHaveAttribute('aria-current')
  })
})

describe('M6.5 — heading hierarchy', () => {
  it.each([
    ['Playground', null],
    ['Memories', 'Memories'],
    ['Observability', 'Observability'],
    ['Evals', 'Evals'],
  ])('keeps exactly one h1 on %s and never skips a level', async (_label, nav) => {
    render(<App />)
    if (nav) await goTo(nav)
    await waitFor(() => expect(document.querySelector('main')).toBeTruthy())

    const levels = [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map(h => Number(h.tagName[1]))
    expect(levels.filter(l => l === 1)).toHaveLength(1)
    // No jump of more than one level on the way down (h1 -> h3 is a skip).
    let prev = levels[0]
    for (const l of levels) {
      if (l > prev) expect(l - prev).toBeLessThanOrEqual(1)
      prev = l
    }
  })
})
