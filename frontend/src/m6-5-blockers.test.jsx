import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { setApiKey, clearApiKey } from './apiKey'

// P2-M6.5 blocker coverage. Two release blockers, verified against the current
// code before these were written:
//
//   H1 — a dropped SSE stream leaves the run hung forever. `es.onerror` only
//        closes the socket; no status, no error, no announcement, no retry.
//   H2 — Observability's fetch effects have no cancellation, so a slow response
//        for a previously-selected task overwrites the freshly-selected one.
//
// Every test here was negative-controlled: each fails against the pre-fix
// implementation. See the milestone report for the recorded failure output.

vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }) => <div>{children}</div>,
  ReactFlow: () => <div data-testid="rf" />,
  Background: () => null,
  Controls: () => null,
  BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => ({ fitView: () => {} }),
}))

const { default: App } = await import('./App')
const { default: ObservabilityPage } = await import('./ObservabilityPage')
const { default: EvalsPage } = await import('./EvalsPage')

const TASK_ID = '99999999-2222-3333-4444-555555555555'
const T = (s) => `2026-08-01T09:00:${String(s).padStart(2, '0')}.000Z`
const jsonOk = (b) => Promise.resolve({ ok: true, json: async () => b })
function deferred() {
  let resolve
  const promise = new Promise(res => { resolve = res })
  return { promise, resolve }
}

let esInstances
class MockEventSource {
  constructor(url) {
    this.url = url
    this.onmessage = null
    this.onerror = null
    this.closed = false
    esInstances.push(this)
  }
  close() { this.closed = true }
  emit(evt) { this.onmessage?.({ data: JSON.stringify(evt) }) }
  drop() { this.onerror?.(new Event('error')) }   // what the browser does on a dropped connection
}

beforeEach(() => {
  window.history.pushState(null, '', '/')
  esInstances = []
  globalThis.EventSource = MockEventSource
  setApiKey('test-key')
})
afterEach(() => { clearApiKey(); vi.restoreAllMocks() })

// ─────────────────────────────── H1 ───────────────────────────────

describe('H1 — a dropped SSE stream must not hang the run', () => {
  beforeEach(() => {
    globalThis.fetch = vi.fn((url, opts = {}) => {
      if (url.endsWith('/memories')) return jsonOk([])
      if (url.endsWith('/tasks') && opts.method === 'POST') return jsonOk({ id: TASK_ID })
      if (url.endsWith(`/tasks/${TASK_ID}/start`)) return jsonOk({})
      if (url.endsWith(`/tasks/${TASK_ID}/stream-ticket`)) return jsonOk({ ticket: 'tk' })
      if (url.includes(`/tasks/${TASK_ID}?`)) return jsonOk({ finalResult: 'done' })
      return jsonOk({})
    })
  })

  async function startRun() {
    render(<App />)
    fireEvent.change(screen.getByPlaceholderText(/Research LangGraph/i), { target: { value: 'T' } })
    fireEvent.change(screen.getByPlaceholderText(/Describe what you need/i), { target: { value: 'P' } })
    await act(async () => { fireEvent.click(screen.getByText('Run Agents')) })
    await waitFor(() => expect(esInstances).toHaveLength(1))
    await act(async () => {
      esInstances[0].emit({ event: 'task_started', payload: {}, timestamp: T(0) })
    })
    return esInstances[0]
  }

  it('stops showing the run as in-flight when the stream drops', async () => {
    const es = await startRun()
    // The results pane defaults to Graph; the in-flight spinner lives in List
    // view, so switch there to observe the "still running" state directly.
    await act(async () => { fireEvent.click(screen.getByRole('radio', { name: 'List' })) })
    expect(screen.getByText('Waiting for agents to complete…')).toBeInTheDocument()

    await act(async () => { es.drop() })

    // The perpetual spinner is the visible symptom of the hang.
    await waitFor(() => {
      expect(screen.queryByText('Waiting for agents to complete…')).not.toBeInTheDocument()
    })
  })

  it('surfaces the disconnection as an error the user can see', async () => {
    const es = await startRun()
    await act(async () => { es.drop() })
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/disconnect|connection|interrupt/i)
  })

  it('announces the disconnection to assistive tech (a11y preserved)', async () => {
    const es = await startRun()
    await act(async () => { es.drop() })
    await waitFor(() => {
      expect(screen.getByTestId('playground-live-status')).toHaveTextContent(/disconnect|connection|interrupt/i)
    })
  })

  it('offers a retry that re-opens the stream with a fresh ticket', async () => {
    const es = await startRun()
    await act(async () => { es.drop() })

    const retry = await screen.findByRole('button', { name: /reconnect|retry/i })
    const ticketCallsBefore = globalThis.fetch.mock.calls.filter(c => String(c[0]).includes('stream-ticket')).length

    await act(async () => { fireEvent.click(retry) })

    // A NEW EventSource, and a NEW ticket — the old one is single-use and spent
    // (see apiKey.js), which is why plain native reconnection cannot work here.
    await waitFor(() => expect(esInstances.length).toBe(2))
    const ticketCallsAfter = globalThis.fetch.mock.calls.filter(c => String(c[0]).includes('stream-ticket')).length
    expect(ticketCallsAfter).toBe(ticketCallsBefore + 1)
  })

  it('resumes streaming after a successful reconnect', async () => {
    const es = await startRun()
    await act(async () => { es.drop() })
    await act(async () => { fireEvent.click(await screen.findByRole('button', { name: /reconnect|retry/i })) })
    await waitFor(() => expect(esInstances.length).toBe(2))

    await act(async () => {
      esInstances[1].emit({ event: 'task_completed', payload: { totalCostUsd: 0.02 }, timestamp: T(5) })
    })
    await waitFor(() => {
      expect(screen.getByTestId('playground-live-status')).toHaveTextContent(/Task completed/i)
    })
    expect(screen.queryByRole('button', { name: /reconnect|retry/i })).toBeNull()
  })

  it('does not report a disconnection when the stream ends after completion', async () => {
    const es = await startRun()
    await act(async () => {
      es.emit({ event: 'task_completed', payload: { totalCostUsd: 0.01 }, timestamp: T(3) })
    })
    // Servers commonly close the connection right after the terminal event; the
    // browser may surface that as an error. It must not be shown as a failure.
    await act(async () => { es.drop() })

    expect(screen.queryByRole('button', { name: /reconnect|retry/i })).toBeNull()
    expect(screen.getByTestId('playground-live-status')).toHaveTextContent(/Task completed/i)
  })
})

