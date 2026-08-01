import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act, within } from '@testing-library/react'
import { a11yAxe as axe } from './test/axe'
import { setApiKey, clearApiKey } from './apiKey'

// jsdom can't measure ReactFlow — mock it (same technique as the M6.2 suite).
vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }) => <div>{children}</div>,
  ReactFlow: () => <div data-testid="rf" />,
  Background: () => null,
  Controls: () => null,
  BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => ({ fitView: () => {} }),
}))

const { default: App } = await import('./App')

const MEMORIES = [
  { id: 'm1', agentType: 'Research', key: 'roster-2026', value: 'v1', importance: 3, updatedAt: '2026-07-30T10:00:00Z' },
  { id: 'm2', agentType: 'Data', key: 'load-index', value: 'v2', importance: 5, updatedAt: '2026-07-30T11:00:00Z' },
  { id: 'm3', agentType: 'Code', key: 'schema-notes', value: 'v3', importance: 1, updatedAt: '2026-07-30T12:00:00Z' },
]

const jsonOk = (b) => Promise.resolve({ ok: true, json: async () => b })
let remaining

beforeEach(() => {
  remaining = [...MEMORIES]
  globalThis.EventSource = class { constructor() {} close() {} }
  globalThis.fetch = vi.fn((url, opts = {}) => {
    if (url.includes('/memories/') && opts.method === 'DELETE') {
      const id = url.split('/memories/')[1]
      remaining = remaining.filter(m => m.id !== id)
      return jsonOk({})
    }
    if (url.endsWith('/memories')) return jsonOk(remaining)
    return jsonOk({})
  })
  setApiKey('test-key')
})
afterEach(() => { clearApiKey(); vi.restoreAllMocks() })

async function openMemories() {
  render(<App />)
  await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Memories' })) })
  return screen.findByRole('table', { name: /Stored agent memories/ })
}

const deleteBtn = (key, agent) => screen.getByRole('button', { name: `Delete memory ${key} from ${agent}` })

describe('M6.4 — Memories table semantics', () => {
  it('names every column header and scopes it', async () => {
    const table = await openMemories()
    const headers = within(table).getAllByRole('columnheader')
    expect(headers).toHaveLength(6)
    for (const h of headers) expect(h).toHaveAttribute('scope', 'col')
    // The formerly-empty action column now carries an off-screen name.
    expect(within(table).getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument()
  })

  it('anchors every off-screen node so it cannot widen the page', async () => {
    const table = await openMemories()
    // Not a style-preference assertion — measured in Chrome at 375px, the
    // off-screen "Actions" span lands at x=483, i.e. 108px PAST the viewport.
    // It is harmless only because its host is positioned, which keeps it
    // clipped by the table's scroll container instead of letting it resolve
    // against the viewport and extend the document (the M6.3 defect exactly).
    const actionsHeader = within(table).getByRole('columnheader', { name: 'Actions' })
    expect(actionsHeader).toHaveStyle({ position: 'relative' })
    expect(table).toHaveStyle({ position: 'relative' })
  })

  it('exposes the section title as a real heading', async () => {
    await openMemories()
    expect(screen.getByRole('heading', { name: 'Agent Memories', level: 2 })).toBeInTheDocument()
  })

  it('reports zero axe violations', async () => {
    const { container } = render(<App />)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Memories' })) })
    await screen.findByRole('table', { name: /Stored agent memories/ })
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('M6.4 — Memories delete affordance', () => {
  it('gives each row Delete button a distinct accessible name', async () => {
    await openMemories()
    // "Delete" alone repeated N times is unusable in a screen reader's button
    // list — and this action is destructive.
    expect(deleteBtn('roster-2026', 'Research')).toBeInTheDocument()
    expect(deleteBtn('load-index', 'Data')).toBeInTheDocument()
    expect(deleteBtn('schema-notes', 'Code')).toBeInTheDocument()
  })

  it('carries the shared focus-ring class', async () => {
    await openMemories()
    expect(deleteBtn('roster-2026', 'Research')).toHaveClass('focus-ring')
  })

  it('moves focus to the next row after a delete instead of dropping it to body', async () => {
    await openMemories()
    const first = deleteBtn('roster-2026', 'Research')
    first.focus()
    await act(async () => { fireEvent.click(first) })

    await waitFor(() => expect(screen.queryByRole('button', { name: /roster-2026/ })).toBeNull())
    expect(document.activeElement).toBe(deleteBtn('load-index', 'Data'))
    expect(document.activeElement).not.toBe(document.body)
  })

  it('falls back to the previous row when the last row is deleted', async () => {
    await openMemories()
    const last = deleteBtn('schema-notes', 'Code')
    last.focus()
    await act(async () => { fireEvent.click(last) })

    await waitFor(() => expect(screen.queryByRole('button', { name: /schema-notes/ })).toBeNull())
    expect(document.activeElement).toBe(deleteBtn('load-index', 'Data'))
  })

  it('falls back to the section heading when no rows remain', async () => {
    await openMemories()
    for (const [key, agent] of [['roster-2026', 'Research'], ['load-index', 'Data'], ['schema-notes', 'Code']]) {
      const btn = deleteBtn(key, agent)
      btn.focus()
      await act(async () => { fireEvent.click(btn) })
      await waitFor(() => expect(screen.queryByRole('button', { name: new RegExp(key) })).toBeNull())
    }
    await screen.findByText('No memories yet')
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Agent Memories', level: 2 }))
    expect(document.activeElement).not.toBe(document.body)
  })
})
