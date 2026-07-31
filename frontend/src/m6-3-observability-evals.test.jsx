import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { a11yAxe as axe } from './test/axe'
import ObservabilityPage from './ObservabilityPage'
import { SuitesView, RunView, PostHocView } from './EvalsPage'

// P2-M6.3 — deterministic a11y coverage for the Observability (editable
// sub-views only) and Evals surfaces. These lock in what the M6.3 commits
// fixed so a later edit can't silently regress it:
//   · every select / date input has an accessible name          (O3)
//   · the view switchers are named groups of real buttons       (O5, A2)
//   · the cost BarChart has a text alternative                  (O1)
//   · data tables carry scoped headers + a caption              (O6)
//   · eval suite rows are keyboard-operable buttons             (O4)
//   · async load state is announced                             (O7)
//
// The Timeline sub-view is NOT exercised here: SpanRow + the TimelineView graph
// render block are frozen for this milestone, so asserting on them would invite
// a change to them. Every assertion below targets editable regions only.
//
// axe cannot evaluate color-contrast in jsdom (disabled in ./test/axe) and
// cannot evaluate responsive/overflow behavior at all — those are covered by
// the Playwright + manual layer, not here.

vi.mock('./apiKey', () => ({ authenticatedFetch: vi.fn() }))
import { authenticatedFetch } from './apiKey'

const okJson = (body) => ({ ok: true, json: async () => body })

const TASKS = [
  { id: 't1', title: 'Analyze roster', status: 'Completed', createdAt: '2026-07-01T00:00:00Z' },
  { id: 't2', title: 'Draft report', status: 'Completed', createdAt: '2026-07-02T00:00:00Z' },
]

const COST_DASHBOARD = {
  totalCostUsd: 1.25,
  totalExecutions: 12,
  breakdown: [
    { date: '2026-07-01', agentType: 'Research', model: 'claude-sonnet-4-6', inputTokens: 100, outputTokens: 50, costUsd: 0.5, executionCount: 4, isLive: false },
    { date: '2026-07-02', agentType: 'Code', model: 'claude-sonnet-4-6', inputTokens: 200, outputTokens: 90, costUsd: 0.75, executionCount: 8, isLive: true },
  ],
}

const ERROR_RATES = {
  agentErrorRates: [
    { agentType: 'Research', totalExecutions: 10, failedExecutions: 2, failureRate: 0.2, retryCount: 1, failuresByCategory: { Timeout: 2 } },
  ],
  toolErrorRates: [
    { toolName: 'web_search', totalCalls: 6, failedCalls: 0, failureRate: 0, failuresByCategory: {} },
  ],
}

// Route by URL so sub-view switching works regardless of call order.
function routeFetch() {
  authenticatedFetch.mockImplementation((url) => {
    if (url.includes('/tasks?')) return Promise.resolve(okJson(TASKS))
    if (url.includes('/cost-dashboard')) return Promise.resolve(okJson(COST_DASHBOARD))
    if (url.includes('/error-rates')) return Promise.resolve(okJson(ERROR_RATES))
    return Promise.resolve(okJson({}))
  })
}

async function gotoSubView(user, name) {
  await user.click(screen.getByRole('button', { name }))
}

beforeEach(() => { vi.clearAllMocks(); routeFetch() })

describe('M6.3 — Observability view switcher (O5)', () => {
  it('exposes the sub-nav as a named group of real buttons', async () => {
    render(<ObservabilityPage />)
    const group = await screen.findByRole('group', { name: 'Observability views' })
    // Real <button>s, not divs/links — keyboard-operable by construction.
    expect(within(group).getAllByRole('button')).toHaveLength(5)
    expect(within(group).getByRole('button', { name: 'Cost Dashboard' })).toBeInTheDocument()
  })

  it('marks the active view with aria-current', async () => {
    const user = userEvent.setup()
    render(<ObservabilityPage />)
    const group = await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, 'Cost Dashboard')
    expect(within(group).getByRole('button', { name: 'Cost Dashboard' })).toHaveAttribute('aria-current')
    expect(within(group).getByRole('button', { name: 'Summary' })).not.toHaveAttribute('aria-current')
  })
})

