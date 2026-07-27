import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ManagerReviewCard } from './App'

// T1 — synthetic/component verification only. No genuine completed keyed run is
// available in this environment (no Anthropic/Perplexity credentials), so this
// proves the render contract against the confirmed SSE payload shape
// (`manager_review_completed` → { result }) — NOT a live end-to-end run.
describe('ManagerReviewCard (T1: manager-review result rendering)', () => {
  it('renders review.result once the review is completed', () => {
    render(
      <ManagerReviewCard
        review={{
          status: 'completed',
          result: 'The agents produced a **coherent synthesis** with three findings.',
        }}
      />,
    )
    expect(screen.getByText('Manager Review', { exact: false })).toBeInTheDocument()
    // The synthesized text (previously captured but never rendered) is now visible.
    expect(screen.getByText(/coherent synthesis/)).toBeInTheDocument()
    // Markdown emphasis renders as a <strong>, proving markdown treatment applies.
    expect(screen.getByText('coherent synthesis').tagName).toBe('STRONG')
  })

  it('does not render a result while the review is still running', () => {
    render(<ManagerReviewCard review={{ status: 'running' }} />)
    expect(screen.getByText(/Synthesizing and quality-checking/)).toBeInTheDocument()
    expect(screen.queryByText(/coherent synthesis/)).not.toBeInTheDocument()
  })

  it('shows the completed description without crashing when result is absent', () => {
    render(<ManagerReviewCard review={{ status: 'completed' }} />)
    expect(
      screen.getByText(/Synthesized and quality-checked all agent outputs/),
    ).toBeInTheDocument()
  })

  it('renders inline code inline (not as a <pre> block) — react-markdown v10 has no `inline` prop', () => {
    const { container } = render(
      <ManagerReviewCard review={{ status: 'completed', result: 'Set the `maxTokens` value.' }} />,
    )
    const codeEl = screen.getByText('maxTokens')
    expect(codeEl.tagName).toBe('CODE')
    // The regression: inline code must NOT be wrapped in <pre>, and must sit
    // inside the paragraph (a <pre> inside a <p> is invalid HTML).
    expect(codeEl.closest('pre')).toBeNull()
    expect(container.querySelector('p > code')).not.toBeNull()
  })

  it('renders a fenced code block inside a <pre>', () => {
    const { container } = render(
      <ManagerReviewCard review={{ status: 'completed', result: '```js\nconst x = 1\n```' }} />,
    )
    expect(container.querySelector('pre')).not.toBeNull()
  })
})
