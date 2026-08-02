import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, act } from '@testing-library/react'
import { setApiKey, clearApiKey } from './apiKey'
import { colors, statusBadgeColors } from './theme/tokens'
import { statusColors, statusTextColors } from './observability/timelineTheme'

// F1.1 — regressions for the Critical/High findings of the human accessibility
// pass. See docs/f1-1-human-accessibility-report.md.
//
// Split deliberately: this file owns the STRUCTURAL half plus the colour-maths
// contract (jsdom can assert token values and markup, and the maths is what
// justified the token choice). Real composited contrast, real focus movement
// and the graph canvas live in the Playwright suite, which is the only place
// they are honest.

vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }) => <div>{children}</div>,
  ReactFlow: () => <div data-testid="rf" />,
  Background: () => null,
  Controls: () => null,
  BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => ({ fitView: () => {} }),
}))

const { default: App } = await import('./App')
const { ManagerReviewCard } = await import('./App')
const jsonOk = (b) => Promise.resolve({ ok: true, json: async () => b })

beforeEach(() => {
  window.history.pushState(null, '', '/')
  globalThis.EventSource = class { constructor() {} close() {} }
  globalThis.fetch = vi.fn((url) => {
    const u = String(url)
    if (u.endsWith('/memories')) return jsonOk([])
    if (u.includes('/eval-suites')) return jsonOk({ suites: [] })
    if (u.includes('/tasks?limit')) return jsonOk([])
    if (u.includes('/error-rates')) return jsonOk({
      agentErrorRates: [{ agentType: 'Research', totalExecutions: 4, failedExecutions: 1, failureRate: 0.25, retryCount: 0, failuresByCategory: {} }],
      toolErrorRates: [{ toolName: 'web_search', totalCalls: 2, failedCalls: 0, failureRate: 0, failuresByCategory: {} }],
    })
    return jsonOk({})
  })
  setApiKey('test-key')
})
afterEach(() => { clearApiKey(); vi.restoreAllMocks() })

// ── contrast maths, kept executable ────────────────────────────────────────
const hex2rgb = (h) => { h = h.replace('#', ''); return [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16)) }
const lum = (rgb) => {
  const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4 }
  return 0.2126 * f(rgb[0]) + 0.7152 * f(rgb[1]) + 0.0722 * f(rgb[2])
}
const ratio = (a, b) => {
  const [la, lb] = [lum(hex2rgb(a)), lum(hex2rgb(b))]
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
}
// The badge/pill background is the status hue itself at 20%/15% over a surface,
// which is exactly why these failed: the tint lifts the background toward the
// foreground. Compositing here reproduces what the browser renders.
const over = (fgHex, alpha, bgHex) => {
  const [f, b] = [hex2rgb(fgHex), hex2rgb(bgHex)]
  return '#' + f.map((c, i) => Math.round(c * alpha + b[i] * (1 - alpha)).toString(16).padStart(2, '0')).join('')
}
const SURFACES = ['#11111b', '#1e1e2e', '#232336', '#2a2a40']

describe('F1.1 — status text tier (WCAG 1.4.3 AA)', () => {
  const PAIRS = [
    ['Running', colors.statusRunning, colors.statusRunningText],
    ['Completed', colors.statusCompleted, colors.statusCompletedText],
    ['Failed', colors.statusFailed, colors.statusFailedText],
    ['Pending', colors.statusPending, colors.statusPendingText],
    ['Warning', colors.statusWarning, colors.statusWarningText],
    ['Skipped', colors.overlay0, colors.subtext0],
  ]

  it.each(PAIRS)('%s text clears 4.5:1 on every background it can land on', (name, fill, text) => {
    for (const surface of SURFACES) {
      // StatusBadge: hue at 20% over the surface.
      expect(ratio(text, over(fill, 0.20, surface)), `${name} badge on ${surface}`).toBeGreaterThanOrEqual(4.5)
      // StatusPill: hue at ~15% (0x26) over the surface.
      expect(ratio(text, over(fill, 0x26 / 255, surface)), `${name} pill on ${surface}`).toBeGreaterThanOrEqual(4.5)
      // Plain text on the surface (SpanRow status, DetailPanel error label).
      expect(ratio(text, surface), `${name} plain on ${surface}`).toBeGreaterThanOrEqual(4.5)
    }
  })

  it.each(PAIRS)('%s FILL is left alone — this was a text fix, not a repaint', (name, fill) => {
    // Guards the whole premise: had the fix changed the hues instead of adding
    // a tier, every badge, border, node accent and graph edge would have moved.
    const key = name.toLowerCase()
    if (statusBadgeColors[key]) {
      expect(statusBadgeColors[key].accent).toBe(fill)
      expect(statusBadgeColors[key].background).toBe(`${fill}33`)
      expect(statusBadgeColors[key].border).toBe(`${fill}8c`)
    }
    if (statusColors[name]) expect(statusColors[name]).toBe(fill)
  })

  it('keeps the painted and the read maps distinct for every status', () => {
    for (const status of Object.keys(statusColors)) {
      expect(statusTextColors[status], `${status} missing a text tier`).toBeTruthy()
      expect(statusTextColors[status]).not.toBe(statusColors[status])
    }
  })
})

