import { useState, useEffect, useRef } from 'react'
import ReactMarkdown from 'react-markdown'
import ObservabilityPage from './ObservabilityPage'
import EvalsPage from './EvalsPage'
import { hasApiKey, authenticatedFetch } from './apiKey'
import ApiKeyPrompt from './ApiKeyPrompt'
import { colors, radii, spacing } from './theme/tokens'
import { StatusBadge } from './components/StatusBadge'
import { Panel } from './components/Panel'
import { Button } from './components/Button'
import { Input, TextArea } from './components/Input'
import { Nav, NavItem } from './components/NavItem'
import { StateText } from './components/StateText'
import { Label } from './components/Label'
import './App.css'

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`
const DEV_USER_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

function ToolCallRow({ toolCall }) {
  const isRunning = toolCall.durationMs == null
  const color = isRunning ? colors.statusWarning : (toolCall.success ? colors.statusCompleted : colors.statusFailed)
  const label = isRunning ? '⏳' : (toolCall.success ? '✓' : '✗')
  const duration = toolCall.durationMs != null ? ` · ${toolCall.durationMs}ms` : ''

  return (
    <div style={{
      display: 'flex',
      alignItems: 'flex-start',
      gap: 8,
      padding: '5px 8px',
      background: colors.mantle,
      borderLeft: `3px solid ${color}`,
      borderRadius: `0 ${radii.md} ${radii.md} 0`,
      marginTop: 4,
      fontSize: 12,
    }}>
      <span style={{ color, flexShrink: 0, minWidth: 14 }}>{label}</span>
      <div style={{ flex: 1, minWidth: 0 }}>
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
            <strong style={{ color: colors.text, fontSize: 14 }}>{execution.agentType}</strong>
            {memoryCount > 0 && (
              <span title={`${memoryCount} saved ${memoryCount === 1 ? 'memory' : 'memories'} for this agent`} style={{ fontSize: 11, color: colors.subtext0 }}>
                🧠 {memoryCount}
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
          <div style={{ fontSize: 11, color: colors.surface2, marginTop: 8, display: 'flex', gap: 12 }}>
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

function ApprovalCard({ request, onApprove, onReject, busy }) {
  const [showRejectInput, setShowRejectInput] = useState(false)
  const [note, setNote] = useState('')

  return (
    <div style={{
      border: '1px solid #f59e0b60',
      borderLeft: '3px solid #f59e0b',
      borderRadius: radii.xl,
      padding: '14px 16px',
      marginBottom: 14,
      background: colors.base,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
        <span>⏸</span>
        <strong style={{ color: colors.statusWarning, fontSize: 13 }}>Waiting for Your Approval</strong>
      </div>
      <div style={{ fontSize: 12, color: colors.text, marginBottom: 8, lineHeight: 1.5 }}>{request.plan}</div>
      <div style={{ fontSize: 11, color: colors.subtext0, marginBottom: 12 }}>
        <div>Agents selected: {request.selectedAgents?.join(', ')}</div>
        <div>Mode: {request.executionMode}</div>
      </div>

      {!showRejectInput ? (
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            onClick={onApprove}
            disabled={busy}
            style={{
              flex: 1, padding: '7px 0', borderRadius: 6, border: 'none',
              background: colors.statusCompleted, color: colors.crust, fontWeight: 700, fontSize: 12,
              cursor: busy ? 'not-allowed' : 'pointer',
            }}
          >
            Approve ✓
          </button>
          <button
            onClick={() => setShowRejectInput(true)}
            disabled={busy}
            style={{
              flex: 1, padding: '7px 0', borderRadius: 6, border: '1px solid #dc262680',
              background: 'transparent', color: colors.alertRed, fontWeight: 700, fontSize: 12,
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
            rows={2}
            style={{ padding: '6px 8px', fontSize: 12, marginBottom: 8 }}
          />
          <div style={{ display: 'flex', gap: 8 }}>
            <button
              onClick={() => onReject(note)}
              disabled={busy}
              style={{
                flex: 1, padding: '7px 0', borderRadius: 6, border: 'none',
                background: colors.statusFailed, color: colors.crust, fontWeight: 700, fontSize: 12,
                cursor: busy ? 'not-allowed' : 'pointer',
              }}
            >
              Confirm Reject
            </button>
            <button
              onClick={() => setShowRejectInput(false)}
              disabled={busy}
              style={{
                flex: 1, padding: '7px 0', borderRadius: 6, border: '1px solid #313244',
                background: 'transparent', color: colors.subtext0, fontWeight: 700, fontSize: 12,
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

function ManagerReviewCard({ review }) {
  const isRunning = review.status === 'running'
  // Panel's accentStatus prop only understands execution-status keys
  // (Pending/Running/WaitingForApproval/Completed/Failed/Skipped). This
  // card's own running/completed states are colored orange/green — which
  // happen to be exactly Panel's WaitingForApproval/Completed accent colors
  // — so those keys are reused here purely for their color values, with
  // StatusBadge's `label` override supplying the correct RUNNING/COMPLETED
  // display text instead of WAITINGFORAPPROVAL/COMPLETED.
  const accentStatus = isRunning ? 'WaitingForApproval' : 'Completed'

  return (
    <div style={{ marginBottom: 10 }}>
      <Panel accentStatus={accentStatus} style={{ padding: '12px 14px' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <strong style={{ color: colors.text, fontSize: 14 }}>🎯 Manager Review</strong>
          <StatusBadge status={accentStatus} label={isRunning ? 'Running' : 'Completed'} borderAlphaSuffix="60" style={{ padding: '1px 8px', letterSpacing: '0.05em' }} />
        </div>
        <div style={{ fontSize: 12, color: colors.subtext0, marginTop: 8 }}>
          {isRunning
            ? 'Synthesizing and quality-checking all agent outputs…'
            : 'Synthesized and quality-checked all agent outputs into the final result.'}
        </div>
      </Panel>
    </div>
  )
}

function MemoriesPage({ onDelete }) {
  const [memories, setMemories] = useState(null)
  const [error, setError] = useState(null)

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

  const handleDelete = async (id) => {
    try {
      await authenticatedFetch(`${API_BASE}/users/${DEV_USER_ID}/memories/${id}`, { method: 'DELETE' })
      setMemories(prev => prev.filter(m => m.id !== id))
      onDelete?.()
    } catch (err) {
      setError(err.message)
    }
  }

  return (
    <div style={{ padding: 24 }}>
      <div style={{ marginBottom: 16 }}>
        <Label style={{ color: colors.surface2, fontWeight: 400 }}>Agent Memories</Label>
      </div>

      {error && (
        <div style={{ background: colors.errorBg, border: `1px solid ${colors.statusFailed}80`, borderRadius: radii.lg, padding: '10px 12px', marginBottom: spacing.lg }}>
          <StateText tone="error" style={{ fontSize: 12 }}>{error}</StateText>
        </div>
      )}

      {memories == null ? (
        <StateText tone="muted">Loading…</StateText>
      ) : memories.length === 0 ? (
        <StateText tone="muted">No memories saved yet — run a task and agents will start remembering things.</StateText>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
          <thead>
            <tr style={{ textAlign: 'left', color: '#6c7086', borderBottom: '1px solid #313244' }}>
              <th style={{ padding: '8px 10px' }}>Agent</th>
              <th style={{ padding: '8px 10px' }}>Key</th>
              <th style={{ padding: '8px 10px' }}>Value</th>
              <th style={{ padding: '8px 10px' }}>Importance</th>
              <th style={{ padding: '8px 10px' }}>Updated</th>
              <th style={{ padding: '8px 10px' }}></th>
            </tr>
          </thead>
          <tbody>
            {memories.map(m => (
              <tr key={m.id} style={{ borderBottom: '1px solid #1e1e2e' }}>
                <td style={{ padding: '8px 10px', color: '#89b4fa' }}>{m.agentType}</td>
                <td style={{ padding: '8px 10px', color: '#cdd6f4' }}>{m.key}</td>
                <td style={{ padding: '8px 10px', color: '#a6adc8', maxWidth: 400 }}>{m.value}</td>
                <td style={{ padding: '8px 10px', color: '#6c7086' }}>{m.importance}</td>
                <td style={{ padding: '8px 10px', color: '#6c7086' }}>{new Date(m.updatedAt).toLocaleString()}</td>
                <td style={{ padding: '8px 10px' }}>
                  <button
                    onClick={() => handleDelete(m.id)}
                    style={{ background: 'transparent', border: '1px solid #dc262680', color: '#f38ba8', borderRadius: 4, padding: '3px 8px', fontSize: 11, cursor: 'pointer' }}
                  >
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
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
  const [view, setView] = useState('playground')
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

          switch (data.event) {
            case 'task_started':
              setTaskStatus('Running')
              break
            case 'approval_required':
              setTaskStatus('WaitingForApproval')
              setApprovalRequest(p)
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
              break
            case 'manager_review_completed':
              setManagerReview({ status: 'completed', result: p.result })
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
              break
            case 'agent_failed':
              updateAgent(p.agentExecutionId, { status: 'Failed' })
              break
            case 'task_completed':
              setTaskStatus('Completed')
              setTotalCost(p.totalCostUsd)
              es.close()
              fetchFinalResult(id)
              refreshSavedMemories()
              break
            case 'task_failed':
              setTaskStatus('Failed')
              setApprovalRequest(null)
              setError(p.errorMessage ?? 'Task failed')
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

  return (
    <div style={{ minHeight: '100vh', background: '#11111b', color: '#cdd6f4', fontFamily: '"JetBrains Mono", "Fira Code", monospace', fontSize: 14 }}>

      {/* Header */}
      <div style={{ borderBottom: '1px solid #1e1e2e', padding: '14px 24px', display: 'flex', alignItems: 'center', gap: 16 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 700, color: '#89b4fa', margin: 0 }}>OrchestAI</h1>
          <p style={{ color: '#585b70', margin: 0, fontSize: 11 }}>Multi-agent CQRS orchestration · .NET 8</p>
        </div>

        <Nav>
          <NavItem active={view === 'playground'} onClick={() => setView('playground')}>
            Playground
          </NavItem>
          <NavItem active={view === 'memories'} onClick={() => setView('memories')}>
            🧠 Memories
          </NavItem>
          <NavItem active={view === 'observability'} onClick={() => setView('observability')}>
            📊 Observability
          </NavItem>
          <NavItem active={view === 'evals'} onClick={() => setView('evals')}>
            🎯 Evals
          </NavItem>
        </Nav>

        {taskId && view === 'playground' && (
          <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ fontSize: 11, color: colors.surface2 }}>{taskId.slice(0, 8)}…</span>
            <StatusBadge status={taskStatus} borderAlphaSuffix="50" style={{ textTransform: 'none' }} />
            {totalCost != null && (
              <span style={{ fontSize: 11, color: colors.overlay0 }}>
                ${Number(totalCost).toFixed(6)} total
              </span>
            )}
          </div>
        )}
      </div>

      {view === 'memories' ? (
        <MemoriesPage onDelete={() => fetchMemoryCounts().then(setMemoryCounts)} />
      ) : view === 'observability' ? (
        <ObservabilityPage />
      ) : view === 'evals' ? (
        <EvalsPage />
      ) : (
      <div style={{ display: 'grid', gridTemplateColumns: '380px 1fr', minHeight: 'calc(100vh - 57px)' }}>

        {/* Left: Input + Agent Feed */}
        <div style={{ borderRight: '1px solid #1e1e2e', padding: 20, overflowY: 'auto' }}>
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
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 14, fontSize: 12, color: colors.subtext0, cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={requireApproval}
                onChange={e => setRequireApproval(e.target.checked)}
              />
              Require human approval before agents run
            </label>
            <Button
              type="submit"
              variant="primary"
              disabled={submitting || isActive}
              style={{ ...(submitting || isActive ? {} : { color: colors.base }), transition: 'background 0.15s' }}
            >
              {isActive ? '⏳ Running…' : 'Run Agents'}
            </Button>
          </form>

          {error && (
            <div style={{ background: colors.errorBg, border: `1px solid ${colors.statusFailed}80`, borderRadius: radii.lg, padding: '10px 12px', marginBottom: spacing.lg }}>
              <StateText tone="error" style={{ fontSize: 12 }}>{error}</StateText>
            </div>
          )}

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
              <div style={{ fontSize: 11, color: '#585b70', textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: 10 }}>Agent Executions</div>
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

        {/* Right: Final Result */}
        <div style={{ padding: 24, overflowY: 'auto' }}>
          {finalResult ? (
            <div>
              <div style={{ fontSize: 11, color: '#585b70', textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: 16 }}>Final Result</div>
              <Panel style={{ padding: '20px 24px' }}>
                <div style={{ fontSize: 13, lineHeight: 1.7, color: colors.text }}>
                  <ReactMarkdown
                    components={{
                      h1: ({ children }) => <h1 style={{ color: colors.traceBlue, fontSize: 20, marginTop: 0 }}>{children}</h1>,
                      h2: ({ children }) => <h2 style={{ color: colors.traceBlue, fontSize: 17 }}>{children}</h2>,
                      h3: ({ children }) => <h3 style={{ color: colors.agentSky, fontSize: 15 }}>{children}</h3>,
                      code: ({ inline, children }) => inline
                        ? <code style={{ background: colors.surface0, borderRadius: 3, padding: '1px 5px', fontSize: 12 }}>{children}</code>
                        : <pre style={{ background: colors.mantle, borderRadius: 6, padding: '12px 16px', overflow: 'auto' }}><code style={{ fontSize: 12 }}>{children}</code></pre>,
                      a: ({ href, children }) => <a href={href} style={{ color: colors.traceBlue }} target="_blank" rel="noreferrer">{children}</a>,
                      p: ({ children }) => <p style={{ margin: '0 0 12px' }}>{children}</p>,
                      ul: ({ children }) => <ul style={{ paddingLeft: 20, margin: '0 0 12px' }}>{children}</ul>,
                      li: ({ children }) => <li style={{ marginBottom: 4 }}>{children}</li>,
                    }}
                  >
                    {finalResult}
                  </ReactMarkdown>
                </div>
              </Panel>
            </div>
          ) : (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
              <StateText tone="muted" style={{ color: colors.surface0 }}>
                {isActive ? 'Waiting for agents to complete…' : 'Submit a task to see results here.'}
              </StateText>
            </div>
          )}
        </div>
      </div>
      )}
    </div>
  )
}