describe('M6.3 — Observability control names (O3)', () => {
  it('gives the Compare task pickers accessible names', async () => {
    const user = userEvent.setup()
    render(<ObservabilityPage />)
    await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, 'Compare')
    expect(screen.getByRole('combobox', { name: 'First Task' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Second Task' })).toBeInTheDocument()
  })

  it('gives the date-range inputs accessible names', async () => {
    const user = userEvent.setup()
    const { container } = render(<ObservabilityPage />)
    await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, 'Cost Dashboard')
    await screen.findByText('Cost Over Time')

    // type=date has no implicit ARIA role in jsdom, so resolve by label association.
    const [from, to] = container.querySelectorAll('input[type="date"]')
    expect(document.querySelector(`label[for="${from.id}"]`)).toHaveTextContent('From')
    expect(document.querySelector(`label[for="${to.id}"]`)).toHaveTextContent('To')
  })

  it('leaves no select without a visible focus indicator (no inline outline:none)', async () => {
    const user = userEvent.setup()
    render(<ObservabilityPage />)
    await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, 'Compare')
    for (const select of screen.getAllByRole('combobox')) {
      expect(select.style.outline).not.toBe('none')
    }
  })
})

describe('M6.3 — Cost Dashboard chart + table semantics (O1/O6)', () => {
  it('exposes the BarChart as a labeled image with a data-table alternative', async () => {
    const user = userEvent.setup()
    render(<ObservabilityPage />)
    await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, 'Cost Dashboard')

    const chart = await screen.findByRole('img', { name: /Cost over time/ })
    expect(chart).toHaveAccessibleName(/2 bars from 2026-07-01 to 2026-07-02/)
    expect(chart).toHaveAccessibleName(/Peak \$0\.750000 on 2026-07-02/)

    // The off-screen table is a SIBLING of the role=img node: children of a
    // role=img are presentational and would be dropped from the a11y tree.
    const dataTable = screen.getByRole('table', { name: 'Cost over time — full data' })
    expect(chart.contains(dataTable)).toBe(false)
    expect(within(dataTable).getByRole('columnheader', { name: 'Cost' })).toBeInTheDocument()
    expect(within(dataTable).getByRole('cell', { name: '$0.750000' })).toBeInTheDocument()
  })

  it('scopes the breakdown table headers and names the action column', async () => {
    const user = userEvent.setup()
    render(<ObservabilityPage />)
    await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, 'Cost Dashboard')

    const table = await screen.findByRole('table', { name: /Cost and token breakdown/ })
    for (const header of within(table).getAllByRole('columnheader')) {
      expect(header).toHaveAttribute('scope', 'col')
    }
    // The formerly-empty trailing header now carries an off-screen name.
    expect(within(table).getByRole('columnheader', { name: 'Live status' })).toBeInTheDocument()
  })

  it('captions the error-rate tables', async () => {
    const user = userEvent.setup()
    render(<ObservabilityPage />)
    await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, 'Error Rates')
    expect(await screen.findByRole('table', { name: 'Failure rates by agent' })).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'Failure rates by tool' })).toBeInTheDocument()
  })
})

