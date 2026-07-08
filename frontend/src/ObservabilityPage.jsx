import { useState, useEffect, useMemo } from 'react'

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`
const DEV_USER_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

const AGENT_COLORS = {
  Orchestrator: '#f9e2af',
  Research: '#89b4fa',
  Writer: '#a6e3a1',
  Code: '#cba6f7',
  Data: '#89dceb',
  Browser: '#fab387',
}

const STATUS_COLORS = {
  Pending: '#6b7280',
  Running: '#2563eb',
  Completed: '#16a34a',
  Failed: '#dc2626',
  Skipped: '#6c7086',
}

function todayIso() {
  return new Date().toISOString().slice(0, 10)
}

function daysAgoIso(days) {
  const d = new Date()
  d.setDate(d.getDate() - days)
  return d.toISOString().slice(0, 10)
}

function fmtCost(v) {
  return `$${Number(v ?? 0).toFixed(6)}`
}

function fmtDuration(ms) {
  if (ms == null) return '—'
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

const panelStyle = {
  background: '#1e1e2e',
  border: '1px solid #313244',
  borderRadius: 8,
  padding: '16px 18px',
}

const labelStyle = {
  fontSize: 11,
  color: '#6c7086',
  textTransform: 'uppercase',
  letterSpacing: '0.07em',
  marginBottom: 8,
}

const selectStyle = {
  padding: '6px 10px',
  borderRadius: 6,
  border: '1px solid #313244',
  background: '#181825',
  color: '#cdd6f4',
  fontSize: 12,
  outline: 'none',
}

function SubNav({ subView, setSubView }) {
  const tabs = [
    ['timeline', 'Timeline'],
    ['summary', 'Summary'],
    ['dashboard', 'Cost Dashboard'],
    ['errors', 'Error Rates'],
    ['compare', 'Compare'],
  ]
  return (
    <div style={{ display: 'flex', gap: 4, marginBottom: 20, borderBottom: '1px solid #1e1e2e', paddingBottom: 12 }}>
      {tabs.map(([key, label]) => (
        <button
          key={key}
          onClick={() => setSubView(key)}
          style={{
            background: subView === key ? '#313244' : 'transparent',
            color: subView === key ? '#cdd6f4' : '#6c7086',
            border: 'none', borderRadius: 6, padding: '6px 14px', fontSize: 12, cursor: 'pointer', fontWeight: subView === key ? 700 : 400,
          }}
        >
          {label}
        </button>
      ))}
    </div>
  )
}

function TaskPicker({ tasks, value, onChange, label = 'Task' }) {
  return (
    <div>
      <div style={labelStyle}>{label}</div>
      <select value={value ?? ''} onChange={e => onChange(e.target.value)} style={{ ...selectStyle, width: '100%' }}>
        <option value="" disabled>Select a task…</option>
        {tasks.map(t => (
          <option key={t.id} value={t.id}>
            {t.title} — {t.status} — {new Date(t.createdAt).toLocaleString()}
          </option>
        ))}
      </select>
    </div>
  )
}

function DateRangePicker({ from, to, setFrom, setTo }) {
  return (
    <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end' }}>
      <div>
        <div style={labelStyle}>From</div>
        <input type="date" value={from} onChange={e => setFrom(e.target.value)} style={selectStyle} />
      </div>
      <div>
        <div style={labelStyle}>To</div>
        <input type="date" value={to} onChange={e => setTo(e.target.value)} style={selectStyle} />
      </div>
    </div>
  )
}

// ── Timeline (Gantt-style nested spans, reconstructed from spanId/parentSpanId) ──

function buildSpanTree(spans) {
  const byId = new Map(spans.map(s => [s.spanId, { ...s, children: [] }]))
  const roots = []
  for (const span of byId.values()) {
    if (span.parentSpanId && byId.has(span.parentSpanId)) {
      byId.get(span.parentSpanId).children.push(span)
    } else {
      roots.push(span)
    }
  }
  const sortByStart = (list) => {
    list.sort((a, b) => new Date(a.startedAt ?? 0) - new Date(b.startedAt ?? 0))
    list.forEach(s => sortByStart(s.children))
  }
  sortByStart(roots)
  return roots
}

function SpanRow({ span, depth, traceStart, traceDurationMs }) {
  const [expanded, setExpanded] = useState(true)
  const color = span.spanType === 'ToolCall' ? '#cba6f7' : (AGENT_COLORS[span.label] ?? '#89b4fa')
  const isFailed = span.status === 'Failed'

  const offsetPct = span.startedAt && traceDurationMs > 0
    ? ((new Date(span.startedAt) - traceStart) / traceDurationMs) * 100
    : 0
  const widthPct = span.durationMs && traceDurationMs > 0
    ? Math.max((span.durationMs / traceDurationMs) * 100, 0.5)
    : 0.5

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '5px 0', paddingLeft: depth * 20 }}>
        <span
          onClick={() => span.children.length > 0 && setExpanded(e => !e)}
          style={{ width: 14, flexShrink: 0, cursor: span.children.length > 0 ? 'pointer' : 'default', color: '#6c7086', fontSize: 10 }}
        >
          {span.children.length > 0 ? (expanded ? '▾' : '▸') : ''}
        </span>
        <span style={{
          width: 10, height: 10, borderRadius: 3, background: color, flexShrink: 0,
          border: isFailed ? '2px solid #f38ba8' : 'none',
        }} />
        <span style={{ fontSize: 12, color: '#cdd6f4', minWidth: 160, fontWeight: 600 }}>
          {span.spanType === 'ToolCall' ? `🔧 ${span.label}` : span.label}
        </span>
        <div style={{ flex: 1, position: 'relative', height: 14, background: '#181825', borderRadius: 3 }}>
          <div style={{
            position: 'absolute', left: `${offsetPct}%`, width: `${widthPct}%`, height: '100%',
            background: isFailed ? '#dc262690' : `${color}90`, borderRadius: 3, minWidth: 2,
          }} />
        </div>
        <span style={{ fontSize: 11, color: '#6c7086', width: 70, textAlign: 'right' }}>{fmtDuration(span.durationMs)}</span>
        <span style={{ fontSize: 11, color: '#6c7086', width: 90, textAlign: 'right' }}>
          {span.costUsd != null ? fmtCost(span.costUsd) : ''}
        </span>
        <span style={{
          fontSize: 10, fontWeight: 700, color: STATUS_COLORS[span.status] ?? '#6c7086', width: 70, textAlign: 'right',
        }}>{span.status}</span>
      </div>
      {expanded && span.children.map(child => (
        <SpanRow key={child.spanId} span={child} depth={depth + 1} traceStart={traceStart} traceDurationMs={traceDurationMs} />
      ))}
    </div>
  )
}

function TimelineView({ tasks }) {
  const [taskId, setTaskId] = useState(null)
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!taskId) return
    setData(null)
    setError(null)
    fetch(`${API_BASE}/tasks/${taskId}/timeline`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [taskId])

  const tree = useMemo(() => data ? buildSpanTree(data.spans) : [], [data])
  const { traceStart, traceDurationMs } = useMemo(() => {
    if (!data || data.spans.length === 0) return { traceStart: new Date(), traceDurationMs: 0 }
    const starts = data.spans.filter(s => s.startedAt).map(s => new Date(s.startedAt))
    const ends = data.spans.filter(s => s.completedAt).map(s => new Date(s.completedAt))
    const start = new Date(Math.min(...starts))
    const end = new Date(Math.max(...ends, ...starts))
    return { traceStart: start, traceDurationMs: Math.max(end - start, 1) }
  }, [data])

  return (
    <div>
      <div style={{ marginBottom: 16, maxWidth: 480 }}>
        <TaskPicker tasks={tasks} value={taskId} onChange={setTaskId} />
      </div>
      {error && <div style={{ color: '#f38ba8', fontSize: 12 }}>{error}</div>}
      {!taskId && <div style={{ color: '#585b70', fontSize: 13 }}>Select a task to view its execution timeline.</div>}
      {data && (
        <div style={panelStyle}>
          <div style={{ fontSize: 11, color: '#585b70', marginBottom: 12 }}>
            Trace <span style={{ color: '#89b4fa', fontFamily: 'monospace' }}>{data.traceId}</span> · {data.spans.length} spans
          </div>
          {tree.map(root => (
            <SpanRow key={root.spanId} span={root} depth={0} traceStart={traceStart} traceDurationMs={traceDurationMs} />
          ))}
        </div>
      )}
    </div>
  )
}

// ── Execution Summary Card ──

function SummaryStat({ label, value, color }) {
  return (
    <div>
      <div style={{ fontSize: 10, color: '#6c7086', textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 4 }}>{label}</div>
      <div style={{ fontSize: 18, fontWeight: 700, color: color ?? '#cdd6f4' }}>{value}</div>
    </div>
  )
}

function SummaryView({ tasks }) {
  const [taskId, setTaskId] = useState(null)
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!taskId) return
    setData(null)
    setError(null)
    fetch(`${API_BASE}/tasks/${taskId}/summary`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [taskId])

  return (
    <div>
      <div style={{ marginBottom: 16, maxWidth: 480 }}>
        <TaskPicker tasks={tasks} value={taskId} onChange={setTaskId} />
      </div>
      {error && <div style={{ color: '#f38ba8', fontSize: 12 }}>{error}</div>}
      {!taskId && <div style={{ color: '#585b70', fontSize: 13 }}>Select a task to view its summary card.</div>}
      {data && (
        <div style={panelStyle}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
            <span style={{
              background: `${STATUS_COLORS[data.status] ?? '#6c7086'}20`, color: STATUS_COLORS[data.status] ?? '#6c7086',
              border: `1px solid ${STATUS_COLORS[data.status] ?? '#6c7086'}60`, borderRadius: 4, padding: '2px 10px',
              fontSize: 12, fontWeight: 700, letterSpacing: '0.05em',
            }}>{data.status.toUpperCase()}</span>
            {data.checkpointRestored && (
              <span style={{ fontSize: 11, color: '#f9e2af' }}>⏮ Resumed from checkpoint</span>
            )}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 20, marginBottom: 20 }}>
            <SummaryStat label="Duration" value={data.durationSeconds != null ? `${data.durationSeconds.toFixed(1)}s` : '—'} />
            <SummaryStat label="Total Cost" value={fmtCost(data.totalCostUsd)} color="#a6e3a1" />
            <SummaryStat label="Tokens" value={`${data.totalInputTokens.toLocaleString()} / ${data.totalOutputTokens.toLocaleString()}`} />
            <SummaryStat label="Tool Calls" value={data.toolCallCount} />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 20, marginBottom: 20 }}>
            <SummaryStat label="Retry Count" value={data.retryCount} color={data.retryCount > 0 ? '#f59e0b' : undefined} />
            <SummaryStat label="Error Count" value={data.errorCount} color={data.errorCount > 0 ? '#f38ba8' : undefined} />
            <SummaryStat label="Memory Used" value={data.memoryUsed ? 'Yes' : 'No'} color={data.memoryUsed ? '#a6e3a1' : undefined} />
            <SummaryStat label="Checkpoint Restored" value={data.checkpointRestored ? 'Yes' : 'No'} />
          </div>

          <div style={{ borderTop: '1px solid #313244', paddingTop: 14 }}>
            <div style={labelStyle}>Agents Involved</div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 14 }}>
              {data.agentsInvolved.map(a => (
                <span key={a} style={{
                  fontSize: 11, color: AGENT_COLORS[a] ?? '#cdd6f4', border: `1px solid ${AGENT_COLORS[a] ?? '#585b70'}60`,
                  borderRadius: 4, padding: '2px 8px',
                }}>{a}</span>
              ))}
            </div>
            <div style={labelStyle}>Providers / Models</div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {data.providersAndModels.map(m => (
                <span key={m} style={{ fontSize: 11, color: '#a6adc8', fontFamily: 'monospace' }}>{m}</span>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

// ── Cost Dashboard ──

function BarChart({ entries, keyFn, valueFn, colorFn }) {
  const grouped = useMemo(() => {
    const map = new Map()
    for (const e of entries) {
      const k = keyFn(e)
      map.set(k, (map.get(k) ?? 0) + valueFn(e))
    }
    return [...map.entries()].sort((a, b) => a[0].localeCompare(b[0]))
  }, [entries])

  const max = Math.max(...grouped.map(([, v]) => v), 0.000001)

  if (grouped.length === 0) return <div style={{ color: '#585b70', fontSize: 12 }}>No data in this range.</div>

  return (
    <div style={{ display: 'flex', alignItems: 'flex-end', gap: 6, height: 140, padding: '8px 0' }}>
      {grouped.map(([key, value]) => (
        <div key={key} title={`${key}: ${fmtCost(value)}`} style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
          <div style={{
            width: '100%', maxWidth: 28, height: `${(value / max) * 110}px`, minHeight: 2,
            background: colorFn ? colorFn(key) : '#89b4fa', borderRadius: '3px 3px 0 0',
          }} />
          <div style={{ fontSize: 9, color: '#6c7086', writingMode: 'vertical-rl', textOrientation: 'mixed', height: 40 }}>
            {key.slice(5)}
          </div>
        </div>
      ))}
    </div>
  )
}

function DashboardView() {
  const [from, setFrom] = useState(daysAgoIso(30))
  const [to, setTo] = useState(todayIso())
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    setError(null)
    fetch(`${API_BASE}/users/${DEV_USER_ID}/observability/cost-dashboard?from=${from}&to=${to}`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [from, to])

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <DateRangePicker from={from} to={to} setFrom={setFrom} setTo={setTo} />
      </div>
      {error && <div style={{ color: '#f38ba8', fontSize: 12 }}>{error}</div>}
      {data && (
        <>
          <div style={{ display: 'flex', gap: 20, marginBottom: 20 }}>
            <div style={panelStyle}>
              <SummaryStat label="Total Cost" value={fmtCost(data.totalCostUsd)} color="#a6e3a1" />
            </div>
            <div style={panelStyle}>
              <SummaryStat label="Total Executions" value={data.totalExecutions} />
            </div>
          </div>

          <div style={{ ...panelStyle, marginBottom: 20 }}>
            <div style={labelStyle}>Cost Over Time</div>
            <BarChart entries={data.breakdown} keyFn={b => b.date} valueFn={b => b.costUsd} />
          </div>

          <div style={panelStyle}>
            <div style={labelStyle}>Breakdown by Agent / Model</div>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
              <thead>
                <tr style={{ textAlign: 'left', color: '#6c7086', borderBottom: '1px solid #313244' }}>
                  <th style={{ padding: '6px 8px' }}>Date</th>
                  <th style={{ padding: '6px 8px' }}>Agent</th>
                  <th style={{ padding: '6px 8px' }}>Model</th>
                  <th style={{ padding: '6px 8px', textAlign: 'right' }}>Tokens</th>
                  <th style={{ padding: '6px 8px', textAlign: 'right' }}>Cost</th>
                  <th style={{ padding: '6px 8px', textAlign: 'right' }}>Executions</th>
                  <th style={{ padding: '6px 8px' }}></th>
                </tr>
              </thead>
              <tbody>
                {data.breakdown.map((b, i) => (
                  <tr key={i} style={{ borderBottom: '1px solid #181825' }}>
                    <td style={{ padding: '6px 8px', color: '#a6adc8' }}>{b.date}</td>
                    <td style={{ padding: '6px 8px', color: AGENT_COLORS[b.agentType] ?? '#cdd6f4' }}>{b.agentType}</td>
                    <td style={{ padding: '6px 8px', color: '#6c7086', fontFamily: 'monospace', fontSize: 11 }}>{b.model}</td>
                    <td style={{ padding: '6px 8px', textAlign: 'right', color: '#a6adc8' }}>{b.inputTokens + b.outputTokens}</td>
                    <td style={{ padding: '6px 8px', textAlign: 'right', color: '#a6e3a1' }}>{fmtCost(b.costUsd)}</td>
                    <td style={{ padding: '6px 8px', textAlign: 'right', color: '#a6adc8' }}>{b.executionCount}</td>
                    <td style={{ padding: '6px 8px' }}>
                      {b.isLive && <span style={{ fontSize: 9, color: '#f59e0b' }}>● LIVE</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  )
}

// ── Error Rate Monitoring ──

function ErrorRateTable({ title, rows, nameKey }) {
  return (
    <div style={panelStyle}>
      <div style={labelStyle}>{title}</div>
      {rows.length === 0 ? (
        <div style={{ color: '#585b70', fontSize: 12 }}>No data in this range.</div>
      ) : (
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
          <thead>
            <tr style={{ textAlign: 'left', color: '#6c7086', borderBottom: '1px solid #313244' }}>
              <th style={{ padding: '6px 8px' }}>{nameKey === 'agentType' ? 'Agent' : 'Tool'}</th>
              <th style={{ padding: '6px 8px', textAlign: 'right' }}>Total</th>
              <th style={{ padding: '6px 8px', textAlign: 'right' }}>Failed</th>
              <th style={{ padding: '6px 8px', textAlign: 'right' }}>Failure Rate</th>
              {nameKey === 'agentType' && <th style={{ padding: '6px 8px', textAlign: 'right' }}>Retries</th>}
              <th style={{ padding: '6px 8px' }}>Failure Categories</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => {
              const failColor = r.failureRate > 0.2 ? '#f38ba8' : r.failureRate > 0 ? '#f59e0b' : '#a6e3a1'
              return (
                <tr key={i} style={{ borderBottom: '1px solid #181825' }}>
                  <td style={{ padding: '6px 8px', color: '#cdd6f4', fontWeight: 600 }}>{r[nameKey]}</td>
                  <td style={{ padding: '6px 8px', textAlign: 'right', color: '#a6adc8' }}>{r.totalExecutions ?? r.totalCalls}</td>
                  <td style={{ padding: '6px 8px', textAlign: 'right', color: '#a6adc8' }}>{r.failedExecutions ?? r.failedCalls}</td>
                  <td style={{ padding: '6px 8px', textAlign: 'right', color: failColor, fontWeight: 700 }}>
                    {(r.failureRate * 100).toFixed(1)}%
                  </td>
                  {nameKey === 'agentType' && <td style={{ padding: '6px 8px', textAlign: 'right', color: r.retryCount > 0 ? '#f59e0b' : '#6c7086' }}>{r.retryCount}</td>}
                  <td style={{ padding: '6px 8px', color: '#6c7086', fontSize: 11 }}>
                    {Object.entries(r.failuresByCategory).map(([cat, count]) => `${cat}: ${count}`).join(', ') || '—'}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}

function ErrorRatesView() {
  const [from, setFrom] = useState(daysAgoIso(30))
  const [to, setTo] = useState(todayIso())
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    setError(null)
    fetch(`${API_BASE}/users/${DEV_USER_ID}/observability/error-rates?from=${from}&to=${to}`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [from, to])

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <DateRangePicker from={from} to={to} setFrom={setFrom} setTo={setTo} />
      </div>
      {error && <div style={{ color: '#f38ba8', fontSize: 12 }}>{error}</div>}
      {data && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
          <ErrorRateTable title="Agent Error Rates" rows={data.agentErrorRates} nameKey="agentType" />
          <ErrorRateTable title="Tool Error Rates" rows={data.toolErrorRates} nameKey="toolName" />
        </div>
      )}
    </div>
  )
}

// ── Comparison ──

function ComparisonSide({ side }) {
  if (!side) return <div style={{ ...panelStyle, flex: 1, color: '#585b70', fontSize: 12 }}>Select a task.</div>
  return (
    <div style={{ ...panelStyle, flex: 1 }}>
      <div style={{ fontSize: 13, color: '#cdd6f4', fontWeight: 700, marginBottom: 4 }}>{side.userPrompt}</div>
      <span style={{
        background: `${STATUS_COLORS[side.status] ?? '#6c7086'}20`, color: STATUS_COLORS[side.status] ?? '#6c7086',
        border: `1px solid ${STATUS_COLORS[side.status] ?? '#6c7086'}60`, borderRadius: 4, padding: '1px 8px',
        fontSize: 10, fontWeight: 700,
      }}>{side.status.toUpperCase()}</span>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, margin: '14px 0' }}>
        <SummaryStat label="Duration" value={side.durationSeconds != null ? `${side.durationSeconds.toFixed(1)}s` : '—'} />
        <SummaryStat label="Cost" value={fmtCost(side.totalCostUsd)} color="#a6e3a1" />
        <SummaryStat label="Input Tokens" value={side.totalInputTokens.toLocaleString()} />
        <SummaryStat label="Output Tokens" value={side.totalOutputTokens.toLocaleString()} />
      </div>

      <div style={labelStyle}>Final Result</div>
      <div style={{
        background: '#181825', borderRadius: 6, padding: '10px 12px', fontSize: 12, color: '#a6adc8',
        whiteSpace: 'pre-wrap', maxHeight: 200, overflow: 'auto', marginBottom: 14,
      }}>
        {side.finalResult ?? '—'}
      </div>

      <div style={labelStyle}>Agent Executions</div>
      {side.executions.map((e, i) => (
        <div key={i} style={{ borderLeft: `3px solid ${AGENT_COLORS[e.agentType] ?? '#585b70'}`, padding: '4px 10px', marginBottom: 6 }}>
          <div style={{ fontSize: 11, color: '#cdd6f4', fontWeight: 600 }}>{e.agentType} — {fmtDuration(e.durationMs)} — {fmtCost(e.costUsd)}</div>
        </div>
      ))}
    </div>
  )
}

function CompareView({ tasks }) {
  const [firstId, setFirstId] = useState(null)
  const [secondId, setSecondId] = useState(null)
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!firstId || !secondId) return
    setData(null)
    setError(null)
    fetch(`${API_BASE}/tasks/compare?firstTaskId=${firstId}&secondTaskId=${secondId}`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [firstId, secondId])

  return (
    <div>
      <div style={{ display: 'flex', gap: 16, marginBottom: 20 }}>
        <div style={{ flex: 1 }}><TaskPicker tasks={tasks} value={firstId} onChange={setFirstId} label="First Task" /></div>
        <div style={{ flex: 1 }}><TaskPicker tasks={tasks} value={secondId} onChange={setSecondId} label="Second Task" /></div>
      </div>
      {error && <div style={{ color: '#f38ba8', fontSize: 12 }}>{error}</div>}
      {(!firstId || !secondId) && <div style={{ color: '#585b70', fontSize: 13 }}>Select two tasks to compare.</div>}
      {data && (
        <div style={{ display: 'flex', gap: 16 }}>
          <ComparisonSide side={data.first} />
          <ComparisonSide side={data.second} />
        </div>
      )}
    </div>
  )
}

// ── Top-level page ──

export default function ObservabilityPage() {
  const [subView, setSubView] = useState('timeline')
  const [tasks, setTasks] = useState([])

  useEffect(() => {
    fetch(`${API_BASE}/users/${DEV_USER_ID}/tasks?limit=30`)
      .then(res => res.ok ? res.json() : [])
      .then(setTasks)
      .catch(() => setTasks([]))
  }, [])

  return (
    <div style={{ padding: 24 }}>
      <SubNav subView={subView} setSubView={setSubView} />
      {subView === 'timeline' && <TimelineView tasks={tasks} />}
      {subView === 'summary' && <SummaryView tasks={tasks} />}
      {subView === 'dashboard' && <DashboardView />}
      {subView === 'errors' && <ErrorRatesView />}
      {subView === 'compare' && <CompareView tasks={tasks} />}
    </div>
  )
}