// ─────────────────────────────── H2 ───────────────────────────────

describe('H2 — Observability must not let a stale response win', () => {
  const TASKS = [
    { id: 'task-a', title: 'Task A', status: 'Completed', createdAt: '2026-07-01T00:00:00Z' },
    { id: 'task-b', title: 'Task B', status: 'Completed', createdAt: '2026-07-02T00:00:00Z' },
  ]
  const summaryFor = (marker) => ({
    status: 'Completed', durationSeconds: marker, totalCostUsd: 0.5,
    totalInputTokens: 1, totalOutputTokens: 2, toolCallCount: 3, retryCount: 0,
    errorCount: 0, memoryUsed: false, checkpointRestored: false,
    agentsInvolved: ['Research'], providersAndModels: ['anthropic/claude'],
  })

  it('keeps the newest selection when an older request resolves last', async () => {
    const slowA = deferred()
    globalThis.fetch = vi.fn((url) => {
      if (url.includes('/tasks?limit')) return jsonOk(TASKS)
      if (url.includes('/tasks/task-a/summary')) return slowA.promise
      if (url.includes('/tasks/task-b/summary')) return jsonOk(summaryFor(22.2))
      return jsonOk({})
    })

    render(<ObservabilityPage />)
    await act(async () => { fireEvent.click(await screen.findByRole('button', { name: 'Summary' })) })
    const picker = await screen.findByRole('combobox', { name: 'Task' })
    // The picker is populated by its own fetch — wait for it, or the selects
    // below are no-ops against an empty option list.
    await screen.findByRole('option', { name: /Task A/ })

    // Select A — its request hangs.
    await act(async () => { fireEvent.change(picker, { target: { value: 'task-a' } }) })
    expect(picker.value).toBe('task-a')
    expect(screen.getByText('Loading summary…')).toBeInTheDocument()

    // Switch to B before A returns. B answers immediately.
    await act(async () => { fireEvent.change(picker, { target: { value: 'task-b' } }) })
    expect(picker.value).toBe('task-b')
    await waitFor(() => expect(screen.getByText('22.2s')).toBeInTheDocument())

    // A finally answers — with the user now looking at B.
    await act(async () => { slowA.resolve({ ok: true, json: async () => summaryFor(99.9) }) })

    // The stale answer must be discarded, not painted over the current view.
    await waitFor(() => expect(screen.getByText('22.2s')).toBeInTheDocument())
    expect(screen.queryByText('99.9s')).toBeNull()
  })

  it('discards a superseded request that FAILS, instead of showing its error', async () => {
    // The success path is covered above; this covers the .catch branch, which
    // is the nastier half — a stale rejection would otherwise put an error
    // banner over a view that loaded perfectly well.
    const slowA = deferred()
    globalThis.fetch = vi.fn((url) => {
      if (url.includes('/tasks?limit')) return jsonOk(TASKS)
      if (url.includes('/tasks/task-a/summary')) return slowA.promise
      if (url.includes('/tasks/task-b/summary')) return jsonOk(summaryFor(22.2))
      return jsonOk({})
    })

    render(<ObservabilityPage />)
    await act(async () => { fireEvent.click(await screen.findByRole('button', { name: 'Summary' })) })
    const picker = await screen.findByRole('combobox', { name: 'Task' })
    await screen.findByRole('option', { name: /Task A/ })

    await act(async () => { fireEvent.change(picker, { target: { value: 'task-a' } }) })
    await act(async () => { fireEvent.change(picker, { target: { value: 'task-b' } }) })
    await waitFor(() => expect(screen.getByText('22.2s')).toBeInTheDocument())

    // A's request now fails, long after the user moved on.
    await act(async () => { slowA.resolve({ ok: false, status: 500, json: async () => ({}) }) })

    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByText('22.2s')).toBeInTheDocument()
  })

  it('keeps the newest date range on the Cost Dashboard', async () => {
    const slowFirst = deferred()
    let dashboardCalls = 0
    globalThis.fetch = vi.fn((url) => {
      if (url.includes('/tasks?limit')) return jsonOk(TASKS)
      if (url.includes('/cost-dashboard')) {
        dashboardCalls += 1
        if (dashboardCalls === 1) return slowFirst.promise
        return jsonOk({ totalCostUsd: 2.22, totalExecutions: 22, breakdown: [] })
      }
      return jsonOk({})
    })

    render(<ObservabilityPage />)
    await act(async () => { fireEvent.click(await screen.findByRole('button', { name: 'Cost Dashboard' })) })

    // Change the range, which re-fires the effect while the first is in flight.
    const from = document.querySelector('input[type="date"]')
    await act(async () => { fireEvent.change(from, { target: { value: '2026-06-01' } }) })
    await waitFor(() => expect(screen.getByText('$2.220000')).toBeInTheDocument())

    await act(async () => {
      slowFirst.resolve({ ok: true, json: async () => ({ totalCostUsd: 9.99, totalExecutions: 99, breakdown: [] }) })
    })

    await waitFor(() => expect(screen.getByText('$2.220000')).toBeInTheDocument())
    expect(screen.queryByText('$9.990000')).toBeNull()
  })
})

