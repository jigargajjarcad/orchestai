// Vitest global setup — registers jest-dom matchers and cleans up the DOM
// between tests. Component-level test harness for P2-M3.
// P2-M6.1 — also registers the vitest-axe `toHaveNoViolations` matcher so any
// test can run axe-core structural accessibility assertions (labels, roles,
// ARIA). NOTE: axe-core in jsdom cannot evaluate color-contrast or responsive
// layout — those are verified via Playwright + a manual pass, not here.
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi, expect } from 'vitest'
import { toHaveNoViolations } from 'vitest-axe/matchers'

expect.extend({ toHaveNoViolations })

// jsdom lacks these browser APIs that some components (and their libraries)
// touch at mount. Test-only shims — no production behavior is affected.
if (!globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
}
if (!window.matchMedia) {
  window.matchMedia = vi.fn().mockImplementation((query) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  }))
}

afterEach(() => {
  cleanup()
})
