import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import { a11yAxe as axe } from './test/axe'
import { Input, TextArea } from './components/Input'

// P2-M6.1 — vitest-axe harness smoke test. Proves the shared axe runner +
// `toHaveNoViolations` matcher work; also validates that a labeled field has no
// structural a11y violations. (Color-contrast is disabled in jsdom — verified
// via Playwright + a manual pass per the P2-M6 verification strategy.)

describe('M6.1 — vitest-axe harness', () => {
  it('runs axe and reports zero violations on a labeled Input', async () => {
    const { container } = render(<Input label="Email address" value="" onChange={() => {}} />)
    expect(await axe(container)).toHaveNoViolations()
  })

  it('runs axe and reports zero violations on a labeled TextArea', async () => {
    const { container } = render(<TextArea label="Prompt" value="" onChange={() => {}} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
