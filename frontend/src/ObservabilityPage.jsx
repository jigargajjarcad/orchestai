import { useState, useEffect, useMemo, useRef } from 'react'
import { ReactFlow, ReactFlowProvider, Background, Controls, BackgroundVariant } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import './observability/timelineFocus.css'
import { useViewportWidth } from './hooks/useViewportWidth'
import { authenticatedFetch } from './apiKey'
import { colors, radii, uiFontFamily, dataFontFamily, spacing } from './theme/tokens'
import { Panel } from './components/Panel'
import { Label } from './components/Label'
import { StatusBadge } from './components/StatusBadge'
import { Nav, NavItem } from './components/NavItem'
import { StateText } from './components/StateText'
import { Spinner } from './components/Spinner'
import { EmptyState } from './components/EmptyState'
import { ErrorBanner } from './components/ErrorBanner'
import { computeGraphLayout } from './observability/layout'
import { AgentNode } from './observability/AgentNode'
import { ToolCallNode } from './observability/ToolCallNode'
import { TraceEdge } from './observability/TraceEdge'
import { DetailPanel } from './observability/DetailPanel'
import { surface as timelineSurface, focusRing } from './observability/timelineTheme'
import { formatDuration as formatNodeDuration } from './observability/format'

// Milestone 2's own ~768px narrow-viewport precedent (App.jsx) — reused
// here for the Graph view canvas/detail-panel responsive pass (Task 6/§6).
const NARROW_VIEWPORT_BREAKPOINT = 768

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`
const DEV_USER_ID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

const AGENT_COLORS = {
  Orchestrator: colors.beaconYellow,
  Research: colors.traceBlue,
  Writer: colors.signalGreen,
  Code: colors.agentMauve,
  Data: colors.agentSky,
  Browser: colors.agentPeach,
}

const STATUS_COLORS = {
  Pending: colors.statusPending,
  Running: colors.statusRunning,
  Completed: colors.statusCompleted,
  Failed: colors.statusFailed,
  Skipped: colors.overlay0,
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

// <select>/<input type=date> aren't a good fit for the Input component
// (which renders an <input> specifically) — styled directly from tokens.js
// values instead. Matches the original selectStyle shape exactly (padding,
// borderRadius, border, background, color, fontSize, outline), just with
// hardcoded hex replaced by token references.
const selectStyle = {
  padding: '6px 10px',
  borderRadius: radii.lg,
  border: `1px solid ${colors.surface0}`,
  background: colors.mantle,
  color: colors.text,
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
    <div style={{ marginBottom: 20, borderBottom: `1px solid ${colors.base}`, paddingBottom: 12 }}>
      <Nav>
        {tabs.map(([key, label]) => (
          <NavItem
            key={key}
            active={subView === key}
            onClick={() => setSubView(key)}
            style={{ padding: '6px 14px', fontWeight: subView === key ? 700 : 400 }}
          >
            {label}
          </NavItem>
        ))}
      </Nav>
    </div>
  )
}

function TaskPicker({ tasks, value, onChange, label = 'Task' }) {
  return (
    <div>
      <div style={{ marginBottom: 8 }}>
        <Label style={{ fontWeight: 400 }}>{label}</Label>
      </div>
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
        <div style={{ marginBottom: 8 }}>
          <Label style={{ fontWeight: 400 }}>From</Label>
        </div>
        <input type="date" value={from} onChange={e => setFrom(e.target.value)} style={selectStyle} />
      </div>
      <div>
        <div style={{ marginBottom: 8 }}>
          <Label style={{ fontWeight: 400 }}>To</Label>
        </div>
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
  const color = span.spanType === 'ToolCall' ? colors.agentMauve : (AGENT_COLORS[span.label] ?? colors.traceBlue)
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
          style={{ width: 14, flexShrink: 0, cursor: span.children.length > 0 ? 'pointer' : 'default', color: colors.overlay0, fontSize: 10 }}
        >
          {span.children.length > 0 ? (expanded ? '▾' : '▸') : ''}
        </span>
        <span style={{
          width: 10, height: 10, borderRadius: 3, background: color, flexShrink: 0,
          border: isFailed ? `2px solid ${colors.alertRed}` : 'none',
        }} />
        <span style={{ fontSize: 12, color: colors.text, minWidth: 160, fontWeight: 600 }}>
          {span.spanType === 'ToolCall' ? `🔧 ${span.label}` : span.label}
        </span>
        <div style={{ flex: 1, position: 'relative', height: 14, background: colors.mantle, borderRadius: 3 }}>
          <div style={{
            position: 'absolute', left: `${offsetPct}%`, width: `${widthPct}%`, height: '100%',
            background: isFailed ? `${colors.statusFailed}90` : `${color}90`, borderRadius: 3, minWidth: 2,
          }} />
        </div>
        <span style={{ fontSize: 11, color: colors.overlay0, width: 70, textAlign: 'right' }}>{fmtDuration(span.durationMs)}</span>
        <span style={{ fontSize: 11, color: colors.overlay0, width: 90, textAlign: 'right' }}>
          {span.costUsd != null ? fmtCost(span.costUsd) : ''}
        </span>
        <span style={{
          fontSize: 10, fontWeight: 700, color: STATUS_COLORS[span.status] ?? colors.overlay0, width: 70, textAlign: 'right',
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
  // Graph view is the new default (plan §3/§5); List stays available,
  // unmodified, as the accessibility fallback (§6 item 5).
  const [viewMode, setViewMode] = useState('graph')
  const [selectedSpan, setSelectedSpan] = useState(null)
  const returnFocusRef = useRef(null)
  // Task 6/§6 responsive pass — same hook/breakpoint precedent as
  // SummaryView below and Milestone 2's App.jsx.
  const viewportWidth = useViewportWidth()
  const narrow = viewportWidth <= NARROW_VIEWPORT_BREAKPOINT

  useEffect(() => {
    if (!taskId) return
    setData(null)
    setError(null)
    setSelectedSpan(null)
    authenticatedFetch(`${API_BASE}/tasks/${taskId}/timeline`)
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

  // Graph view (§3a): same already-fetched `data`, translated into React
  // Flow's {nodes, edges} shape by the Task 3 layout engine — no second fetch.
  const graphLayout = useMemo(() => (data ? computeGraphLayout(data.spans) : null), [data])
  // Full span lookup by id, for the detail panel (Task 4) — the layout's own
  // node `data` only carries the fields the node card renders; the panel
  // needs the raw span (spanType, inputTokens/outputTokens, errorCategory, ...).
  const spansById = useMemo(() => new Map((data?.spans ?? []).map(s => [s.spanId, s])), [data])
  // Stable identity across renders (React Flow warns/re-renders needlessly
  // otherwise) — the components themselves are static imports.
  const nodeTypes = useMemo(() => ({ agentNode: AgentNode, toolCallNode: ToolCallNode }), [])
  const edgeTypes = useMemo(() => ({ traceEdge: TraceEdge }), [])
  // §3a.3 item 3: a lightweight time axis/ruler giving absolute scale across
  // the canvas — five evenly-spaced marks over the trace's real duration.
  const timeAxisTicks = useMemo(() => {
    if (!graphLayout) return []
    const totalMs = graphLayout.traceDurationMs
    return [0, 0.25, 0.5, 0.75, 1].map(fraction => `${((totalMs * fraction) / 1000).toFixed(1)}s`)
  }, [graphLayout])

  // §6 item 1: ARIA relationship description. Tabbing between nodes doesn't
  // inherently convey *why* one follows another — each node gets a rich
  // `ariaLabel` (React Flow's own supported top-level node prop — it sets
  // the aria-label directly on the actual focusable `.react-flow__node`
  // wrapper, per @xyflow/react's NodeWrapper source: `"aria-label":
  // node.ariaLabel`) describing its parent and any siblings dispatched by
  // the same parent, derived from `graphLayout.edges` (already built by
  // Task 3's layout engine — no changes to layout.js needed).
  const nodesWithAria = useMemo(() => {
    if (!graphLayout) return []
    const parentIdOf = new Map()
    const childIdsOf = new Map()
    for (const edge of graphLayout.edges) {
      parentIdOf.set(edge.target, edge.source)
      if (!childIdsOf.has(edge.source)) childIdsOf.set(edge.source, [])
      childIdsOf.get(edge.source).push(edge.target)
    }
    const labelById = new Map(graphLayout.nodes.map(n => [n.id, n.data?.label ?? n.id]))

    return graphLayout.nodes.map(node => {
      const { label, status, durationMs } = node.data ?? {}
      const kind = node.type === 'toolCallNode' ? 'Tool call' : 'Agent'
      const parentId = parentIdOf.get(node.id)
      const parentLabel = parentId ? labelById.get(parentId) : null
      const siblingLabels = parentId
        ? (childIdsOf.get(parentId) ?? []).filter(id => id !== node.id).map(id => labelById.get(id))
        : []
      const relationship = parentLabel
        ? `. Child of ${parentLabel}${siblingLabels.length > 0 ? `, running alongside ${siblingLabels.join(', ')}` : ''}.`
        : '. Root span.'
      const durationText = durationMs != null ? `, ${formatNodeDuration(durationMs)}` : ''

      return { ...node, ariaLabel: `${kind} ${label}, ${status}${durationText}${relationship}` }
    })
  }, [graphLayout])

  function openDetailForSpan(span, focusEl) {
    if (!span) return
    returnFocusRef.current = focusEl
    setSelectedSpan({ ...span, agentName: span.label })
  }

  function handleNodeClick(event, node) {
    openDetailForSpan(spansById.get(node.id), event.currentTarget)
  }

  // §6 item 2 / carry-forward #2 (Task 5's review): React Flow's own
  // keydown handling on a focused node (confirmed in @xyflow/system's
  // `elementSelectionKeys = ['Enter', ' ', 'Escape']`) only toggles the
  // node's internal `selected` state — it never calls the user-supplied
  // `onNodeClick` prop, so Enter/Space on a keyboard-focused node silently
  // did nothing. Fix: a keydown listener on the canvas container (an
  // ancestor of every node wrapper, so it sees the bubbled event) that maps
  // a focused node + Enter/Space onto the same `openDetailForSpan` path
  // `handleNodeClick` already uses — including populating `returnFocusRef`
  // so DetailPanel's existing close-focus-return logic works identically
  // for keyboard activation. `preventDefault` also stops Space's default
  // page-scroll action (React Flow's own internal handler doesn't prevent
  // it for Enter/Space, only for arrow-key node-drag).
  function handleGraphKeyDown(event) {
    if (event.key !== 'Enter' && event.key !== ' ') return
    const nodeEl = event.target.closest?.('.react-flow__node')
    if (!nodeEl) return
    event.preventDefault()
    openDetailForSpan(spansById.get(nodeEl.dataset.id), nodeEl)
  }

  function toggleButtonStyle(active) {
    return {
      padding: '4px 12px',
      fontSize: 11,
      fontWeight: active ? 700 : 400,
      borderRadius: radii.lg,
      border: `1px solid ${active ? colors.traceBlue : colors.surface0}`,
      background: active ? `${colors.traceBlue}20` : 'transparent',
      color: active ? colors.traceBlue : colors.subtext0,
      cursor: 'pointer',
    }
  }

  return (
    <div>
      <div style={{ marginBottom: 16, maxWidth: 480 }}>
        <TaskPicker tasks={tasks} value={taskId} onChange={setTaskId} />
      </div>
      {error && <StateText tone="error" style={{ fontSize: 12 }}>{error}</StateText>}
      {!taskId && <StateText tone="muted">Select a task to view its execution timeline.</StateText>}
      {data && (
        <Panel>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginBottom: 12 }}>
            <div style={{ fontSize: 11, color: colors.surface2 }}>
              Trace <span style={{ color: colors.traceBlue, fontFamily: 'monospace' }}>{data.traceId}</span> · {data.spans.length} spans
            </div>
            <div role="group" aria-label="Timeline view mode" style={{ display: 'flex', gap: 6 }}>
              <button
                type="button"
                aria-pressed={viewMode === 'graph'}
                onClick={() => setViewMode('graph')}
                style={toggleButtonStyle(viewMode === 'graph')}
              >
                Graph
              </button>
              <button
                type="button"
                aria-pressed={viewMode === 'list'}
                onClick={() => setViewMode('list')}
                style={toggleButtonStyle(viewMode === 'list')}
              >
                List
              </button>
            </div>
          </div>

          {viewMode === 'list' && tree.map(root => (
            <SpanRow key={root.spanId} span={root} depth={0} traceStart={traceStart} traceDurationMs={traceDurationMs} />
          ))}

          {viewMode === 'graph' && graphLayout && (
            <div>
              <div
                style={{
                  display: 'flex', justifyContent: 'space-between', padding: '0 14px 6px',
                  fontFamily: 'monospace', fontSize: 10, color: colors.overlay0,
                }}
              >
                {timeAxisTicks.map((tick, i) => <span key={i}>{tick}</span>)}
              </div>
              <div
                // Task 6/§6 responsive pass: a fixed 520px canvas eats most
                // of a narrow viewport's vertical space before the user even
                // scrolls to it — reduced at the same ~768px breakpoint used
                // elsewhere in this product. `onKeyDown` is carry-forward #2
                // (handleGraphKeyDown, above): this div is an ancestor of
                // every node wrapper, so it sees the bubbled Enter/Space keydown.
                // The `--observability-focus-ring` custom property feeds
                // timelineFocus.css's `.react-flow__node:focus-visible`
                // override (carry-forward #1) from timelineTheme.js's
                // `focusRing` — one source of truth for that color.
                onKeyDown={handleGraphKeyDown}
                style={{
                  width: '100%', height: narrow ? 380 : 520, borderRadius: radii.lg, overflow: 'hidden',
                  background: timelineSurface.canvas, border: `1px solid ${timelineSurface.panelBorder}`,
                  '--observability-focus-ring': focusRing,
                }}
              >
                <ReactFlowProvider>
                  <ReactFlow
                    nodes={nodesWithAria}
                    edges={graphLayout.edges}
                    nodeTypes={nodeTypes}
                    edgeTypes={edgeTypes}
                    onNodeClick={handleNodeClick}
                    fitView
                    nodesFocusable
                    edgesFocusable
                    minZoom={0.2}
                  >
                    <Background variant={BackgroundVariant.Dots} gap={16} size={1} color={timelineSurface.panelBorder} />
                    <Controls />
                  </ReactFlow>
                </ReactFlowProvider>
              </div>
            </div>
          )}
        </Panel>
      )}

      {selectedSpan && (
        <DetailPanel span={selectedSpan} onClose={() => setSelectedSpan(null)} returnFocusRef={returnFocusRef} narrow={narrow} />
      )}
    </div>
  )
}