// ────────────────────── H2b (M6.5.1) — EvalsPage ──────────────────────
// The same stale-response race, on the page whose ONE guarded effect was the
// reference used to fix Observability. Independent verification found the other
// three unguarded: ResultsView's runs fetch, ResultsView's results+regression
// pair, and the page-level suites fetch.
//
// RunView's runs effect (EvalsPage.jsx:146) was verified ALREADY SAFE and is
// deliberately not covered here — nothing changed about it.

describe('H2b — Evals must not let a stale response win', () => {
  const SUITES = [
    { id: 'suite-1', name: 'Suite One', targetAgentType: 'Research', description: 'first' },
    { id: 'suite-2', name: 'Suite Two', targetAgentType: 'Code', description: 'second' },
  ]
  const resultsWith = (passCount) => ({
    results: Array.from({ length: 4 }, (_, i) => ({
      evalCaseId: `case-${i}`, scorerType: 'ExpectedSubstring',
      score: '0.9', passed: i < passCount,
    })),
  })

  async function openResultsForSuite(fetchImpl) {
    globalThis.fetch = vi.fn(fetchImpl)
    render(<EvalsPage />)
    // Pick a suite, then move to the Results tab. findBy* must be awaited
    // OUTSIDE act(): its poller cannot observe updates from inside the batch.
    const suiteRow = await screen.findByRole('button', { name: /Suite One/ })
    await act(async () => { fireEvent.click(suiteRow) })
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Results' })) })
    return screen.findByRole('combobox', { name: 'Run' })
  }

  it('ignores a stale results response when the run selection moved on (L258)', async () => {
    const slowRunA = deferred()
    await openResultsForSuite((url) => {
      const u = String(url)
      if (u.endsWith('/eval-suites')) return jsonOk({ suites: SUITES })
      if (u.includes('/eval-suites/suite-1/runs')) return jsonOk({
        runs: [
          { id: 'run-a', subjectVersion: 'va', status: 'Completed', triggeredAt: '2026-07-30T09:00:00Z' },
          { id: 'run-b', subjectVersion: 'vb', status: 'Completed', triggeredAt: '2026-07-30T10:00:00Z' },
        ],
      })
      if (u.includes('/eval-runs/run-a/results')) return slowRunA.promise
      if (u.includes('/eval-runs/run-b/results')) return jsonOk(resultsWith(3))
      return jsonOk({ caseDiffs: [], currentPassRate: 0, baselinePassRate: 0, passRateDelta: 0 })
    })

    const picker = await screen.findByRole('combobox', { name: 'Run' })
    await screen.findByRole('option', { name: /va/ })

    await act(async () => { fireEvent.change(picker, { target: { value: 'run-a' } }) })
    await act(async () => { fireEvent.change(picker, { target: { value: 'run-b' } }) })
    await waitFor(() => expect(screen.getByText('3 / 4')).toBeInTheDocument())

    // run-a answers late, with the user now looking at run-b.
    await act(async () => { slowRunA.resolve({ ok: true, json: async () => resultsWith(1) }) })

    await waitFor(() => expect(screen.getByText('3 / 4')).toBeInTheDocument())
    expect(screen.queryByText('1 / 4')).toBeNull()
  })

  it('ignores a stale results FAILURE instead of erroring over a good view (L258)', async () => {
    const slowRunA = deferred()
    await openResultsForSuite((url) => {
      const u = String(url)
      if (u.endsWith('/eval-suites')) return jsonOk({ suites: SUITES })
      if (u.includes('/eval-suites/suite-1/runs')) return jsonOk({
        runs: [
          { id: 'run-a', subjectVersion: 'va', status: 'Completed', triggeredAt: '2026-07-30T09:00:00Z' },
          { id: 'run-b', subjectVersion: 'vb', status: 'Completed', triggeredAt: '2026-07-30T10:00:00Z' },
        ],
      })
      if (u.includes('/eval-runs/run-a/results')) return slowRunA.promise
      if (u.includes('/eval-runs/run-b/results')) return jsonOk(resultsWith(2))
      return jsonOk({ caseDiffs: [], currentPassRate: 0, baselinePassRate: 0, passRateDelta: 0 })
    })

    const picker = await screen.findByRole('combobox', { name: 'Run' })
    await screen.findByRole('option', { name: /va/ })
    await act(async () => { fireEvent.change(picker, { target: { value: 'run-a' } }) })
    await act(async () => { fireEvent.change(picker, { target: { value: 'run-b' } }) })
    await waitFor(() => expect(screen.getByText('2 / 4')).toBeInTheDocument())

    await act(async () => {
      slowRunA.resolve({ ok: false, status: 500, json: async () => ({ title: 'run-a blew up' }) })
    })

    expect(screen.queryByText(/run-a blew up/)).toBeNull()
    expect(screen.getByText('2 / 4')).toBeInTheDocument()
  })

  // NO race test for L250 (ResultsView's runs fetch) or L584 (the page-level
  // suites fetch), deliberately. Verified by experiment, not assumption: a test
  // driving a suite switch PASSES against the unguarded code, because changing
  // `selectedSuiteId` means leaving the Results tab, which UNMOUNTS ResultsView.
  // The late response then writes to a dead component instead of overwriting a
  // live one. L584 has empty deps and runs once, so it cannot race itself.
  // Both are still guarded — for unmount-safety and so a future refactor that
  // keeps the view mounted cannot silently reintroduce the race — but claiming
  // a stale-overwrite test for them would be inventing a failure that does not
  // occur. The same reasoning applied to Observability's page-level tasks fetch.
})
