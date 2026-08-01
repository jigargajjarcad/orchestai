import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, act } from '@testing-library/react'
import { a11yAxe as axe } from './test/axe'
import { setApiKey, clearApiKey } from './apiKey'
import { colors } from './theme/tokens'

// F1 — accessibility conformance regressions.
//
// The browser suite owns contrast and real focus movement (jsdom can do
// neither). These lock in the STRUCTURAL half: the muted-text token contract,
// the Timeline control's semantics, and the sidebar landmark.

vi.mock('@xyflow/react', () => ({
  ReactFlowProvider: ({ children }) => <div>{children}</div>,
  ReactFlow: () => <div data-testid="rf" />,
  Background: () => null,
  Controls: () => null,
  BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => ({ fitView: () => {} }),
}))

const { default: App } = await import('./App')
const jsonOk = (b) => Promise.resolve({ ok: true, json: async () => b })

beforeEach(() => {
  window.history.pushState(null, '', '/')
  globalThis.EventSource = class { constructor() {} close() {} }
  globalThis.fetch = vi.fn((url) => {
    if (String(url).endsWith('/memories')) return jsonOk([
      { id: 'm1', agentType: 'Research', key: 'k', value: 'v', importance: 3, updatedAt: '2026-07-30T10:00:00Z' },
    ])
    if (String(url).includes('/eval-suites')) return jsonOk({ suites: [] })
    if (String(url).includes('/tasks?limit')) return jsonOk([])
    return jsonOk({})
  })
  setApiKey('test-key')
})
afterEach(() => { clearApiKey(); vi.restoreAllMocks() })

describe('F1 — muted text token', () => {
  it('meets the WCAG AA 4.5:1 bar against every surface tier', () => {
    // The contrast maths that justified the token choice, kept executable so a
    // future palette edit cannot silently drop below the bar.
    const hex2rgb = (h) => { h = h.replace('#', ''); return [0, 2, 4].map(i => parseInt(h.slice(i, i + 2), 16)) }
    const lum = (rgb) => {
      const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4 }
      const [r, g, b] = rgb
      return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)
    }
    const ratio = (a, b) => {
      const [la, lb] = [lum(hex2rgb(a)), lum(hex2rgb(b))]
      return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
    }
    const tiers = ['#11111b', '#1e1e2e', '#232336', '#2a2a40']
    for (const tier of tiers) {
      expect(ratio(colors.textMuted, tier), `textMuted on ${tier}`).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('leaves disabled text on the dimmer token, which WCAG exempts', async () => {
    const { interactiveState } = await import('./theme/tokens')
    // WCAG 1.4.3 exempts inactive controls. Brightening disabled text would
    // wrongly make it read as enabled.
    expect(interactiveState.disabledText).toBe(colors.overlay0)
    expect(interactiveState.disabledText).not.toBe(colors.textMuted)
  })
})

describe('F1 — landmarks', () => {
  it('places all sidebar content inside a landmark', async () => {
    // axe reported a `region` violation on all eleven surfaces: the brand block
    // sat outside <nav>, so it belonged to no landmark at all.
    const { container } = render(<App />)
    const aside = container.querySelector('aside')
    expect(aside).toBeTruthy()
    expect(aside.getAttribute('aria-label')).toBeTruthy()
    expect(aside.querySelector('nav')).toBeTruthy()
  })

  it('reports zero axe violations on the shell', async () => {
    const { container } = render(<App />)
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('F1 — Memories delete control', () => {
  it('carries the touch-target class alongside the focus ring', async () => {
    render(<App />)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Memories' })) })
    const del = await screen.findByRole('button', { name: /Delete memory/ })
    // Hit area is expanded by CSS rather than padding, so the class IS the
    // contract in jsdom — the real 44px measurement lives in the browser suite.
    expect(del).toHaveClass('touch-target')
    expect(del).toHaveClass('focus-ring')
  })
})