describe('M6.3 — Observability axe (editable sub-views)', () => {
  it.each([
    ['Summary', 'Summary'],
    ['Cost Dashboard', 'Cost Dashboard'],
    ['Error Rates', 'Error Rates'],
    ['Compare', 'Compare'],
  ])('reports zero violations on the %s sub-view', async (_name, tab) => {
    const user = userEvent.setup()
    const { container } = render(<ObservabilityPage />)
    await screen.findByRole('group', { name: 'Observability views' })
    await gotoSubView(user, tab)
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('M6.3 — Evals suite rows are keyboard-operable (O4)', () => {
  const SUITES = [
    { id: 's1', name: 'Roster Suite', targetAgentType: 'Research', description: 'roster checks' },
    { id: 's2', name: 'Report Suite', targetAgentType: 'Code', description: 'report checks' },
  ]

  it('renders each row as a button reachable by Tab', async () => {
    const user = userEvent.setup()
    render(<SuitesView suites={SUITES} error={null} onSelect={() => {}} />)
    const row = screen.getByRole('button', { name: /Roster Suite/ })
    await user.tab()
    expect(row).toHaveFocus()
  })

  it('activates a row with Enter and with Space', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    render(<SuitesView suites={SUITES} error={null} onSelect={onSelect} />)

    screen.getByRole('button', { name: /Roster Suite/ }).focus()
    await user.keyboard('{Enter}')
    expect(onSelect).toHaveBeenCalledWith('s1')

    screen.getByRole('button', { name: /Report Suite/ }).focus()
    await user.keyboard(' ')
    expect(onSelect).toHaveBeenLastCalledWith('s2')
  })

  it('marks the selected row with aria-current', () => {
    render(<SuitesView suites={SUITES} error={null} selectedSuiteId="s2" onSelect={() => {}} />)
    expect(screen.getByRole('button', { name: /Report Suite/ })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('button', { name: /Roster Suite/ })).not.toHaveAttribute('aria-current')
  })

  it('announces the settled suite count in a live region (O7)', () => {
    const { rerender } = render(<SuitesView suites={SUITES} error={null} onSelect={() => {}} />)
    expect(screen.getByRole('status')).toHaveTextContent('2 eval suites loaded.')

    rerender(<SuitesView suites={[SUITES[0]]} error={null} onSelect={() => {}} />)
    expect(screen.getByRole('status')).toHaveTextContent('1 eval suite loaded.')
  })

  // A10: one announcement per transition. The Spinner (role=status) owns the
  // loading state and ErrorBanner (role=alert) owns the failure state, so the
  // M6.3 region must stay silent for both or AT reads each transition twice.
  it('does not double-announce the loading or error states', () => {
    const { rerender } = render(<SuitesView suites={null} error={null} onSelect={() => {}} />)
    const spinnerStatus = screen.getByText('Loading suites…')
    const regions = screen.getAllByRole('status')
    expect(regions.filter(r => r.textContent.trim() !== '')).toEqual([
      regions.find(r => r.contains(spinnerStatus)),
    ])

    rerender(<SuitesView suites={null} error="Failed: 500" onSelect={() => {}} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Failed: 500')
    for (const region of screen.queryAllByRole('status')) {
      expect(region).toHaveTextContent('')
    }
  })
})

describe('M6.3 — Evals control names + axe (O3)', () => {
  const suites = [{ id: 's1', name: 'Roster Suite' }]

  it('names the Run view controls', async () => {
    authenticatedFetch.mockResolvedValueOnce(okJson({ runs: [] }))
    render(<RunView suites={suites} selectedSuiteId="s1" onRunTriggered={() => {}} />)
    expect(screen.getByRole('textbox', { name: 'Subject version' })).toBeInTheDocument()
    expect(await screen.findByRole('combobox', { name: 'Baseline run' })).toBeInTheDocument()
  })

  it('names the Post-Hoc view controls', () => {
    const { container } = render(<PostHocView />)
    expect(screen.getByRole('combobox', { name: 'Agent type' })).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Max traces' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Rubric' })).toBeInTheDocument()
    const [from, to] = container.querySelectorAll('input[type="datetime-local"]')
    expect(from).toHaveAccessibleName('From date')
    expect(to).toHaveAccessibleName('To date')
  })

  it('reports zero violations on the Suites view', async () => {
    const { container } = render(
      <SuitesView suites={[{ id: 's1', name: 'Roster Suite', targetAgentType: 'Research', description: 'd' }]} error={null} onSelect={() => {}} />
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('reports zero violations on the Run view', async () => {
    authenticatedFetch.mockResolvedValueOnce(okJson({ runs: [] }))
    const { container } = render(<RunView suites={suites} selectedSuiteId="s1" onRunTriggered={() => {}} />)
    await screen.findByRole('combobox', { name: 'Baseline run' })
    expect(await axe(container)).toHaveNoViolations()
  })

  it('reports zero violations on the Post-Hoc view', async () => {
    const { container } = render(<PostHocView />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