describe('F1.1 — StatusBadge renders the fix', () => {
  it('draws the glyph in the text tier and the border in the accent hue', async () => {
    const { StatusBadge } = await import('./components/StatusBadge')
    render(<StatusBadge status="Completed" />)
    const badge = screen.getByText('Completed')
    expect(badge).toHaveStyle({ color: colors.statusCompletedText })
    // The border must still be built from the untouched hue, or the "no visual
    // change" claim is false. jsdom serialises the hex + alpha suffix to
    // rgba(), so match on the accent hue's channels rather than its hex.
    const [r, g, b] = hex2rgb(colors.statusCompleted)
    expect(badge.getAttribute('style')).toContain(`border: 1px solid rgba(${r}, ${g}, ${b}, 0.55)`)
    expect(badge.getAttribute('style')).toContain(`background: rgba(${r}, ${g}, ${b}, 0.2)`)
  })
})

describe('F1.1 — markdown code is not OS-theme dependent', () => {
  // index.css styles bare `code` with variables that flip on
  // prefers-color-scheme. The app is hard-coded dark, so on a light-scheme
  // machine inline code rendered at 1.60:1. An explicit colour is the fix; its
  // ABSENCE is the bug, so that is what this asserts.
  it('sets an explicit colour on inline code in the manager review', () => {
    render(<ManagerReviewCard review={{ status: 'completed', result: 'Call the `db_query` tool.' }} />)
    const code = document.querySelector('code')
    expect(code).toBeTruthy()
    expect(code.style.color).not.toBe('')
    expect(code).toHaveStyle({ color: colors.text })
  })

  it('sets an explicit colour and a transparent plate on fenced code', () => {
    render(<ManagerReviewCard review={{ status: 'completed', result: '```\nSELECT 1\n```' }} />)
    const code = document.querySelector('pre code')
    expect(code).toBeTruthy()
    expect(code).toHaveStyle({ color: colors.text })
    // Transparent so the <pre> supplies the surface — otherwise index.css's
    // light `--code-bg` plate shows through inside a dark block.
    expect(code).toHaveStyle({ background: 'transparent' })
  })
})

describe('F1.1 — the run control keeps focus', () => {
  it('reports unavailability with aria-disabled, not the disabled attribute', async () => {
    render(<App />)
    const run = screen.getByRole('button', { name: /run agents/i })
    fireEvent.change(screen.getByLabelText('Task Title'), { target: { value: 't' } })
    fireEvent.change(screen.getByLabelText('Prompt'), { target: { value: 'p' } })
    run.focus()
    expect(document.activeElement).toBe(run)

    await act(async () => { fireEvent.submit(run.closest('form')) })

    // The whole point: still focusable, so focus never fell to <body>.
    expect(run).not.toBeDisabled()
    expect(run).toHaveAttribute('aria-disabled', 'true')
    expect(document.activeElement).toBe(run)
    expect(document.activeElement).not.toBe(document.body)
  })

  it('guards re-entry in the handler, since aria-disabled does not block submit', async () => {
    render(<App />)
    fireEvent.change(screen.getByLabelText('Task Title'), { target: { value: 't' } })
    fireEvent.change(screen.getByLabelText('Prompt'), { target: { value: 'p' } })
    const form = screen.getByRole('button', { name: /run agents/i }).closest('form')

    await act(async () => { fireEvent.submit(form) })
    const afterFirst = globalThis.fetch.mock.calls.filter(c => String(c[0]).endsWith('/tasks')).length
    await act(async () => { fireEvent.submit(form) })
    const afterSecond = globalThis.fetch.mock.calls.filter(c => String(c[0]).endsWith('/tasks')).length

    expect(afterSecond).toBe(afterFirst)
  })
})

describe('F1.1 — section headings exist to navigate by', () => {
  const headingNames = () =>
    screen.getAllByRole('heading').map(h => `${h.tagName}:${h.textContent.trim()}`)

  it('gives Evals real headings below the view title', async () => {
    render(<App />)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Evals' })) })
    expect(screen.getByRole('heading', { level: 1, name: 'Evals' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Eval Suites' })).toBeInTheDocument()
    expect(headingNames().length).toBeGreaterThan(1)
  })

  it('gives Observability Error Rates a heading per table', async () => {
    render(<App />)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Observability' })) })
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Error Rates' })) })
    expect(await screen.findByRole('heading', { level: 2, name: 'Agent Error Rates' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 2, name: 'Tool Error Rates' })).toBeInTheDocument()
  })

  it('adds heading semantics without adding heading margin', async () => {
    render(<App />)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Evals' })) })
    const h2 = screen.getByRole('heading', { level: 2, name: 'Eval Suites' })
    expect(h2.style.margin).toBe('0px')
    expect(h2.style.letterSpacing).toBe('inherit')
    // The other half of "semantics only" is `font: inherit`, which cancels
    // index.css's 24px h2 type. jsdom does not implement the `font` shorthand
    // at all — it drops the declaration from the serialised style attribute —
    // so that half is asserted in the browser suite, against a real computed
    // font-size (tests/playwright/a11y/f1-1.spec.js).
  })
})
