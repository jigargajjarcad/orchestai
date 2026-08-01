import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { a11yAxe as axe } from './test/axe'
import { clearApiKey, hasApiKey } from './apiKey'
import ApiKeyPrompt from './ApiKeyPrompt'

// P2-M6.4 — the onboarding gate is the app's only entry surface. Everything a
// keyboard or screen-reader user needs to get past it is asserted here.

afterEach(() => { clearApiKey() })

describe('M6.4 — onboarding gate', () => {
  it('focuses the API key field on mount', () => {
    render(<ApiKeyPrompt onSubmitted={() => {}} />)
    const field = screen.getByLabelText('API key')
    expect(document.activeElement).toBe(field)
    expect(document.activeElement).not.toBe(document.body)
  })

  it('associates the visible caption with the field', () => {
    render(<ApiKeyPrompt onSubmitted={() => {}} />)
    // getByLabelText above already proves association; assert the wiring
    // explicitly so a regression names the cause rather than "not found".
    const field = screen.getByLabelText('API key')
    expect(document.querySelector(`label[for="${field.id}"]`)).toHaveTextContent('API key')
  })

  it('submits with Enter from the field, without reaching for the mouse', () => {
    const onSubmitted = vi.fn()
    render(<ApiKeyPrompt onSubmitted={onSubmitted} />)
    const field = screen.getByLabelText('API key')
    field.focus()
    // Type a key, then press Enter — the whole flow without a pointer.
    fireEvent.change(field, { target: { value: 'orch_live_abc' } })
    fireEvent.keyDown(field, { key: 'Enter' })
    expect(onSubmitted).toHaveBeenCalled()
    expect(hasApiKey()).toBe(true)
  })

  it('exposes a heading and reports zero axe violations', async () => {
    const { container } = render(<ApiKeyPrompt onSubmitted={() => {}} />)
    expect(screen.getByRole('heading', { name: 'OrchestAI' })).toBeInTheDocument()
    expect(await axe(container)).toHaveNoViolations()
  })
})