// ── Execution Summary Card ──

function SummaryStat({ label, value, color, categorical }) {
  // `value` defaults to the data face (numeric metrics — the common case);
  // `categorical` renders it on the UI face for status/Yes-No values, which
  // are categorical/status info per the Two-Face Rule, not data.
  return (
    <div>
      <div style={{ fontSize: 10, color: colors.overlay0, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 4, fontFamily: uiFontFamily }}>{label}</div>
      <div style={{ fontSize: 18, fontWeight: 700, color: color ?? colors.text, fontFamily: categorical ? uiFontFamily : dataFontFamily }}>{value}</div>
    </div>
  )
}

function SummaryView({ tasks }) {
  const [taskId, setTaskId] = useState(null)
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)
  const width = useViewportWidth()
  const statGridCols = width <= 1024 ? 2 : 4

  useEffect(() => {
    if (!taskId) return
    setData(null)
    setError(null)
    authenticatedFetch(`${API_BASE}/tasks/${taskId}/summary`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [taskId])

  return (
    <div>
      <div style={{ marginBottom: 16, maxWidth: 480 }}>
        <TaskPicker tasks={tasks} value={taskId} onChange={setTaskId} />
      </div>
      {error && <ErrorBanner>{error}</ErrorBanner>}
      {!taskId && <EmptyState title="Select a task to view its summary card" />}
      {taskId && !data && !error && <Spinner label="Loading summary…" />}
      {data && (
        <Panel>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
            <StatusBadge status={data.status} borderAlphaSuffix="60" style={{ fontSize: 12, letterSpacing: '0.05em' }} />
            {data.checkpointRestored && (
              <span style={{ fontSize: 11, color: colors.beaconYellow, fontFamily: uiFontFamily }}>⏮ Resumed from checkpoint</span>
            )}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: `repeat(${statGridCols}, 1fr)`, gap: 20, marginBottom: 20 }}>
            <SummaryStat label="Duration" value={data.durationSeconds != null ? `${data.durationSeconds.toFixed(1)}s` : '—'} />
            <SummaryStat label="Total Cost" value={fmtCost(data.totalCostUsd)} color={colors.signalGreen} />
            <SummaryStat label="Tokens" value={`${data.totalInputTokens.toLocaleString()} / ${data.totalOutputTokens.toLocaleString()}`} />
            <SummaryStat label="Tool Calls" value={data.toolCallCount} />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: `repeat(${statGridCols}, 1fr)`, gap: 20, marginBottom: 20 }}>
            <SummaryStat label="Retry Count" value={data.retryCount} color={data.retryCount > 0 ? colors.statusWarning : undefined} />
            <SummaryStat label="Error Count" value={data.errorCount} color={data.errorCount > 0 ? colors.alertRed : undefined} />
            <SummaryStat label="Memory Used" value={data.memoryUsed ? 'Yes' : 'No'} color={data.memoryUsed ? colors.signalGreen : undefined} categorical />
            <SummaryStat label="Checkpoint Restored" value={data.checkpointRestored ? 'Yes' : 'No'} categorical />
          </div>

          <div style={{ borderTop: `1px solid ${colors.surface0}`, paddingTop: 14 }}>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>Agents Involved</Label>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 14 }}>
              {data.agentsInvolved.map(a => (
                <span key={a} style={{
                  fontSize: 11, color: AGENT_COLORS[a] ?? colors.text, border: `1px solid ${AGENT_COLORS[a] ?? colors.surface2}60`,
                  borderRadius: 4, padding: '2px 8px', fontFamily: uiFontFamily,
                }}>{a}</span>
              ))}
            </div>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>Providers / Models</Label>
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {data.providersAndModels.map(m => (
                <span key={m} style={{ fontSize: 11, color: colors.subtext0, fontFamily: dataFontFamily }}>{m}</span>
              ))}
            </div>
          </div>
        </Panel>
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

  if (grouped.length === 0) return <StateText tone="muted" style={{ fontSize: 12 }}>No data in this range.</StateText>

  return (
    <div style={{ display: 'flex', alignItems: 'flex-end', gap: 6, height: 140, padding: '8px 0' }}>
      {grouped.map(([key, value]) => (
        <div key={key} title={`${key}: ${fmtCost(value)}`} style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
          <div style={{
            width: '100%', maxWidth: 28, height: `${(value / max) * 110}px`, minHeight: 2,
            background: colorFn ? colorFn(key) : colors.traceBlue, borderRadius: '3px 3px 0 0',
          }} />
          <div style={{ fontSize: 9, color: colors.overlay0, writingMode: 'vertical-rl', textOrientation: 'mixed', height: 40, fontFamily: dataFontFamily }}>
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
    authenticatedFetch(`${API_BASE}/users/${DEV_USER_ID}/observability/cost-dashboard?from=${from}&to=${to}`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [from, to])

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <DateRangePicker from={from} to={to} setFrom={setFrom} setTo={setTo} />
      </div>
      {error && <ErrorBanner>{error}</ErrorBanner>}
      {!data && !error && <Spinner label="Loading cost dashboard…" />}
      {data && (
        <>
          <div style={{ display: 'flex', gap: 20, marginBottom: 20 }}>
            <Panel>
              <SummaryStat label="Total Cost" value={fmtCost(data.totalCostUsd)} color={colors.signalGreen} />
            </Panel>
            <Panel>
              <SummaryStat label="Total Executions" value={data.totalExecutions} />
            </Panel>
          </div>

          <Panel style={{ marginBottom: 20 }}>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>Cost Over Time</Label>
            </div>
            <BarChart entries={data.breakdown} keyFn={b => b.date} valueFn={b => b.costUsd} />
          </Panel>

          <Panel>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>Breakdown by Agent / Model</Label>
            </div>
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12, fontFamily: uiFontFamily }}>
                <thead>
                  <tr style={{ textAlign: 'left', color: colors.overlay0, borderBottom: `1px solid ${colors.surface0}` }}>
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
                    <tr key={i} style={{ borderBottom: `1px solid ${colors.mantle}` }}>
                      <td style={{ padding: '6px 8px', color: colors.subtext0, fontFamily: dataFontFamily }}>{b.date}</td>
                      <td style={{ padding: '6px 8px', color: AGENT_COLORS[b.agentType] ?? colors.text, fontFamily: uiFontFamily }}>{b.agentType}</td>
                      <td style={{ padding: '6px 8px', color: colors.overlay0, fontFamily: dataFontFamily, fontSize: 11 }}>{b.model}</td>
                      <td style={{ padding: '6px 8px', textAlign: 'right', color: colors.subtext0, fontFamily: dataFontFamily }}>{b.inputTokens + b.outputTokens}</td>
                      <td style={{ padding: '6px 8px', textAlign: 'right', color: colors.signalGreen, fontFamily: dataFontFamily }}>{fmtCost(b.costUsd)}</td>
                      <td style={{ padding: '6px 8px', textAlign: 'right', color: colors.subtext0, fontFamily: dataFontFamily }}>{b.executionCount}</td>
                      <td style={{ padding: '6px 8px' }}>
                        {b.isLive && <span style={{ fontSize: 9, color: colors.statusWarning, fontFamily: uiFontFamily }}>● LIVE</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Panel>
        </>
      )}
    </div>
  )
}

// ── Error Rate Monitoring ──

function ErrorRateTable({ title, rows, nameKey }) {
  return (
    <Panel>
      <div style={{ marginBottom: 8 }}>
        <Label style={{ fontWeight: 400 }}>{title}</Label>
      </div>
      {rows.length === 0 ? (
        <StateText tone="muted" style={{ fontSize: 12 }}>No data in this range.</StateText>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12, fontFamily: uiFontFamily }}>
            <thead>
              <tr style={{ textAlign: 'left', color: colors.overlay0, borderBottom: `1px solid ${colors.surface0}` }}>
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
                const failColor = r.failureRate > 0.2 ? colors.alertRed : r.failureRate > 0 ? colors.statusWarning : colors.signalGreen
                return (
                  <tr key={i} style={{ borderBottom: `1px solid ${colors.mantle}` }}>
                    <td style={{ padding: '6px 8px', color: colors.text, fontWeight: 600, fontFamily: uiFontFamily }}>{r[nameKey]}</td>
                    <td style={{ padding: '6px 8px', textAlign: 'right', color: colors.subtext0, fontFamily: dataFontFamily }}>{r.totalExecutions ?? r.totalCalls}</td>
                    <td style={{ padding: '6px 8px', textAlign: 'right', color: colors.subtext0, fontFamily: dataFontFamily }}>{r.failedExecutions ?? r.failedCalls}</td>
                    <td style={{ padding: '6px 8px', textAlign: 'right', color: failColor, fontWeight: 700, fontFamily: dataFontFamily }}>
                      {(r.failureRate * 100).toFixed(1)}%
                    </td>
                    {nameKey === 'agentType' && <td style={{ padding: '6px 8px', textAlign: 'right', color: r.retryCount > 0 ? colors.statusWarning : colors.overlay0, fontFamily: dataFontFamily }}>{r.retryCount}</td>}
                    <td style={{ padding: '6px 8px', color: colors.overlay0, fontSize: 11, fontFamily: dataFontFamily }}>
                      {Object.entries(r.failuresByCategory).map(([cat, count]) => `${cat}: ${count}`).join(', ') || '—'}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  )
}

function ErrorRatesView() {
  const [from, setFrom] = useState(daysAgoIso(30))
  const [to, setTo] = useState(todayIso())
  const [data, setData] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    setError(null)
    authenticatedFetch(`${API_BASE}/users/${DEV_USER_ID}/observability/error-rates?from=${from}&to=${to}`)
      .then(res => { if (!res.ok) throw new Error(`Failed: ${res.status}`); return res.json() })
      .then(setData)
      .catch(err => setError(err.message))
  }, [from, to])

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <DateRangePicker from={from} to={to} setFrom={setFrom} setTo={setTo} />
      </div>
      {error && <ErrorBanner>{error}</ErrorBanner>}
      {!data && !error && <Spinner label="Loading error rates…" />}
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
  if (!side) {
    return (
      <Panel style={{ flex: 1 }}>
        <EmptyState title="Select a task" style={{ padding: `${spacing.xl} ${spacing.lg}` }} />
      </Panel>
    )
  }
  return (
    <Panel style={{ flex: 1 }}>
      <div style={{ fontSize: 13, color: colors.text, fontWeight: 700, marginBottom: 4 }}>{side.userPrompt}</div>
      <StatusBadge status={side.status} borderAlphaSuffix="60" style={{ padding: '1px 8px', fontSize: 10, letterSpacing: 'normal' }} />

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, margin: '14px 0' }}>
        <SummaryStat label="Duration" value={side.durationSeconds != null ? `${side.durationSeconds.toFixed(1)}s` : '—'} />
        <SummaryStat label="Cost" value={fmtCost(side.totalCostUsd)} color={colors.signalGreen} />
        <SummaryStat label="Input Tokens" value={side.totalInputTokens.toLocaleString()} />
        <SummaryStat label="Output Tokens" value={side.totalOutputTokens.toLocaleString()} />
      </div>

      <div style={{ marginBottom: 8 }}>
        <Label style={{ fontWeight: 400 }}>Final Result</Label>
      </div>
      <div style={{
        background: colors.mantle, borderRadius: 6, padding: '10px 12px', fontSize: 12, color: colors.subtext0,
        whiteSpace: 'pre-wrap', maxHeight: 200, overflow: 'auto', marginBottom: 14,
      }}>
        {side.finalResult ?? '—'}
      </div>

      <div style={{ marginBottom: 8 }}>
        <Label style={{ fontWeight: 400 }}>Agent Executions</Label>
      </div>
      {side.executions.map((e, i) => (
        <div key={i} style={{ borderLeft: `3px solid ${AGENT_COLORS[e.agentType] ?? colors.surface2}`, padding: '4px 10px', marginBottom: 6 }}>
          <div style={{ fontSize: 11, color: colors.text, fontWeight: 600, fontFamily: uiFontFamily }}>
            {e.agentType} — <span style={{ fontFamily: dataFontFamily }}>{fmtDuration(e.durationMs)}</span> — <span style={{ fontFamily: dataFontFamily }}>{fmtCost(e.costUsd)}</span>
          </div>
        </div>
      ))}
    </Panel>
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
    authenticatedFetch(`${API_BASE}/tasks/compare?firstTaskId=${firstId}&secondTaskId=${secondId}`)
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
      {error && <ErrorBanner>{error}</ErrorBanner>}
      {(!firstId || !secondId) && <EmptyState title="Select two tasks to compare" />}
      {firstId && secondId && !data && !error && <Spinner label="Loading comparison…" />}
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
    authenticatedFetch(`${API_BASE}/users/${DEV_USER_ID}/tasks?limit=30`)
      .then(res => res.ok ? res.json() : [])
      .then(setTasks)
      .catch(() => setTasks([]))
  }, [])

  return (
    <div>
      <SubNav subView={subView} setSubView={setSubView} />
      {subView === 'timeline' && <TimelineView tasks={tasks} />}
      {subView === 'summary' && <SummaryView tasks={tasks} />}
      {subView === 'dashboard' && <DashboardView />}
      {subView === 'errors' && <ErrorRatesView />}
      {subView === 'compare' && <CompareView tasks={tasks} />}
    </div>
  )
}
