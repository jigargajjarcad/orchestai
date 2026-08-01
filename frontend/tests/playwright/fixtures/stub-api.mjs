// Offline stub backend for the Playwright suite (F2 — Verification Durability).
//
// Serves every endpoint the frontend calls, with fixture data, so the browser
// tests render REAL populated tables, charts and streams instead of error
// states. Nothing here reaches the network: no live backend, no external API,
// no paid service. Started automatically by playwright.config.js.
//
// Control endpoints (test-only, under /__control) let a test arm specific
// server behaviour explicitly, instead of depending on process-global state
// that would make results order-dependent:
//   POST /__control/reset            — clear armed behaviour
//   POST /__control/drop-next-stream — the next SSE connection dies mid-run
import { createServer } from 'node:http'

const PORT = 5188

const TASKS = [
  { id: '11111111-1111-1111-1111-111111111111', title: 'Analyze the 2026 roster and flag load-management risks for the upcoming road trip', status: 'Completed', createdAt: '2026-07-28T10:00:00Z' },
  { id: '22222222-2222-2222-2222-222222222222', title: 'Draft a conditioning report for returning athletes', status: 'Completed', createdAt: '2026-07-29T10:00:00Z' },
  { id: '33333333-3333-3333-3333-333333333333', title: 'Summarize injury trends', status: 'Failed', createdAt: '2026-07-30T10:00:00Z' },
]

const AGENTS = ['Orchestrator', 'Research', 'Code', 'Data']
const MODELS = ['claude-sonnet-4-6', 'claude-opus-4-1']

const breakdown = []
for (let d = 1; d <= 14; d++) {
  const date = `2026-07-${String(d).padStart(2, '0')}`
  for (const agentType of AGENTS.slice(0, 3)) {
    breakdown.push({
      date, agentType, model: MODELS[d % 2],
      inputTokens: 1200 + d * 37, outputTokens: 400 + d * 11,
      costUsd: 0.004 * d + (agentType === 'Research' ? 0.02 : 0.005),
      executionCount: 1 + (d % 4), isLive: d === 14 && agentType === 'Research',
    })
  }
}

const COST_DASHBOARD = {
  totalCostUsd: breakdown.reduce((s, b) => s + b.costUsd, 0),
  totalExecutions: breakdown.reduce((s, b) => s + b.executionCount, 0),
  breakdown,
}

const ERROR_RATES = {
  agentErrorRates: [
    { agentType: 'Research', totalExecutions: 42, failedExecutions: 9, failureRate: 0.214, retryCount: 4, failuresByCategory: { Timeout: 5, RateLimit: 4 } },
    { agentType: 'Code', totalExecutions: 31, failedExecutions: 2, failureRate: 0.065, retryCount: 1, failuresByCategory: { Validation: 2 } },
    { agentType: 'Data', totalExecutions: 27, failedExecutions: 0, failureRate: 0, retryCount: 0, failuresByCategory: {} },
  ],
  toolErrorRates: [
    { toolName: 'web_search', totalCalls: 64, failedCalls: 7, failureRate: 0.109, failuresByCategory: { Timeout: 7 } },
    { toolName: 'database_query', totalCalls: 38, failedCalls: 1, failureRate: 0.026, failuresByCategory: { Syntax: 1 } },
  ],
}

// Values are DERIVED FROM THE TASK ID on purpose. Identical fixtures across
// tasks make a stale-response test vacuous: an overwrite would be invisible
// because both payloads render the same. The leading digit drives the numbers
// so a test can assert which task's data is actually on screen.
// Nested spans so the Timeline renders real SpanRows — including a parent with
// children, which is what the expand/collapse control operates on.
const T0 = '2026-07-30T10:00:00.000Z'
const TIMELINE_SPANS = [
  { spanId: 's1', parentSpanId: null, label: 'Orchestrator', spanType: 'Agent', agentType: 'Orchestrator', status: 'Completed', startedAt: T0, durationMs: 5200 },
  { spanId: 's2', parentSpanId: 's1', label: 'Research agent', spanType: 'Agent', agentType: 'Research', status: 'Completed', startedAt: T0, durationMs: 3100 },
  { spanId: 's3', parentSpanId: 's2', label: 'web_search', spanType: 'ToolCall', agentType: 'Research', status: 'Completed', startedAt: T0, durationMs: 900 },
  { spanId: 's4', parentSpanId: 's1', label: 'Data agent', spanType: 'Agent', agentType: 'Data', status: 'Failed', startedAt: T0, durationMs: 1200 },
]

const summaryFor = (id) => ({
  status: id.startsWith('3') ? 'Failed' : 'Completed',
  durationSeconds: Number(`${id[0]}0.5`), totalCostUsd: Number(`0.${id[0]}84`),
  totalInputTokens: 18422, totalOutputTokens: 5310,
  toolCallCount: 12, retryCount: 1, errorCount: 0, memoryUsed: true, checkpointRestored: false,
  agentsInvolved: AGENTS, providersAndModels: ['anthropic/claude-sonnet-4-6', 'anthropic/claude-opus-4-1'],
})

