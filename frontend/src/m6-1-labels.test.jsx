import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Input, TextArea } from './components/Input'
import { Label } from './components/Label'

// P2-M6.1 — form-control label association (Input/Label useId + htmlFor).

describe('M6.1 — Input/Label association (useId)', () => {
  it('associates the visible label with the field so getByLabelText resolves', () => {
    render(<Input label="Task Title" value="" onChange={() => {}} />)
    expect(screen.getByLabelText('Task Title').tagName).toBe('INPUT')
  })

  it('associates the TextArea label too', () => {
    render(<TextArea label="Prompt" value="" onChange={() => {}} />)
    expect(screen.getByLabelText('Prompt').tagName).toBe('TEXTAREA')
  })

  it('renders the caption as a real <label htmlFor> pointing at the field id', () => {
    render(<Input label="Search" value="" onChange={() => {}} />)
    const label = screen.getByText('Search')
    const input = screen.getByLabelText('Search')
    expect(label.tagName).toBe('LABEL')
    expect(label).toHaveAttribute('for', input.id)
    expect(input.id).toBeTruthy()
  })

  it('honors a caller-supplied id instead of generating one', () => {
    render(<Input label="Custom" id="my-field" value="" onChange={() => {}} />)
    expect(screen.getByLabelText('Custom')).toHaveAttribute('id', 'my-field')
  })

  it('clicking the label moves focus to the field (real association)', async () => {
    const user = userEvent.setup()
    render(<TextArea label="Notes" value="" onChange={() => {}} />)
    await user.click(screen.getByText('Notes'))
    expect(screen.getByLabelText('Notes')).toHaveFocus()
  })

  it('keeps Label a plain <span> when used without htmlFor (section eyebrow)', () => {
    render(<Label>Agent Memories</Label>)
    expect(screen.getByText('Agent Memories').tagName).toBe('SPAN')
  })
})
