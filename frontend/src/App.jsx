import { useState, useEffect, useRef } from 'react'
import ReactMarkdown from 'react-markdown'
import './App.css'

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`
const DEV_USER_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

const STATUS_COLORS = {
  Pending: '#6b7280',
  Running: '#2563eb',
  Completed: '#16a34a',
  Failed: '#dc2626',
}

function ToolCallRow({ toolCall }) {
  const isRunning = toolCall.durationMs == null
  const color = isRunning ? '#f59e0b' : (toolCall.success ? '#16a34a' : '#dc2626')
  const label = isRunning ? '⏳' : (toolCall.success ? '✓' : '✗')
  const duration = toolCall.durationMs != null ? ` · ${toolCall.durationMs}ms` : ''

  return (
    <div style={{
      display: 'flex',
      alignItems: 'flex-start',
      gap: 8,
      padding: '5px 8px',
      background: '#181825',
      borderLeft: `3px solid ${color}`,
      borderRadius: '0 4px 4px 0',
      marginTop: 4,
      fontSize: 12,
    }}>
      <span style={{ color, flexShrink: 0, minWidth: 14 }}>{label}</span>
      <div style={{ flex: 1, minWidth: 0 }}>
        <span style={{ color: '#89b4fa', fontWeight: 600 }}>{toolCall.name}</span>
        <span style={{ color: '#585b70' }}>{duration}</span>
        {toolCall.output && (
          <div style={{ color: '#a6adc8', marginTop: 2, wordBreak: 'break-word' }}>
            {toolCall.output.length > 120 ? toolCall.output.slice(0, 120) + '…' : toolCall.output}
          </div>
        )}
        {toolCall.error && (
          <div style={{ color: '#f38ba8', marginTop: 2 }}>{toolCall.error}</div>
        )}
      </div>
    </div>
  )
}

function AgentCard({ execution }) {
  const statusColor = STATUS_COLORS[execution.status] ?? '#6b7280'

  return (
    <div style={{
      border: `1px solid ${statusColor}40`,
      borderLeft: `3px solid ${statusColor}`,
      borderRadius: 8,
      padding: '12px 14px',
      marginBottom: 10,
      background: '#1e1e2e',
    }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
        <strong style={{ color: '#cdd6f4', fontSize: 14 }}>{execution.agentType}</strong>
        <span style={{
          background: `${statusColor}20`,
          color: statusColor,
          border: `1px solid ${statusColor}60`,
          borderRadius: 4,
          padding: '1px 8px',
          fontSize: 11,
          fontWeight: 700,
          letterSpacing: '0.05em',
        }}>{execution.status.toUpperCase()}</span>
      </div>

      {execution.messages?.map((msg, i) => (
        <div key={i} style={{
          background: '#181825',
          borderRadius: 4,
          padding: '6px 10px',
          marginTop: 4,
          fontSize: 12,
          color: '#a6adc8',
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
        <div style={{ fontSize: 11, color: '#585b70', marginTop: 8, display: 'flex', gap: 12 }}>
          <span>{execution.inputTokens}in + {execution.outputTokens}out tokens</span>
          <span style={{ color: '#6c7086' }}>${Number(execution.costUsd).toFixed(6)}</span>
        </div>
      )}
    </div>
  )
}

export default function App() {
  const [title, setTitle] = useState('')
  const [prompt, setPrompt] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [taskId, setTaskId] = useState(null)
  const [taskStatus, setTaskStatus] = useState(null)
  const [agents, setAgents] = useState({})
  const [agentOrder, setAgentOrder] = useState([])
  const [finalResult, setFinalResult] = useState(null)
  const [totalCost, setTotalCost] = useState(null)
  const [error, setError] = useState(null)
  const eventSourceRef = useRef(null)

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
    eventSourceRef.current?.close()

    try {
      const createRes = await fetch(`${API_BASE}/tasks`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId: DEV_USER_ID, title, userPrompt: prompt }),
      })
      if (!createRes.ok) throw new Error(`Create failed: ${createRes.status}`)
      const created = await createRes.json()
      const id = created.id
      setTaskId(id)
      setTaskStatus('Pending')

      await fetch(`${API_BASE}/tasks/${id}/start`, { method: 'POST' })
      setTaskStatus('Running')

      const es = new EventSource(`${API_BASE}/tasks/${id}/stream`)
      eventSourceRef.current = es

      es.onmessage = (e) => {
        try {
          const data = JSON.parse(e.data)
          const p = data.payload ?? {}

          switch (data.event) {
            case 'task_started':
              setTaskStatus('Running')
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
              break
            case 'task_failed':
              setTaskStatus('Failed')
              setError(p.errorMessage ?? 'Task failed')
              es.close()
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
      const res = await fetch(`${API_BASE}/tasks/${id}?includeToolCalls=true`)
      if (res.ok) {
        const data = await res.json()
        setFinalResult(data.finalResult)
      }
    } catch (_) {}
  }

  useEffect(() => () => eventSourceRef.current?.close(), [])

  const isRunning = taskStatus === 'Running'
  const statusColor = STATUS_COLORS[taskStatus] ?? '#6b7280'

  return (
    <div style={{ minHeight: '100vh', background: '#11111b', color: '#cdd6f4', fontFamily: '"JetBrains Mono", "Fira Code", monospace', fontSize: 14 }}>

      {/* Header */}
      <div style={{ borderBottom: '1px solid #1e1e2e', padding: '14px 24px', display: 'flex', alignItems: 'center', gap: 16 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 700, color: '#89b4fa', margin: 0 }}>OrchestAI</h1>
          <p style={{ color: '#585b70', margin: 0, fontSize: 11 }}>Multi-agent CQRS orchestration · .NET 8</p>
        </div>
        {taskId && (
          <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ fontSize: 11, color: '#585b70' }}>{taskId.slice(0, 8)}…</span>
            <span style={{
              background: `${statusColor}18`,
              color: statusColor,
              border: `1px solid ${statusColor}50`,
              borderRadius: 4,
              padding: '2px 10px',
              fontSize: 11,
              fontWeight: 700,
              letterSpacing: '0.06em',
            }}>{taskStatus}</span>
            {totalCost != null && (
              <span style={{ fontSize: 11, color: '#6c7086' }}>
                ${Number(totalCost).toFixed(6)} total
              </span>
            )}
          </div>
        )}
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '380px 1fr', minHeight: 'calc(100vh - 57px)' }}>

        {/* Left: Input + Agent Feed */}
        <div style={{ borderRight: '1px solid #1e1e2e', padding: 20, overflowY: 'auto' }}>
          <form onSubmit={handleSubmit} style={{ marginBottom: 24 }}>
            <div style={{ marginBottom: 10 }}>
              <label style={{ display: 'block', marginBottom: 4, fontSize: 11, color: '#6c7086', textTransform: 'uppercase', letterSpacing: '0.07em' }}>Task Title</label>
              <input
                value={title}
                onChange={e => setTitle(e.target.value)}
                placeholder="e.g. Research LangGraph"
                required
                style={{ width: '100%', padding: '7px 10px', borderRadius: 6, border: '1px solid #313244', background: '#1e1e2e', color: '#cdd6f4', fontSize: 13, boxSizing: 'border-box', outline: 'none' }}
              />
            </div>
            <div style={{ marginBottom: 14 }}>
              <label style={{ display: 'block', marginBottom: 4, fontSize: 11, color: '#6c7086', textTransform: 'uppercase', letterSpacing: '0.07em' }}>Prompt</label>
              <textarea
                value={prompt}
                onChange={e => setPrompt(e.target.value)}
                rows={5}
                placeholder="Describe what you need the agents to do..."
                required
                style={{ width: '100%', padding: '7px 10px', borderRadius: 6, border: '1px solid #313244', background: '#1e1e2e', color: '#cdd6f4', fontSize: 13, resize: 'vertical', boxSizing: 'border-box', outline: 'none' }}
              />
            </div>
            <button
              type="submit"
              disabled={submitting || isRunning}
              style={{
                width: '100%', padding: '9px 0', borderRadius: 6, border: 'none',
                background: submitting || isRunning ? '#313244' : '#89b4fa',
                color: submitting || isRunning ? '#6c7086' : '#1e1e2e',
                fontWeight: 700, fontSize: 13, cursor: submitting || isRunning ? 'not-allowed' : 'pointer',
                transition: 'background 0.15s',
              }}
            >
              {isRunning ? '⏳ Running…' : 'Run Agents'}
            </button>
          </form>

          {error && (
            <div style={{ background: '#2d1b1b', border: '1px solid #dc262680', borderRadius: 6, padding: '10px 12px', marginBottom: 16, color: '#f38ba8', fontSize: 12 }}>
              {error}
            </div>
          )}

          {agentOrder.length > 0 && (
            <div>
              <div style={{ fontSize: 11, color: '#585b70', textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: 10 }}>Agent Executions</div>
              {agentOrder.map(id => agents[id] && (
                <AgentCard key={id} execution={agents[id]} />
              ))}
            </div>
          )}
        </div>

        {/* Right: Final Result */}
        <div style={{ padding: 24, overflowY: 'auto' }}>
          {finalResult ? (
            <div>
              <div style={{ fontSize: 11, color: '#585b70', textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: 16 }}>Final Result</div>
              <div style={{
                background: '#1e1e2e',
                border: '1px solid #313244',
                borderRadius: 8,
                padding: '20px 24px',
                fontSize: 13,
                lineHeight: 1.7,
                color: '#cdd6f4',
              }}>
                <ReactMarkdown
                  components={{
                    h1: ({ children }) => <h1 style={{ color: '#89b4fa', fontSize: 20, marginTop: 0 }}>{children}</h1>,
                    h2: ({ children }) => <h2 style={{ color: '#89b4fa', fontSize: 17 }}>{children}</h2>,
                    h3: ({ children }) => <h3 style={{ color: '#89dceb', fontSize: 15 }}>{children}</h3>,
                    code: ({ inline, children }) => inline
                      ? <code style={{ background: '#313244', borderRadius: 3, padding: '1px 5px', fontSize: 12 }}>{children}</code>
                      : <pre style={{ background: '#181825', borderRadius: 6, padding: '12px 16px', overflow: 'auto' }}><code style={{ fontSize: 12 }}>{children}</code></pre>,
                    a: ({ href, children }) => <a href={href} style={{ color: '#89b4fa' }} target="_blank" rel="noreferrer">{children}</a>,
                    p: ({ children }) => <p style={{ margin: '0 0 12px' }}>{children}</p>,
                    ul: ({ children }) => <ul style={{ paddingLeft: 20, margin: '0 0 12px' }}>{children}</ul>,
                    li: ({ children }) => <li style={{ marginBottom: 4 }}>{children}</li>,
                  }}
                >
                  {finalResult}
                </ReactMarkdown>
              </div>
            </div>
          ) : (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%', color: '#313244', fontSize: 13 }}>
              {isRunning ? 'Waiting for agents to complete…' : 'Submit a task to see results here.'}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
