import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { AppShell } from './components/shell/AppShell'

// P2-M6.1 — skip-to-content link + focus target (keyboard navigation).

describe('M6.1 — skip-to-content link', () => {
  const renderShell = () =>
    render(
      <AppShell sidebar={<div>nav</div>} topBar={<div>title</div>}>
        <p>page content</p>
      </AppShell>,
    )

  it('renders a skip link that targets the main region', () => {
    renderShell()
    const link = screen.getByRole('link', { name: 'Skip to main content' })
    expect(link).toHaveAttribute('href', '#main')
    const main = screen.getByRole('main')
    expect(main).toHaveAttribute('id', 'main')
    expect(main).toHaveAttribute('tabindex', '-1')
  })

  it('places the skip link before <main> in DOM order (first focusable)', () => {
    renderShell()
    const link = screen.getByRole('link', { name: 'Skip to main content' })
    const main = screen.getByRole('main')
    // DOCUMENT_POSITION_FOLLOWING (4) => main comes after the link in the DOM.
    expect(link.compareDocumentPosition(main) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
