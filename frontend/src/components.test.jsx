import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ApprovalCard } from './App'
import { Spinner } from './components/Spinner'
import { ErrorBanner } from './components/ErrorBanner'
import { EmptyState } from './components/EmptyState'

describe('ApprovalCard (approval flow)', () => {
  const request = { plan: 'Scrape and summarize', selectedAgents: ['Research', 'Writer'], executionMode: 'parallel' }

  it('renders the plan, selected agents, and execution mode', () => {
    render(<ApprovalCard request={request} onApprove={() => {}} onReject={() => {}} busy={false} />)
    expect(screen.getByText('Scrape and summarize')).toBeInTheDocument()
    expect(screen.getByText(/Research, Writer/)).toBeInTheDocument()
    expect(screen.getByText(/parallel/)).toBeInTheDocument()
  })

  it('invokes onApprove when Approve is clicked', () => {
    const onApprove = vi.fn()
    render(<ApprovalCard request={request} onApprove={onApprove} onReject={() => {}} busy={false} />)
    fireEvent.click(screen.getByText('Approve ✓'))
    expect(onApprove).toHaveBeenCalledOnce()
  })

  it('reveals the note field and invokes onReject with the note', () => {
    const onReject = vi.fn()
    render(<ApprovalCard request={request} onApprove={() => {}} onReject={onReject} busy={false} />)
    fireEvent.click(screen.getByText('Reject ✗'))
    fireEvent.change(screen.getByPlaceholderText(/Reason for rejection/), { target: { value: 'not safe' } })
    fireEvent.click(screen.getByText('Confirm Reject'))
    expect(onReject).toHaveBeenCalledWith('not safe')
  })
})

describe('shared state primitives', () => {
  it('Spinner exposes an accessible status with its label', () => {
    render(<Spinner label="Loading things…" />)
    const status = screen.getByRole('status')
    expect(status).toHaveTextContent('Loading things…')
  })

  it('ErrorBanner renders children as an alert', () => {
    render(<ErrorBanner>Something broke</ErrorBanner>)
    expect(screen.getByRole('alert')).toHaveTextContent('Something broke')
  })

  it('EmptyState renders its title and description', () => {
    render(<EmptyState title="Nothing here" description="Add something to get started" />)
    expect(screen.getByText('Nothing here')).toBeInTheDocument()
    expect(screen.getByText('Add something to get started')).toBeInTheDocument()
  })
})