const sideFor = (id) => ({
  userPrompt: TASKS.find(t => t.id === id)?.title ?? 'Unknown task',
  status: 'Completed', durationSeconds: 51.2, totalCostUsd: 0.211, totalInputTokens: 20144, totalOutputTokens: 6021,
  finalResult: 'Three athletes exceed the acute:chronic workload threshold. Recommend reduced minutes on the second night of the back-to-back.',
  executions: AGENTS.map((agentType, i) => ({ agentType, durationMs: 4200 + i * 1900, costUsd: 0.03 + i * 0.02 })),
})

// Memories fixtures — long key/value so the row buttons and hidden header text
// are exercised at their realistic worst case.
const MEMORIES = [
  { id: 'mm1', agentType: 'Research', key: 'roster-load-management-2026', value: 'Three athletes flagged for acute:chronic workload ratio above threshold on back-to-backs.', importance: 5, updatedAt: '2026-07-30T10:00:00Z' },
  { id: 'mm2', agentType: 'Data', key: 'conditioning-index', value: 'Baseline conditioning index recalculated weekly from GPS load.', importance: 3, updatedAt: '2026-07-30T11:00:00Z' },
  { id: 'mm3', agentType: 'Code', key: 'schema-notes', value: 'athlete_metrics table partitioned by season.', importance: 2, updatedAt: '2026-07-30T12:00:00Z' },
]

