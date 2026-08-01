import { useState, useEffect, useRef } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import ObservabilityPage from './ObservabilityPage'
import EvalsPage from './EvalsPage'
import { hasApiKey, clearApiKey, authenticatedFetch } from './apiKey'
import ApiKeyPrompt from './ApiKeyPrompt'
import {
  colors,
  radii,
  spacing,
  surface,
  elevation,
  uiFontFamily,
  dataFontFamily,
  buttonGhostBorderAlphaSuffix,
} from './theme/tokens'
import { StatusBadge } from './components/StatusBadge'
import { Panel } from './components/Panel'
import { Card } from './components/Card'
import { Button } from './components/Button'
import { Input, TextArea } from './components/Input'
import { Spinner } from './components/Spinner'
import { ErrorBanner } from './components/ErrorBanner'
import { Label } from './components/Label'
import { Toggle } from './components/Toggle'
import { EmptyState } from './components/EmptyState'
import { AppShell } from './components/shell/AppShell'
import { Sidebar } from './components/shell/Sidebar'
import { TopBar } from './components/shell/TopBar'
import { PageContainer } from './components/shell/PageContainer'
import { useRouting } from './hooks/useRouting'
import { useBreakpoint } from './hooks/useBreakpoint'
import { usePlaygroundExecutionGraph } from './playground/usePlaygroundExecutionGraph'
import { PlaygroundExecutionGraph } from './playground/PlaygroundExecutionGraph'
import './App.css'

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`
const DEV_USER_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

// P2-M6.2 — visually-hidden-but-screen-reader-available text. Used to convey
// status that is otherwise carried only by a decorative glyph/color (tool-call
// state) and for the live streaming-status announcement region.
const srOnly = {
  position: 'absolute', width: 1, height: 1, padding: 0, margin: -1,
  overflow: 'hidden', clip: 'rect(0 0 0 0)', whiteSpace: 'nowrap', border: 0,
}

// P2-M6.4 — spread onto whatever hosts an `srOnly` node. `srOnly` is
// position:absolute, so an unpositioned host leaves its containing block as the
// viewport: the hidden node then escapes every `overflow` clip above it and adds
// its own width to the document's scrollable area. That is exactly how an
// invisible <span> made the Observability page scroll 86px sideways at 375px in
// M6.3. Cheap to apply, and it makes the containment local to the host.
const srOnlyAnchor = { position: 'relative' }

// Sentinel for MemoriesPage's post-delete focus target: the section heading,
// used when no row remains to receive focus.
const HEADING_FOCUS_TARGET = '__heading__'

// ---------------------------------------------------------------------------
// P2-M1B Task 2 — Sidebar nav content: inline monochrome stroke icons
// (`currentColor`, ~18px), matching the approved shell-demo icon style
// (shell-demo/ShellDemo.jsx's DashboardIcon/TableIcon/etc.) — NOT the old
// emoji. Brand/NAV_ITEMS/VIEW_TITLES feed AppShell's Sidebar/TopBar slots.
// ---------------------------------------------------------------------------

function PlaygroundIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <path d="M6 4.5v11l9-5.5-9-5.5Z" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
    </svg>
  )
}

function MemoriesIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <path
        d="M7 3.5a2.5 2.5 0 0 0-2.4 3.2A2.5 2.5 0 0 0 4 9a2.5 2.5 0 0 0 2 2.45V13a3 3 0 0 0 3 3"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M13 3.5a2.5 2.5 0 0 1 2.4 3.2A2.5 2.5 0 0 1 16 9a2.5 2.5 0 0 1-2 2.45V13a3 3 0 0 1-3 3"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

function ObservabilityIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <path d="M4 16V9M10 16V4M16 16v-6" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
      <path d="M2.5 16.5h15" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  )
}

function EvalsIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <circle cx="10" cy="10" r="6.5" stroke="currentColor" strokeWidth="1.5" />
      <circle cx="10" cy="10" r="3" stroke="currentColor" strokeWidth="1.5" />
      <circle cx="10" cy="10" r="0.75" fill="currentColor" />
    </svg>
  )
}

// Sidebar `brand` node — the current header's h1/subtitle, unchanged text
// and colors (colors.traceBlue / colors.surface2), just relocated.
function Brand() {
  // The wordmark is a brand mark, NOT the page heading — so it is a styled
  // <div>, not an <h1>. The single page <h1> is TopBar's per-view title
  // (VIEW_TITLES), preserving one-h1-per-view heading semantics after the
  // shell migration (the old header made "OrchestAI" the h1; the new shell
  // gives each surface its own titled h1 in the TopBar instead).
  return (
    <div>
      <div style={{ fontSize: 20, fontWeight: 700, color: colors.traceBlue }}>OrchestAI</div>
      <p style={{ color: colors.subtext0, margin: 0, fontSize: 11 }}>Multi-agent CQRS orchestration · .NET 8</p>
    </div>
  )
}

const NAV_ITEMS = [
  { key: 'playground', label: 'Playground', icon: <PlaygroundIcon /> },
  { key: 'memories', label: 'Memories', icon: <MemoriesIcon /> },
  { key: 'observability', label: 'Observability', icon: <ObservabilityIcon /> },
  { key: 'evals', label: 'Evals', icon: <EvalsIcon /> },
]

const VIEW_TITLES = {
  playground: 'Playground',
  memories: 'Memories',
  observability: 'Observability',
  evals: 'Evals',
}

function ToolCallRow({ toolCall }) {
  const isRunning = toolCall.durationMs == null
  const color = isRunning ? colors.statusWarning : (toolCall.success ? colors.statusCompleted : colors.statusFailed)
  const label = isRunning ? '⏳' : (toolCall.success ? '✓' : '✗')
  // P2-M6.2 — status is otherwise carried only by the glyph + color (color-alone
  // to a screen reader); expose it as text and hide the decorative glyph.
  const statusWord = isRunning ? 'running' : (toolCall.success ? 'completed' : 'failed')
  const duration = toolCall.durationMs != null ? ` · ${toolCall.durationMs}ms` : ''

  return (
    <div style={{
      display: 'flex',
      alignItems: 'flex-start',
      gap: spacing.sm,
      padding: '5px 8px',
      background: colors.mantle,
      borderLeft: `3px solid ${color}`,
      borderRadius: `0 ${radii.md} ${radii.md} 0`,
      boxShadow: elevation.flat,
      marginTop: spacing.xs,
      fontSize: 12,
      fontFamily: dataFontFamily,
    }}>
      <span aria-hidden="true" style={{ color, flexShrink: 0, minWidth: 14 }}>{label}</span>
      <div style={{ flex: 1, minWidth: 0 }}>
        <span style={srOnly}>Tool {toolCall.name} {statusWord}. </span>
        <span style={{ color: colors.traceBlue, fontWeight: 600 }}>{toolCall.name}</span>
        <span style={{ color: colors.surface2 }}>{duration}</span>
        {toolCall.output && (
          <div style={{ color: colors.subtext0, marginTop: 2, wordBreak: 'break-word' }}>
            {toolCall.output.length > 120 ? toolCall.output.slice(0, 120) + '…' : toolCall.output}
          </div>
        )}
        {toolCall.error && (
          <div style={{ color: colors.alertRed, marginTop: 2 }}>{toolCall.error}</div>
        )}
      </div>
    </div>
  )
}

function AgentCard({ execution, memoryCount, savedMemories }) {
  return (
    <div style={{ marginBottom: 10 }}>
      <Panel accentStatus={execution.status} style={{ padding: '12px 14px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <h3 style={{ color: colors.text, fontSize: 14, fontFamily: uiFontFamily, fontWeight: 700, margin: 0 }}>{execution.agentType}</h3>
            {memoryCount > 0 && (
              <span title={`${memoryCount} saved ${memoryCount === 1 ? 'memory' : 'memories'} for this agent`} style={{ fontSize: 11, color: colors.subtext0 }}>
                <span aria-hidden="true">🧠</span> {memoryCount}
              </span>
            )}
          </div>
          <StatusBadge status={execution.status} borderAlphaSuffix="60" style={{ padding: '1px 8px', letterSpacing: '0.05em' }} />
        </div>

        {execution.messages?.map((msg, i) => (
          <div key={i} style={{
            background: colors.mantle,
            borderRadius: radii.md,
            padding: '6px 10px',
            marginTop: 4,
            fontSize: 12,
            color: colors.subtext0,
            fontFamily: dataFontFamily,
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word',
            maxHeight: 160,
            overflow: 'auto',
          }}>
            {msg}
          </div>
        ))}

        {execution.toolCalls?.length > 0 && (
          <div style={{ marginTop: 6 }}>
            {execution.toolCalls.map((tc, i) => (
              <ToolCallRow key={i} toolCall={tc} />
            ))}
          </div>
        )}

        {execution.costUsd > 0 && (
          <div style={{ fontSize: 11, color: colors.surface2, marginTop: 8, display: 'flex', gap: 12, fontFamily: dataFontFamily }}>
            <span>{execution.inputTokens}in + {execution.outputTokens}out tokens</span>
            <span style={{ color: colors.overlay0 }}>${Number(execution.costUsd).toFixed(6)}</span>
          </div>
        )}

        {savedMemories > 0 && (
          <div style={{ fontSize: 11, color: colors.signalGreen, marginTop: 6 }}>
            🧠 Saved {savedMemories} {savedMemories === 1 ? 'memory' : 'memories'}
          </div>
        )}
      </Panel>
    </div>
  )
}

export function ApprovalCard({ request, onApprove, onReject, busy }) {
  const [showRejectInput, setShowRejectInput] = useState(false)
  const [note, setNote] = useState('')
  // P2-M6.2 — the run pauses here waiting on the user; move focus to the card on
  // mount so keyboard/screen-reader users are taken to it (the SSE live region
  // also announces it). tabIndex=-1 = a programmatic focus target only.
  const cardRef = useRef(null)
  useEffect(() => { cardRef.current?.focus() }, [])
  // When the reject-note field is revealed, move focus into it (it replaces the
  // focused Reject button). Done via a ref-effect rather than `autoFocus` so no
  // jsx-a11y no-autofocus warning is introduced.
  useEffect(() => {
    if (showRejectInput) cardRef.current?.querySelector('textarea')?.focus()
  }, [showRejectInput])

  return (
    <div
      ref={cardRef}
      tabIndex={-1}
      role="group"
      aria-label="Approval required"
      style={{
      border: `1px solid ${colors.statusWarning}60`,
      borderLeft: `3px solid ${colors.statusWarning}`,
      borderRadius: radii.xl,
      padding: '14px 16px',
      marginBottom: 14,
      background: surface.surface,
      boxShadow: elevation.flat,
      outline: 'none',
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
        <span aria-hidden="true">⏸</span>
        <strong style={{ color: colors.statusWarning, fontSize: 13, fontFamily: uiFontFamily }}>Waiting for Your Approval</strong>
      </div>
      <div style={{ fontSize: 12, color: colors.text, marginBottom: 8, lineHeight: 1.5, fontFamily: dataFontFamily }}>{request.plan}</div>
      <div style={{ fontSize: 11, color: colors.subtext0, marginBottom: 12, fontFamily: uiFontFamily }}>
        <div>Agents selected: {request.selectedAgents?.join(', ')}</div>
        <div>Mode: {request.executionMode}</div>
      </div>

      {!showRejectInput ? (
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            onClick={onApprove}
            disabled={busy}
            className="focus-ring"
            style={{
              flex: 1, padding: '7px 0', borderRadius: radii.lg, border: 'none',
              background: colors.statusCompleted, color: colors.crust, fontWeight: 700, fontSize: 12,
              fontFamily: uiFontFamily,
              cursor: busy ? 'not-allowed' : 'pointer',
            }}
          >
            Approve ✓
          </button>
          <button
            onClick={() => setShowRejectInput(true)}
            disabled={busy}
            className="focus-ring"
            style={{
              flex: 1, padding: '7px 0', borderRadius: radii.lg, border: `1px solid ${colors.statusFailed}${buttonGhostBorderAlphaSuffix}`,
              background: 'transparent', color: colors.alertRed, fontWeight: 700, fontSize: 12,
              fontFamily: uiFontFamily,
              cursor: busy ? 'not-allowed' : 'pointer',
            }}
          >
            Reject ✗
          </button>
        </div>
      ) : (
        <div>
          <TextArea
            value={note}
            onChange={e => setNote(e.target.value)}
            placeholder="Reason for rejection (optional)"
            aria-label="Reason for rejection (optional)"
            rows={2}
            style={{ padding: '6px 8px', fontSize: 12, marginBottom: 8 }}
          />
          <div style={{ display: 'flex', gap: 8 }}>
            <button
              onClick={() => onReject(note)}
              disabled={busy}
              className="focus-ring"
              style={{
                flex: 1, padding: '7px 0', borderRadius: radii.lg, border: 'none',
                background: colors.statusFailed, color: colors.crust, fontWeight: 700, fontSize: 12,
                fontFamily: uiFontFamily,
                cursor: busy ? 'not-allowed' : 'pointer',
              }}
            >
              Confirm Reject
            </button>
            <button
              onClick={() => setShowRejectInput(false)}
              disabled={busy}
              className="focus-ring"
              style={{
                flex: 1, padding: '7px 0', borderRadius: radii.lg, border: `1px solid ${colors.surface0}`,
                background: 'transparent', color: colors.subtext0, fontWeight: 700, fontSize: 12,
                fontFamily: uiFontFamily,
                cursor: busy ? 'not-allowed' : 'pointer',
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

// Compact markdown treatment for the manager-review synthesis rendered inline
// in the execution feed. Intentionally lighter than the right-pane Final Result
// renderer (smaller headings, no code-block chrome) — this card is the
// narrative "the manager synthesized this" beat, not the primary artifact.
const reviewMarkdownComponents = {
  h1: ({ children }) => <h3 style={{ color: colors.traceBlue, fontSize: 14, margin: '8px 0 4px' }}>{children}</h3>,
  h2: ({ children }) => <h4 style={{ color: colors.traceBlue, fontSize: 13, margin: '8px 0 4px' }}>{children}</h4>,
  h3: ({ children }) => <h5 style={{ color: colors.agentSky, fontSize: 12, margin: '8px 0 4px' }}>{children}</h5>,
  a: ({ href, children }) => <a href={href} style={{ color: colors.traceBlue }} target="_blank" rel="noreferrer">{children}</a>,
  p: ({ children }) => <p style={{ margin: '0 0 8px' }}>{children}</p>,
  ul: ({ children }) => <ul style={{ paddingLeft: 18, margin: '0 0 8px' }}>{children}</ul>,
  li: ({ children }) => <li style={{ marginBottom: 3 }}>{children}</li>,
  pre: ({ children }) => <pre style={{ background: colors.mantle, borderRadius: 6, padding: '10px 12px', overflow: 'auto', margin: '0 0 8px' }}>{children}</pre>,
  // react-markdown v10 no longer passes an `inline` flag to `code`, so detect a
  // fenced block via its language class / multiline content instead. Inline
  // backticks must stay inline `<code>` — never emit a <pre> inside a <p>.
  code: ({ className, children }) => {
    const isBlock = /language-/.test(className || '') || String(children).includes('\n')
    return isBlock
      ? <code style={{ fontSize: 11, fontFamily: dataFontFamily }}>{children}</code>
      : <code style={{ background: colors.surface0, borderRadius: 3, padding: '1px 5px', fontSize: 11, fontFamily: dataFontFamily }}>{children}</code>
  },
}

export function ManagerReviewCard({ review }) {
  const isRunning = review.status === 'running'
  // Panel's accentStatus prop only understands execution-status keys
  // (Pending/Running/WaitingForApproval/Completed/Failed/Skipped). This
  // card's own running/completed states are colored orange/green — which
  // happen to be exactly Panel's WaitingForApproval/Completed accent colors
  // — so those keys are reused here purely for their color values, with
  // StatusBadge's `label` override supplying the correct RUNNING/COMPLETED
  // display text instead of WAITINGFORAPPROVAL/COMPLETED.
  const accentStatus = isRunning ? 'WaitingForApproval' : 'Completed'
  // The synthesized review text (SSE `manager_review_completed` → `review.result`)
  // was previously captured but never rendered; surface it once the review lands.
  const hasResult = !isRunning && Boolean(review.result)

  return (
    <div style={{ marginBottom: 10 }}>
      <Panel accentStatus={accentStatus} style={{ padding: '12px 14px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <h3 style={{ color: colors.text, fontSize: 14, fontFamily: uiFontFamily, fontWeight: 700, margin: 0 }}><span aria-hidden="true">🎯</span> Manager Review</h3>
          <StatusBadge status={accentStatus} label={isRunning ? 'Running' : 'Completed'} borderAlphaSuffix="60" style={{ padding: '1px 8px', letterSpacing: '0.05em' }} />
        </div>
        <div style={{ fontSize: 12, color: colors.subtext0, marginTop: 8, fontFamily: uiFontFamily }}>
          {isRunning
            ? 'Synthesizing and quality-checking all agent outputs…'
            : 'Synthesized and quality-checked all agent outputs into the final result.'}
        </div>
        {hasResult && (
          <div style={{ marginTop: 10, fontSize: 12, lineHeight: 1.6, color: colors.text, fontFamily: uiFontFamily }}>
            <ReactMarkdown remarkPlugins={[remarkGfm]} components={reviewMarkdownComponents}>
              {review.result}
            </ReactMarkdown>
          </div>
        )}
      </Panel>
    </div>
  )
}

function MemoriesPage({ onDelete }) {
  const [memories, setMemories] = useState(null)
  const [error, setError] = useState(null)
  // P2-M6.4 — deleting a row destroys the button that had focus, which drops
  // focus to <body>: a keyboard user loses their place entirely and has to tab
  // back from the top of the document. Track the row buttons so focus can be
  // moved somewhere deliberate instead, and remember the intended target across
  // the re-render that removes the row.
  const deleteButtonRefs = useRef(new Map())
  const headingRef = useRef(null)
  const [focusAfterDelete, setFocusAfterDelete] = useState(null)

  const load = async () => {
    try {
      const res = await authenticatedFetch(`${API_BASE}/users/${DEV_USER_ID}/memories`)
      if (!res.ok) throw new Error(`Failed to load memories: ${res.status}`)
      setMemories(await res.json())
    } catch (err) {
      setError(err.message)
      setMemories([])
    }
  }

  useEffect(() => { load() }, [])

  useEffect(() => {
    if (focusAfterDelete == null) return
    // Prefer the row that took the deleted row's place, then the one before it,
    // and fall back to the section heading when the table is gone entirely.
    const target = focusAfterDelete === HEADING_FOCUS_TARGET
      ? headingRef.current
      : deleteButtonRefs.current.get(focusAfterDelete)
    ;(target ?? headingRef.current)?.focus()
    setFocusAfterDelete(null)
  }, [focusAfterDelete, memories])

  const handleDelete = async (id) => {
    // Resolve the successor BEFORE the row disappears — afterwards the index is
    // gone and there is nothing left to compute a neighbour from.
    const index = memories?.findIndex(m => m.id === id) ?? -1
    const successor = memories?.[index + 1] ?? memories?.[index - 1] ?? null
    try {
      await authenticatedFetch(`${API_BASE}/users/${DEV_USER_ID}/memories/${id}`, { method: 'DELETE' })
      setMemories(prev => prev.filter(m => m.id !== id))
      deleteButtonRefs.current.delete(id)
      setFocusAfterDelete(successor ? successor.id : HEADING_FOCUS_TARGET)
      onDelete?.()
    } catch (err) {
      setError(err.message)
    }
  }

  return (
    <div>
      {/* Section heading, matching M6.2's pattern elsewhere in this file: a real
          <h2> wrapping the Label so the visual treatment is byte-for-byte the
          same while the structure becomes navigable. tabIndex=-1 makes it a
          programmatic focus target for post-delete focus recovery (it is not in
          the tab order). */}
      <div style={{ marginBottom: 16 }}>
        <h2 ref={headingRef} tabIndex={-1} style={{ margin: 0, font: 'inherit', letterSpacing: 'inherit' }}>
          <Label style={{ color: colors.surface2, fontWeight: 400 }}>Agent Memories</Label>
        </h2>
      </div>

      {error && <ErrorBanner style={{ marginBottom: spacing.lg }}>{error}</ErrorBanner>}

      {memories == null ? (
        <Spinner label="Loading memories…" />
      ) : memories.length === 0 ? (
        <EmptyState
          title="No memories yet"
          description="Run a task and agents will start remembering things."
        />
      ) : (
        <Card level="surface">
          <div style={{ overflowX: 'auto' }}>
            {/* P2-M6.4 — `width:100%` alone pins the table to the container, so
                on a phone it compressed instead of using the overflow-x wrapper
                it already sits in: the Value column collapsed to roughly one
                character per line (verified in Chrome at 375px). minWidth gives
                the columns a floor and hands the overflow to the wrapper, which
                is the pattern the other tables in this app already use. Desktop
                is unaffected — the container is far wider than the floor, so
                width:100% still wins there. */}
            <table style={{ width: '100%', minWidth: 640, borderCollapse: 'collapse', ...srOnlyAnchor, fontSize: 12, fontFamily: uiFontFamily }}>
              <caption style={srOnly}>Stored agent memories, with a delete control per row</caption>
              <thead>
                <tr style={{ textAlign: 'left', color: colors.overlay0, borderBottom: `1px solid ${colors.surface0}` }}>
                  <th scope="col" style={{ padding: `${spacing.sm} ${spacing.md}` }}>Agent</th>
                  <th scope="col" style={{ padding: `${spacing.sm} ${spacing.md}` }}>Key</th>
                  <th scope="col" style={{ padding: `${spacing.sm} ${spacing.md}` }}>Value</th>
                  <th scope="col" style={{ padding: `${spacing.sm} ${spacing.md}` }}>Importance</th>
                  <th scope="col" style={{ padding: `${spacing.sm} ${spacing.md}` }}>Updated</th>
                  {/* Was an empty <th> (the one jsx-a11y control-has-associated-label
                      warning on this file). The name is off-screen so the column
                      header stays visually blank; srOnlyAnchor keeps that hidden
                      text from widening the page. */}
                  <th scope="col" style={{ padding: `${spacing.sm} ${spacing.md}`, ...srOnlyAnchor }}>
                    <span style={srOnly}>Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {memories.map(m => (
                  <tr key={m.id} style={{ borderBottom: `1px solid ${colors.base}` }}>
                    <td style={{ padding: `${spacing.sm} ${spacing.md}`, color: colors.traceBlue, fontFamily: uiFontFamily }}>{m.agentType}</td>
                    <td style={{ padding: `${spacing.sm} ${spacing.md}`, color: colors.text, fontFamily: dataFontFamily }}>{m.key}</td>
                    <td style={{ padding: `${spacing.sm} ${spacing.md}`, color: colors.subtext0, fontFamily: uiFontFamily, maxWidth: 400, wordBreak: 'break-word' }}>{m.value}</td>
                    <td style={{ padding: `${spacing.sm} ${spacing.md}`, color: colors.overlay0, fontFamily: dataFontFamily }}>{m.importance}</td>
                    <td style={{ padding: `${spacing.sm} ${spacing.md}`, color: colors.overlay0, fontFamily: dataFontFamily }}>{new Date(m.updatedAt).toLocaleString()}</td>
                    <td style={{ padding: `${spacing.sm} ${spacing.md}` }}>
                      {/* Every row's control reads as a bare "Delete" to a screen
                          reader listing buttons — indistinguishable, and this one
                          is destructive. The aria-label names the specific memory.
                          focus-ring matches the shared control convention. */}
                      <button
                        ref={el => {
                          if (el) deleteButtonRefs.current.set(m.id, el)
                          else deleteButtonRefs.current.delete(m.id)
                        }}
                        className="focus-ring"
                        aria-label={`Delete memory ${m.key} from ${m.agentType}`}
                        onClick={() => handleDelete(m.id)}
                        style={{ background: 'transparent', border: `1px solid ${colors.statusFailed}80`, color: colors.alertRed, borderRadius: radii.md, padding: '3px 8px', fontSize: 11, fontFamily: uiFontFamily, cursor: 'pointer' }}
                      >
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </div>
  )
}

export default function App() {
  const [keySet, setKeySet] = useState(hasApiKey())
  const [title, setTitle] = useState('')
  const [prompt, setPrompt] = useState('')
  const [requireApproval, setRequireApproval] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [taskId, setTaskId] = useState(null)
  const [taskStatus, setTaskStatus] = useState(null)
  const [agents, setAgents] = useState({})
  const [agentOrder, setAgentOrder] = useState([])
  const [finalResult, setFinalResult] = useState(null)
  const [totalCost, setTotalCost] = useState(null)
  const [error, setError] = useState(null)
  const [approvalRequest, setApprovalRequest] = useState(null)
  const [approvalBusy, setApprovalBusy] = useState(false)
  const [managerReview, setManagerReview] = useState(null)
  // P2-M6.2 — a single curated status string announced to screen readers on
  // execution PHASE transitions (task/agent/review start+end), rendered in one
  // visually-hidden aria-live="polite" region. Deliberately NOT per-token/
  // per-tool (that would spam AT) — see the SSE handler below.
  const [liveStatus, setLiveStatus] = useState('')
  // P2-M5 — live execution graph. Additive: the same SSE events the card feed
  // already consumes are ALSO folded into a derived span array for the graph;
  // the card reducer (updateAgent/addToolCall/…) is untouched. Default 'graph'
  // is the milestone's differentiator; 'list' is the unchanged card fallback.
  const [playgroundViewMode, setPlaygroundViewMode] = useState('graph')
  const [handoffTaskId, setHandoffTaskId] = useState(null)
  const { liveSpans, dispatchGraphEvent, reset: resetGraph } = usePlaygroundExecutionGraph()
  const { view, navigate } = useRouting()
  // P2-M1B Task 2 — the old `useViewportWidth() <= 768` narrow check is
  // replaced by the shared breakpoint hook; the Playground 2-column grid
  // stacks to 1 column below desktop, a deliberate documented change from the
  // old <=768 stack point (see the grid below). `atLeast('desktop')` is
  // true at BOTH the `desktop` (1024–1439) and `wide` (>=1440) buckets — the
  // 2-pane workspace must persist on large/ultra-wide monitors, so this is
  // NOT the exact-bucket `isDesktop` flag (which would wrongly stack >=1440).
  const { atLeast } = useBreakpoint()
  const playgroundStacked = !atLeast('desktop')
  const [memoryCounts, setMemoryCounts] = useState({})
  const [memoryBaseline, setMemoryBaseline] = useState({})
  const [savedMemories, setSavedMemories] = useState({})
  const eventSourceRef = useRef(null)

  const fetchMemoryCounts = async () => {
    try {
      const res = await authenticatedFetch(`${API_BASE}/users/${DEV_USER_ID}/memories`)
      if (!res.ok) return {}
      const memories = await res.json()
      const counts = {}
      for (const m of memories) counts[m.agentType] = (counts[m.agentType] ?? 0) + 1
      return counts
    } catch (_) {
      return {}
    }
  }

  useEffect(() => { fetchMemoryCounts().then(setMemoryCounts) }, [])

  const updateAgent = (id, patch) => {
    setAgents(prev => {
      const current = prev[id] ?? {}
      return { ...prev, [id]: { ...current, ...patch } }
    })
  }

  const appendAgentMessage = (id, agentType, preview) => {
    setAgents(prev => {
      const current = prev[id] ?? { agentType, status: 'Running', messages: [], toolCalls: [], inputTokens: 0, outputTokens: 0, costUsd: 0 }
      return { ...prev, [id]: { ...current, messages: [...current.messages, preview] } }
    })
  }

  const addToolCall = (agentExecutionId, toolName) => {
    setAgents(prev => {
      const current = prev[agentExecutionId] ?? {}
      const toolCalls = [...(current.toolCalls ?? []), { name: toolName, durationMs: null, success: null, output: null, error: null }]
      return { ...prev, [agentExecutionId]: { ...current, toolCalls } }
    })
  }

  const completeToolCall = (agentExecutionId, toolName, success, durationMs, outputPreview) => {
    setAgents(prev => {
      const current = prev[agentExecutionId] ?? {}
      const toolCalls = [...(current.toolCalls ?? [])]
      // Update the last matching in-progress tool call
      const idx = [...toolCalls].reverse().findIndex(tc => tc.name === toolName && tc.durationMs == null)
      if (idx !== -1) {
        const realIdx = toolCalls.length - 1 - idx
        toolCalls[realIdx] = { ...toolCalls[realIdx], success, durationMs, output: success ? outputPreview : null, error: success ? null : outputPreview }
      }
      return { ...prev, [agentExecutionId]: { ...current, toolCalls } }
    })
  }

  const handleSubmit = async (e) => {
    e.preventDefault()
    setError(null)
    setSubmitting(true)
    setAgents({})
    setAgentOrder([])
    setFinalResult(null)
    setTotalCost(null)
    setTaskId(null)
    setTaskStatus(null)
    setApprovalRequest(null)
    setManagerReview(null)
    setSavedMemories({})
    setHandoffTaskId(null)
    setLiveStatus('')
    resetGraph()
    eventSourceRef.current?.close()

    const baseline = await fetchMemoryCounts()
    setMemoryBaseline(baseline)
    setMemoryCounts(baseline)

    try {
      const createRes = await authenticatedFetch(`${API_BASE}/tasks`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId: DEV_USER_ID, title, userPrompt: prompt, requireApproval }),
      })
      if (!createRes.ok) throw new Error(`Create failed: ${createRes.status}`)
      const created = await createRes.json()
      const id = created.id
      setTaskId(id)
      setTaskStatus('Pending')

      await authenticatedFetch(`${API_BASE}/tasks/${id}/start`, { method: 'POST' })
      setTaskStatus('Running')

      // EventSource is a native browser API and cannot set custom request headers, so it can't
      // carry the normal Authorization: Bearer header every other call uses. Mint a short-lived,
      // single-use ticket bound to this task via a normal authenticated fetch() first, then pass
      // it as a query parameter instead. See apiKey.js's file-level comment for why this is a
      // deliberate, narrow exception rather than a precedent for other endpoints.
      const ticketRes = await authenticatedFetch(`${API_BASE}/tasks/${id}/stream-ticket`, { method: 'POST' })
      if (!ticketRes.ok) throw new Error(`Stream ticket request failed: ${ticketRes.status}`)
      const { ticket } = await ticketRes.json()

      const es = new EventSource(`${API_BASE}/tasks/${id}/stream?ticket=${encodeURIComponent(ticket)}`)
      eventSourceRef.current = es

      es.onmessage = (e) => {
        try {
          const data = JSON.parse(e.data)
          const p = data.payload ?? {}

          // P2-M5 — additively fold every event into the live graph state. This
          // is independent of and does not alter the card switch below; the
          // graph reducer ignores events it doesn't care about.
          dispatchGraphEvent(data)

          switch (data.event) {
            case 'task_started':
              setTaskStatus('Running')
              setLiveStatus('Task started. Agents are running.')
              break
            case 'approval_required':
              setTaskStatus('WaitingForApproval')
              setApprovalRequest(p)
              setLiveStatus('Waiting for your approval to run the agents.')
              break
            case 'task_approved':
              setApprovalRequest(null)
              setTaskStatus('Running')
              break
            case 'task_rejected':
              setApprovalRequest(null)
              break
            case 'manager_review_started':
              setManagerReview({ status: 'running' })
              setLiveStatus('Manager review started.')
              break
            case 'manager_review_completed':
              setManagerReview({ status: 'completed', result: p.result })
              setLiveStatus('Manager review completed.')
              break
            case 'agent_started':
              setAgentOrder(prev => prev.includes(p.agentExecutionId) ? prev : [...prev, p.agentExecutionId])
              updateAgent(p.agentExecutionId, {
                agentType: p.agentType,
                status: 'Running',
                messages: [],
                toolCalls: [],
                inputTokens: 0,
                outputTokens: 0,
                costUsd: 0,
              })
              setLiveStatus(`${p.agentType} agent started.`)
              break
            case 'message_written':
              appendAgentMessage(p.agentExecutionId, p.agentType, p.contentPreview)
              break
            case 'tool_started':
              addToolCall(p.agentExecutionId, p.toolName)
              break
            case 'tool_completed':
              completeToolCall(p.agentExecutionId, p.toolName, p.success, p.durationMs, p.outputPreview)
              break
            case 'agent_completed':
              updateAgent(p.agentExecutionId, {
                status: 'Completed',
                inputTokens: p.inputTokens,
                outputTokens: p.outputTokens,
                costUsd: p.costUsd,
              })
              setLiveStatus(`${p.agentType} agent completed.`)
              break
            case 'agent_failed':
              updateAgent(p.agentExecutionId, { status: 'Failed' })
              setLiveStatus(`${p.agentType} agent failed.`)
              break
            case 'task_completed':
              setTaskStatus('Completed')
              setTotalCost(p.totalCostUsd)
              setHandoffTaskId(id)
              setLiveStatus(`Task completed.${p.totalCostUsd != null ? ` Total cost $${Number(p.totalCostUsd).toFixed(4)}.` : ''}`)
              es.close()
              fetchFinalResult(id)
              refreshSavedMemories()
              break
            case 'task_failed':
              setTaskStatus('Failed')
              setApprovalRequest(null)
              setError(p.errorMessage ?? 'Task failed')
              setLiveStatus('Task failed.')
              es.close()
              refreshSavedMemories()
              break
          }
        } catch (_) {}
      }

      es.onerror = () => es.close()
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  const fetchFinalResult = async (id) => {
    try {
      const res = await authenticatedFetch(`${API_BASE}/tasks/${id}?includeToolCalls=true`)
      if (res.ok) {
        const data = await res.json()
        setFinalResult(data.finalResult)
      }
    } catch (_) {}
  }

  const refreshSavedMemories = async () => {
    const current = await fetchMemoryCounts()
    setMemoryCounts(current)
    const deltas = {}
    for (const agentType of Object.keys(current)) {
      const delta = current[agentType] - (memoryBaseline[agentType] ?? 0)
      if (delta > 0) deltas[agentType] = delta
    }
    setSavedMemories(deltas)
  }

  const handleApprove = async () => {
    if (!taskId) return
    setApprovalBusy(true)
    try {
      await authenticatedFetch(`${API_BASE}/tasks/${taskId}/approve`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({}),
      })
    } catch (err) {
      setError(err.message)
    } finally {
      setApprovalBusy(false)
    }
  }

  const handleReject = async (note) => {
    if (!taskId) return
    setApprovalBusy(true)
    try {
      await authenticatedFetch(`${API_BASE}/tasks/${taskId}/reject`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ note: note || null }),
      })
    } catch (err) {
      setError(err.message)
    } finally {
      setApprovalBusy(false)
    }
  }

  useEffect(() => () => eventSourceRef.current?.close(), [])

  const isActive = taskStatus === 'Running' || taskStatus === 'WaitingForApproval'

  if (!keySet) {
    return <ApiKeyPrompt onSubmitted={() => setKeySet(true)} />
  }

  // TopBar's right-hand context slot — the current header's right cluster
  // (task id / status / cost, playground-only) plus the always-present
  // "Change key" control (now a real Button, Task 1's `fullWidth`/`size` API,
  // in place of the old raw <button>). Exact same behavior, relocated.
  const topBarContext = (
    <>
      {taskId && view === 'playground' && (
        <>
          <span style={{ fontSize: 11, color: colors.subtext0 }}>{taskId.slice(0, 8)}…</span>
          <StatusBadge status={taskStatus} borderAlphaSuffix="50" style={{ textTransform: 'none' }} />
          {totalCost != null && (
            <span style={{ fontSize: 11, color: colors.subtext0 }}>
              ${Number(totalCost).toFixed(6)} total
            </span>
          )}
        </>
      )}
      <Button variant="ghost" size="sm" fullWidth={false} onClick={() => { clearApiKey(); setKeySet(false) }}>
        Change key
      </Button>
    </>
  )

  // Per-view content, each wrapped in PageContainer at its approved density
  // width (see task-2-report.md for the rationale per view).
  const routedContent =
    view === 'memories' ? (
      // `paddingBlock` restores the vertical rhythm the pages' own removed
      // outer containers used to provide — PageContainer applies horizontal
      // padding only, so without this the content butts against the TopBar.
      <PageContainer width="wide" style={{ paddingBlock: spacing.xl }}>
        <MemoriesPage onDelete={() => fetchMemoryCounts().then(setMemoryCounts)} />
      </PageContainer>
    ) : view === 'observability' ? (
      <PageContainer width="full" style={{ paddingBlock: spacing.xl }}>
        <ObservabilityPage initialTaskId={handoffTaskId} />
      </PageContainer>
    ) : view === 'evals' ? (
      <PageContainer width="wide" style={{ paddingBlock: spacing.xl }}>
        <EvalsPage />
      </PageContainer>
    ) : (
      // Edge-to-edge workspace: override PageContainer's horizontal padding with
      // the matching longhands (paddingLeft/Right: 0) rather than the `padding`
      // shorthand — mixing the `padding` shorthand with PageContainer's
      // paddingLeft/paddingRight longhands triggers React's "don't mix shorthand
      // and non-shorthand" dev warning. PageContainer sets no vertical padding,
      // so this yields a fully edge-to-edge (0-padding) full-bleed workspace.
      <PageContainer width="full" style={{ paddingLeft: 0, paddingRight: 0 }}>
      <div style={{
        display: 'grid',
        gridTemplateColumns: playgroundStacked ? '1fr' : '380px 1fr',
        // Fill the viewport below the sticky TopBar (56px = its min-height) so
        // the workspace panes + divider reach the bottom even when the form is
        // short. `100%` would collapse against PageContainer's auto height.
        minHeight: 'calc(100svh - 56px)',
      }}>

        {/* Left: Input + Agent Feed */}
        <div style={{
          borderRight: playgroundStacked ? 'none' : `1px solid ${colors.base}`,
          borderBottom: playgroundStacked ? `1px solid ${colors.base}` : 'none',
          // P2-M6.2 — minWidth:0 lets the grid track shrink so intrinsic-width
          // content (wide code/tables) scrolls in-box instead of forcing the
          // track wider than its share and being clipped by main's overflowX.
          padding: 20, overflowY: 'auto', minWidth: 0,
        }}>
          {/* P2-M6.2 — single polite live region for streaming-status
              announcements (always mounted so updates are reliably announced). */}
          <div data-testid="playground-live-status" role="status" aria-live="polite" style={srOnly}>{liveStatus}</div>
          <form onSubmit={handleSubmit} style={{ marginBottom: 24 }}>
            <div style={{ marginBottom: 10 }}>
              <Input
                label="Task Title"
                labelStyle={{ fontWeight: 400 }}
                value={title}
                onChange={e => setTitle(e.target.value)}
                placeholder="e.g. Research LangGraph"
                required
                style={{ padding: '7px 10px', background: colors.base, outline: 'none' }}
              />
            </div>
            <div style={{ marginBottom: 14 }}>
              <TextArea
                label="Prompt"
                labelStyle={{ fontWeight: 400 }}
                value={prompt}
                onChange={e => setPrompt(e.target.value)}
                rows={5}
                placeholder="Describe what you need the agents to do..."
                required
                style={{ padding: '7px 10px', background: colors.base, outline: 'none' }}
              />
            </div>
            <div style={{ marginBottom: 14 }}>
              <Toggle
                options={[
                  { value: false, label: 'Auto-run' },
                  { value: true, label: 'Require approval' },
                ]}
                value={requireApproval}
                onChange={setRequireApproval}
                aria-label="Require human approval before agents run"
              />
            </div>
            <Button
              type="submit"
              variant="primary"
              disabled={submitting || isActive}
              style={{ ...(submitting || isActive ? {} : { color: colors.base }), transition: 'background 0.15s' }}
            >
              {isActive ? '⏳ Running…' : 'Run Agents'}
            </Button>
          </form>

          {error && <ErrorBanner style={{ marginBottom: spacing.lg }}>{error}</ErrorBanner>}

          {approvalRequest && (
            <ApprovalCard
              request={approvalRequest}
              onApprove={handleApprove}
              onReject={handleReject}
              busy={approvalBusy}
            />
          )}

          {(agentOrder.length > 0 || managerReview) && (
            <div>
              <h2 style={{ margin: '0 0 10px', font: 'inherit', letterSpacing: 'inherit' }}><Label style={{ color: colors.surface2, fontWeight: 400 }}>Agent Executions</Label></h2>
              {agentOrder.map(id => agents[id] && (
                <AgentCard
                  key={id}
                  execution={agents[id]}
                  memoryCount={memoryCounts[agents[id].agentType]}
                  savedMemories={savedMemories[agents[id].agentType]}
                />
              ))}
              {managerReview && <ManagerReviewCard review={managerReview} />}
            </div>
          )}
        </div>

        {/* Right: live execution graph (default) / Final Result (List fallback).
            Both read the same underlying state, so toggling never loses state
            and works identically while running and after completion. */}
        <div style={{ padding: 24, overflowY: 'auto', display: 'flex', flexDirection: 'column', minWidth: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md, marginBottom: 16 }}>
            {/* Observability handoff — only after the run completes (§13). */}
            <div>
              {handoffTaskId && (
                <Button variant="ghost" size="sm" fullWidth={false} onClick={() => navigate('observability')}>
                  View full trace in Observability →
                </Button>
              )}
            </div>
            <Toggle
              options={[{ value: 'graph', label: 'Graph' }, { value: 'list', label: 'List' }]}
              value={playgroundViewMode}
              onChange={setPlaygroundViewMode}
              aria-label="Switch between the live execution graph and the result view"
            />
          </div>

          <div style={{ flex: 1, minHeight: 0 }}>
          {playgroundViewMode === 'graph' ? (
            <PlaygroundExecutionGraph spans={liveSpans} />
          ) : finalResult ? (
            <div>
              <h2 style={{ margin: '0 0 16px', font: 'inherit', letterSpacing: 'inherit' }}><Label style={{ color: colors.surface2, fontWeight: 400 }}>Final Result</Label></h2>
              <Panel style={{ padding: '20px 24px' }}>
                <div style={{ fontSize: 13, lineHeight: 1.7, color: colors.text }}>
                  <ReactMarkdown
                    remarkPlugins={[remarkGfm]}
                    components={{
                      // P2-M6.2 — offset model-output headings down one level so
                      // they never emit an <h1> that competes with the per-view
                      // page <h1> (one-h1-per-view invariant). Visual sizes kept.
                      h1: ({ children }) => <h2 style={{ color: colors.traceBlue, fontSize: 20, marginTop: 0 }}>{children}</h2>,
                      h2: ({ children }) => <h3 style={{ color: colors.traceBlue, fontSize: 17 }}>{children}</h3>,
                      h3: ({ children }) => <h4 style={{ color: colors.agentSky, fontSize: 15 }}>{children}</h4>,
                      // P2-M6.2 — react-markdown v10 no longer passes an `inline`
                      // flag to `code`; detect a fenced block via its language
                      // class / multiline content instead (the proven pattern from
                      // reviewMarkdownComponents). Inline backticks stay inline
                      // <code>; the block wrapper is the separate `pre` handler,
                      // so no <pre> is ever emitted inside a <p>.
                      pre: ({ children }) => <pre style={{ background: colors.mantle, borderRadius: 6, padding: '12px 16px', overflow: 'auto' }}>{children}</pre>,
                      code: ({ className, children }) => {
                        const isBlock = /language-/.test(className || '') || String(children).includes('\n')
                        return isBlock
                          ? <code style={{ fontSize: 12, fontFamily: dataFontFamily }}>{children}</code>
                          : <code style={{ background: colors.surface0, borderRadius: 3, padding: '1px 5px', fontSize: 12, fontFamily: dataFontFamily }}>{children}</code>
                      },
                      a: ({ href, children }) => <a href={href} style={{ color: colors.traceBlue }} target="_blank" rel="noreferrer">{children}</a>,
                      p: ({ children }) => <p style={{ margin: '0 0 12px' }}>{children}</p>,
                      ul: ({ children }) => <ul style={{ paddingLeft: 20, margin: '0 0 12px' }}>{children}</ul>,
                      li: ({ children }) => <li style={{ marginBottom: 4 }}>{children}</li>,
                      // P2-M6.2 — wide GFM tables scroll inside their own box so
                      // the page never overflows horizontally on narrow viewports.
                      table: ({ children }) => (
                        <div style={{ overflowX: 'auto' }}>
                          <table style={{ borderCollapse: 'collapse', width: '100%' }}>{children}</table>
                        </div>
                      ),
                    }}
                  >
                    {finalResult}
                  </ReactMarkdown>
                </div>
              </Panel>
            </div>
          ) : (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
              {isActive ? (
                <Spinner label="Waiting for agents to complete…" />
              ) : (
                <EmptyState
                  title="No results yet"
                  description="Fill in a task on the left and click Run Agents. Per-agent cards will stream in as they work, and the final result renders here when they finish. Once you've run a task or two: Observability has the cost and timeline breakdown for every run, and Evals runs scored test suites against known-good outputs."
                />
              )}
            </div>
          )}
          </div>
        </div>
      </div>
      </PageContainer>
    )

  return (
    <AppShell
      sidebar={<Sidebar items={NAV_ITEMS} active={view} onNavigate={navigate} brand={<Brand />} />}
      topBar={<TopBar title={VIEW_TITLES[view]}>{topBarContext}</TopBar>}
    >
      {routedContent}
    </AppShell>
  )
}
