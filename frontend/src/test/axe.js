// P2-M6.1 — shared axe-core runner for the jsdom test suite.
//
// axe-core cannot evaluate `color-contrast` in jsdom (it needs a real canvas /
// getContext to sample rendered pixels), so we disable that one rule here — it
// only produces noise and a jsdom "not implemented" stderr. Color contrast is
// verified separately via Playwright + a manual pass (the P2-M6 verification
// strategy). Everything else axe checks — labels, roles, ARIA, alt text,
// name-from-content — runs normally and IS the structural a11y gate.
//
// Usage:  import { a11yAxe } from './test/axe'
//         expect(await a11yAxe(container)).toHaveNoViolations()
import { configureAxe } from 'vitest-axe'

export const a11yAxe = configureAxe({
  rules: {
    'color-contrast': { enabled: false },
  },
})
