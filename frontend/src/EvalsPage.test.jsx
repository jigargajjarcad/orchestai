import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { SuitesView, RunView, PostHocView } from './EvalsPage'
import { dataFontFamily } from './theme/tokens'

// authenticatedFetch is the single network boundary — mock it at the module
// edge. No production code is restructured to enable this.
vi.mock('./apiKey', () => ({ authenticatedFetch: vi.fn() }))
import { authenticatedFetch } from './apiKey'

function deferred() {
  let resolve, reject
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}
const okJson = (body) => ({ ok: true, json: async () => body })
const errJson = (status, body) => ({ ok: false, status, json: async () => body })

beforeEach(() => { vi.clearAllMocks() })

describe('SuitesView (T2: error vs empty)', () => {
  it('shows a Spinner while suites are loading (null)', () => {
    render(<SuitesView suites={null} error={null} onSelect={() => {}} />)
    expect(screen.getByText('Loading suites…')).toBeInTheDocument()
  })

  it('shows an EmptyState when the request succeeds with zero suites', () => {
    render(<SuitesView suites={[]} error={null} onSelect={() => {}} />)
    expect(screen.getByText('No eval suites yet')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows an ErrorBanner (distinct from empty) when the request failed', () => {
    render(<SuitesView suites={null} error="Failed: 500" onSelect={() => {}} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Failed: 500')
    expect(screen.queryByText('No eval suites yet')).not.toBeInTheDocument()
    expect(screen.queryByText('Loading suites…')).not.toBeInTheDocument()
  })

  it('renders the suite list when suites are present', () => {
    render(<SuitesView suites={[{ id: 's1', name: 'My Suite', targetAgentType: 'Research', description: 'd' }]} error={null} onSelect={() => {}} />)
    expect(screen.getByText('My Suite')).toBeInTheDocument()
  })
})

describe('RunView (T3: runs loading / empty / error)', () => {
  const suites = [{ id: 's1', name: 'My Suite' }]

  it('shows a Spinner while the runs fetch is in flight', async () => {
    const d = deferred()
    authenticatedFetch.mockReturnValueOnce(d.promise)
    render(<RunView suites={suites} selectedSuiteId="s1" onRunTriggered={() => {}} />)
    expect(screen.getByText('Loading runs…')).toBeInTheDocument()
    d.resolve(okJson({ runs: [] }))
    await screen.findByText(/no prior runs for this suite/)
  })

  it('shows an ErrorBanner when the runs fetch fails', async () => {
    authenticatedFetch.mockResolvedValueOnce(errJson(500, { title: 'Runs boom' }))
    render(<RunView suites={suites} selectedSuiteId="s1" onRunTriggered={() => {}} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Runs boom')
  })

  it('distinguishes a legitimate empty result from an error', async () => {
    authenticatedFetch.mockResolvedValueOnce(okJson({ runs: [] }))
    render(<RunView suites={suites} selectedSuiteId="s1" onRunTriggered={() => {}} />)
    expect(await screen.findByText(/no prior runs for this suite/)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('renders run options when the fetch returns runs', async () => {
    authenticatedFetch.mockResolvedValueOnce(okJson({
      runs: [{ id: 'r1', subjectVersion: 'v1', status: 'Completed', triggeredAt: '2026-07-01T00:00:00Z' }],
    }))
    render(<RunView suites={suites} selectedSuiteId="s1" onRunTriggered={() => {}} />)
    expect(await screen.findByText(/v1 — Completed/)).toBeInTheDocument()
  })
})

describe('PostHocView (T4: summary loading + runId data-face)', () => {
  async function submitToGetRunPanel() {
    authenticatedFetch.mockResolvedValueOnce(okJson({ evalRunId: 'abcd1234efgh' }))
    render(<PostHocView />)
    fireEvent.change(screen.getByPlaceholderText(/Was the tool call appropriate/), { target: { value: 'Judge quality' } })
    const [from, to] = document.querySelectorAll('input[type="datetime-local"]')
    fireEvent.change(from, { target: { value: '2026-07-01T00:00' } })
    fireEvent.change(to, { target: { value: '2026-07-02T00:00' } })
    fireEvent.click(screen.getByText('Submit post-hoc scoring request'))
    return screen.findByText('abcd1234…')
  }

  it('renders the run id on the data face (JetBrains Mono), not the UI face', async () => {
    const idEl = await submitToGetRunPanel()
    expect(idEl).toBeInTheDocument()
    // The id sits on the data face (JetBrains Mono), not the UI face (IBM Plex Sans).
    // Assert intent, not exact string — the DOM normalizes quote style.
    expect(idEl.style.fontFamily).toContain('JetBrains Mono')
    expect(idEl.style.fontFamily).not.toContain('IBM Plex Sans')
    expect(dataFontFamily).toContain('JetBrains Mono')
  })

  it('shows a Spinner while the summary refresh is in flight, then the summary', async () => {
    await submitToGetRunPanel()
    const d = deferred()
    authenticatedFetch.mockReturnValueOnce(d.promise)
    fireEvent.click(screen.getByText('Refresh summary'))
    expect(screen.getByText('Loading summary…')).toBeInTheDocument()
    d.resolve(okJson({
      status: 'Completed', scoredCount: 4, skippedAlreadyScoredCount: 0,
      passRate: 0.75, passedCount: 3, scoreDistribution: [{ range: '0.7-0.8', count: 4 }],
    }))
    expect(await screen.findByText(/pass rate/)).toBeInTheDocument()
    expect(screen.queryByText('Loading summary…')).not.toBeInTheDocument()
  })
})