const SUITES = [
  { id: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Roster analysis regression suite', targetAgentType: 'Research', description: 'Golden-set roster questions with expected-substring and LLM-judge scorers' },
  { id: 'aaaaaaaa-0000-0000-0000-000000000002', name: 'Code generation smoke suite', targetAgentType: 'Code', description: 'Compilation + contract checks' },
]

const RUNS = [
  { id: 'bbbbbbbb-0000-0000-0000-000000000001', subjectVersion: 'a1b2c3d', status: 'Completed', triggeredAt: '2026-07-29T09:00:00Z' },
  { id: 'bbbbbbbb-0000-0000-0000-000000000002', subjectVersion: 'e4f5g6h', status: 'Completed', triggeredAt: '2026-07-30T09:00:00Z' },
]

const RESULTS = {
  results: Array.from({ length: 6 }, (_, i) => ({
    evalCaseId: `cccccccc-0000-0000-0000-00000000000${i}`,
    scorerType: i % 2 ? 'LlmJudge' : 'ExpectedSubstring',
    score: (0.6 + i * 0.06).toFixed(2), passed: i !== 2,
  })),
}

const REGRESSION = {
  currentPassRate: 0.83, baselinePassRate: 0.67, passRateDelta: 0.16,
  caseDiffs: Array.from({ length: 5 }, (_, i) => ({
    evalCaseId: `cccccccc-0000-0000-0000-00000000000${i}`,
    currentScore: (0.6 + i * 0.06).toFixed(2), baselineScore: i === 4 ? null : (0.55 + i * 0.05).toFixed(2),
    scoreDelta: i === 4 ? null : '0.05', regressed: i === 2, isNewCase: i === 4,
  })),
}

const POSTHOC_SUMMARY = {
  status: 'Completed', scoredCount: 24, skippedAlreadyScoredCount: 3,
  passRate: 0.79, passedCount: 19,
  scoreDistribution: [
    { range: '0.0-0.2', count: 1 }, { range: '0.2-0.4', count: 2 },
    { range: '0.4-0.6', count: 2 }, { range: '0.6-0.8', count: 9 }, { range: '0.8-1.0', count: 10 },
  ],
}

function route(pathname, search, method) {
  if (pathname === '/api/v1/tasks' && method === 'POST') return { id: 'eeeeeeee-0000-0000-0000-000000000001' }
  if (/\/tasks$/.test(pathname) || /\/tasks$/.test(pathname.replace(/\?.*/, ''))) return TASKS
  if (/\/users\/[^/]+\/tasks$/.test(pathname)) return TASKS
  if (pathname.endsWith('/observability/cost-dashboard')) return COST_DASHBOARD
  if (pathname.endsWith('/observability/error-rates')) return ERROR_RATES
  if (pathname === '/api/v1/tasks/compare') {
    const p = new URLSearchParams(search)
    return { first: sideFor(p.get('firstTaskId')), second: sideFor(p.get('secondTaskId')) }
  }
  let m = pathname.match(/^\/api\/v1\/tasks\/([^/]+)\/summary$/)
  if (m) return summaryFor(m[1])
  m = pathname.match(/^\/api\/v1\/tasks\/([^/]+)\/timeline$/)
  if (m) return { spans: TIMELINE_SPANS }
  if (pathname === '/api/v1/eval-suites') return { suites: SUITES }
  if (/^\/api\/v1\/eval-suites\/[^/]+\/runs$/.test(pathname)) return { runs: RUNS }
  if (/^\/api\/v1\/eval-runs\/[^/]+\/results$/.test(pathname)) return RESULTS
  if (/^\/api\/v1\/eval-runs\/[^/]+\/regression-report$/.test(pathname)) return REGRESSION
  if (/^\/api\/v1\/eval-runs\/[^/]+\/posthoc-summary$/.test(pathname)) return POSTHOC_SUMMARY
  if (pathname === '/api/v1/post-hoc-scoring') return { evalRunId: 'dddddddd-0000-0000-0000-000000000001' }
  // Playground run + memories (M6.4)
  if (pathname === '/api/v1/tasks') return { id: 'eeeeeeee-0000-0000-0000-000000000001' }
  if (/^\/api\/v1\/tasks\/[^/]+\/start$/.test(pathname)) return {}
  if (/^\/api\/v1\/tasks\/[^/]+\/stream-ticket$/.test(pathname)) return { ticket: 'stub-ticket' }
  if (/^\/api\/v1\/users\/[^/]+\/memories$/.test(pathname)) return MEMORIES
  if (/^\/api\/v1\/tasks\/[^/]+$/.test(pathname)) return { finalResult: 'Three athletes exceed the acute:chronic workload threshold.\n\nUse the `db_query` tool for the raw rows.' }
  return null
}

// ── Playground run fixture ──────────────────────────────────────────────────
// The deliberately long tool name is the worst case for the off-screen
// "Tool <name> completed." span: the wider that hidden text, the further right
// it can push the document if it is not properly contained.
const LONG_TOOL = 'athlete_workload_acute_chronic_ratio_lookup_with_a_deliberately_long_name'
const AGENT_EXEC = 'aaaa1111-bbbb-2222-cccc-333333333333'
const sseEvents = [
  { event: 'task_started', payload: {} },
  { event: 'agent_started', payload: { agentExecutionId: AGENT_EXEC, agentType: 'Research' } },
  { event: 'message_written', payload: { agentExecutionId: AGENT_EXEC, agentType: 'Research', contentPreview: 'Checking workload data for the returning athletes.' } },
  { event: 'tool_started', payload: { agentExecutionId: AGENT_EXEC, toolName: LONG_TOOL } },
  { event: 'tool_completed', payload: { agentExecutionId: AGENT_EXEC, toolName: LONG_TOOL, success: true, durationMs: 1234, outputPreview: 'Three athletes exceed the acute:chronic threshold on the second night of the back-to-back.' } },
  { event: 'agent_completed', payload: { agentExecutionId: AGENT_EXEC, inputTokens: 1800, outputTokens: 640, costUsd: 0.031 } },
  { event: 'task_completed', payload: { totalCostUsd: 0.031 } },
]

// Armed by POST /__control/drop-next-stream. One-shot: the next stream drops
// mid-flight, every later stream (i.e. the reconnect) completes normally.
let dropNextStream = false

function streamSse(res) {
  const shouldDrop = dropNextStream
  dropNextStream = false
  res.writeHead(200, {
    'Content-Type': 'text/event-stream',
    'Cache-Control': 'no-cache',
    Connection: 'keep-alive',
    'Access-Control-Allow-Origin': '*',
  })
  let i = 0
  const tick = () => {
    if (shouldDrop && i === DROP_AFTER_EVENTS) { res.destroy(); return }
    if (i >= sseEvents.length) return
    const e = { ...sseEvents[i], timestamp: `2026-08-01T09:00:${String(i).padStart(2, '0')}.000Z` }
    res.write(`data: ${JSON.stringify(e)}\n\n`)
    i++
    setTimeout(tick, SSE_EVENT_INTERVAL_MS)
  }
  setTimeout(tick, SSE_FIRST_EVENT_DELAY_MS)
}

const DROP_AFTER_EVENTS = 3
const SSE_EVENT_INTERVAL_MS = 120
const SSE_FIRST_EVENT_DELAY_MS = 80

createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost')
  res.setHeader('Access-Control-Allow-Origin', '*')
  res.setHeader('Access-Control-Allow-Headers', '*')
  res.setHeader('Access-Control-Allow-Methods', 'GET,POST,DELETE,OPTIONS')
  if (req.method === 'OPTIONS') { res.writeHead(204); return res.end() }

  // Test-only control surface.
  if (url.pathname === '/__control/reset') {
    dropNextStream = false
    res.writeHead(200, { 'Content-Type': 'application/json' })
    return res.end('{"ok":true}')
  }
  if (url.pathname === '/__control/drop-next-stream') {
    dropNextStream = true
    res.writeHead(200, { 'Content-Type': 'application/json' })
    return res.end('{"ok":true}')
  }

  if (/^\/api\/v1\/tasks\/[^/]+\/stream$/.test(url.pathname)) return streamSse(res)

  const body = route(url.pathname, url.search, req.method)
  if (body == null) {
    res.writeHead(404, { 'Content-Type': 'application/json' })
    return res.end(JSON.stringify({ title: `No stub for ${url.pathname}` }))
  }
  res.writeHead(200, { 'Content-Type': 'application/json' })
  res.end(JSON.stringify(body))
}).listen(PORT, () => console.log(`stub-api listening on ${PORT}`))
