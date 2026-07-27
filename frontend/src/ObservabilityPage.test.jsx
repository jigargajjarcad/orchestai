import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import ObservabilityPage from './ObservabilityPage'

// Mock only the network boundary. The protected Timeline block is not touched;
// with no tasks the default Timeline sub-view renders its picker/empty state.
vi.mock('./apiKey', () => ({ authenticatedFetch: vi.fn() }))
import { authenticatedFetch } from './apiKey'

beforeEach(() => { vi.clearAllMocks() })

describe('ObservabilityPage tasks list (T5a: error vs empty)', () => {
  it('surfaces an ErrorBanner when the tasks fetch fails', async () => {
    authenticatedFetch.mockResolvedValueOnce({ ok: false, status: 500, json: async () => ({ title: 'Tasks unavailable' }) })
    render(<ObservabilityPage />)
    expect(await screen.findByRole('alert')).toHaveTextContent('Tasks unavailable')
  })

  it('does not show an error for a legitimate empty tasks list', async () => {
    authenticatedFetch.mockResolvedValueOnce({ ok: true, json: async () => [] })
    render(<ObservabilityPage />)
    // Let the effect settle; the Timeline sub-nav is always present.
    expect(await screen.findByText('Timeline')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
