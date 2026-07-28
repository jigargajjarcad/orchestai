import { describe, it, expect } from 'vitest'
import {
  emptyGraphState, applyEvent, toSpanArray, ORCHESTRATOR_ID, MANAGER_REVIEW_ID,
} from './playgroundSpanModel'
import { computeGraphLayout } from '../observability/layout'

// Fold a normalized SSE event sequence into a span array (the graph's input).
const ev = (event, payload = {}, timestamp) => ({ event, payload, timestamp })
const fold = (events) => events.reduce(applyEvent, emptyGraphState())
const spansOf = (events) => toSpanArray(fold(events))
const byLabel = (spans, label) => spans.filter(s => s.label === label)
const T = (s) => `2026-07-28T08:00:${String(s).padStart(2, '0')}.000Z`

const DATA = 'a-data', RESEARCH = 'a-research'

describe('playgroundSpanModel — SSE → live span array (P2-M5)', () => {
  it('T1: initial state has zero spans', () => {
    expect(toSpanArray(emptyGraphState())).toEqual([])
  })

  it('T2: agent_started creates an Agent span parented to the orchestrator, Running', () => {
    const spans = spansOf([
      ev('task_started', { taskId: 't' }, T(0)),
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data', status: 'Running' }, T(1)),
    ])
    const orch = spans.find(s => s.spanId === ORCHESTRATOR_ID)
    const data = spans.find(s => s.spanId === DATA)
    expect(orch).toMatchObject({ spanType: 'Agent', parentSpanId: null, label: 'Orchestrator' })
    expect(data).toMatchObject({ spanType: 'Agent', parentSpanId: ORCHESTRATOR_ID, label: 'Data', status: 'Running' })
  })

  it('T3: tool_started creates a ToolCall span parented to its agent', () => {
    const spans = spansOf([
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(2)),
    ])
    const tool = spans.find(s => s.spanType === 'ToolCall')
    expect(tool).toMatchObject({ parentSpanId: DATA, label: 'db_query', status: 'Running' })
  })

  it('T4: parent/child hierarchy — db_query under Data, perplexity under Research', () => {
    const spans = spansOf([
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(2)),
      ev('agent_started', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(1)),
      ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(3)),
    ])
    const db = spans.find(s => s.label === 'db_query')
    const px = spans.find(s => s.label === 'perplexity_search')
    expect(db.parentSpanId).toBe(DATA)
    expect(px.parentSpanId).toBe(RESEARCH)
    // both agents are children of the single orchestrator root
    expect(byLabel(spans, 'Data')[0].parentSpanId).toBe(ORCHESTRATOR_ID)
    expect(byLabel(spans, 'Research')[0].parentSpanId).toBe(ORCHESTRATOR_ID)
  })

  it('T5: parallel siblings share start-X but get distinct rows (Y) via computeGraphLayout', () => {
    const spans = spansOf([
      ev('task_started', {}, T(0)),
      ev('orchestrator_plan', {}, T(1)),
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(2)),      // same start
      ev('agent_started', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(2)), // same start
    ])
    const { nodes } = computeGraphLayout(spans)
    const data = nodes.find(n => n.id === DATA)
    const research = nodes.find(n => n.id === RESEARCH)
    expect(data.position.x).toBe(research.position.x) // parallel -> identical X
    expect(data.position.y).not.toBe(research.position.y) // distinct rows -> no overlap
  })

  it('T6: completion — agent_completed/tool_completed reach Completed', () => {
    const spans = spansOf([
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(2)),
      ev('tool_completed', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query', success: true, durationMs: 18 }, T(3)),
      ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data', costUsd: 0.006 }, T(4)),
    ])
    expect(spans.find(s => s.spanId === DATA)).toMatchObject({ status: 'Completed', costUsd: 0.006 })
    expect(spans.find(s => s.label === 'db_query')).toMatchObject({ status: 'Completed', durationMs: 18 })
  })

  it('T7: failure — agent_failed and tool_completed(success=false) reach Failed', () => {
    const spans = spansOf([
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(2)),
      ev('tool_completed', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query', success: false }, T(3)),
      ev('agent_failed', { agentExecutionId: DATA, agentType: 'Data' }, T(4)),
    ])
    expect(spans.find(s => s.spanId === DATA).status).toBe('Failed')
    expect(spans.find(s => s.label === 'db_query').status).toBe('Failed')
  })

  it('T8: duplicate *_completed is idempotent', () => {
    const events = [
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data', costUsd: 0.01 }, T(2)),
      ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data', costUsd: 0.01 }, T(2)),
    ]
    const spans = spansOf(events)
    expect(byLabel(spans, 'Data')).toHaveLength(1)
    expect(spans.find(s => s.spanId === DATA).status).toBe('Completed')
  })

  it('T9: out-of-order — tool_completed before tool_started still yields one terminal tool node', () => {
    const spans = spansOf([
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('tool_completed', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query', success: true, durationMs: 12 }, T(3)),
    ])
    const tools = spans.filter(s => s.spanType === 'ToolCall')
    expect(tools).toHaveLength(1)
    expect(tools[0]).toMatchObject({ parentSpanId: DATA, status: 'Completed' })
  })

  it('T10: create-on-first-reference — a tool for an agent with no agent_started auto-creates the agent, and the layout tree is valid', () => {
    const spans = spansOf([
      ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(2)),
    ])
    const agent = spans.find(s => s.spanId === DATA)
    expect(agent).toBeDefined()
    expect(agent.parentSpanId).toBe(ORCHESTRATOR_ID)
    // computeGraphLayout must produce a valid, non-throwing tree (every child under a real parent)
    const { nodes } = computeGraphLayout(spans)
    const ids = new Set(nodes.map(n => n.id))
    expect(ids.has(DATA)).toBe(true)
    expect(ids.has(ORCHESTRATOR_ID)).toBe(true)
  })

  it('T11: repeated same tool (x3 perplexity) yields 3 distinct nodes matched in arrival order', () => {
    const spans = spansOf([
      ev('agent_started', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(1)),
      ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(2)),
      ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(3)),
      ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(4)),
      ev('tool_completed', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search', success: true }, T(5)),
      ev('tool_completed', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search', success: false }, T(6)),
    ])
    const px = spans.filter(s => s.label === 'perplexity_search')
    expect(px).toHaveLength(3)
    expect(px.filter(s => s.status === 'Completed')).toHaveLength(1) // first started completed first (FIFO)
    expect(px.filter(s => s.status === 'Failed')).toHaveLength(1)    // second started failed second
    expect(px.filter(s => s.status === 'Running')).toHaveLength(1)   // third still running
    expect(new Set(px.map(s => s.spanId)).size).toBe(3)              // distinct ids
  })

  it('manager review is a child of the orchestrator, and does not duplicate the result', () => {
    const spans = spansOf([
      ev('task_started', {}, T(0)),
      ev('manager_review_started', { taskId: 't' }, T(8)),
      ev('manager_review_completed', { taskId: 't', result: 'synthesis text' }, T(9)),
    ])
    const review = spans.find(s => s.spanId === MANAGER_REVIEW_ID)
    expect(review).toMatchObject({ parentSpanId: ORCHESTRATOR_ID, status: 'Completed', label: 'Manager Review' })
    // the model intentionally does NOT carry the result text (ManagerReviewCard renders it)
    expect(review.result).toBeUndefined()
  })

  it('is idempotent under replayed sequences (reconnect safety)', () => {
    const seq = [
      ev('task_started', {}, T(0)),
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data' }, T(2)),
    ]
    const once = toSpanArray(fold(seq))
    const twice = toSpanArray(fold([...seq, ...seq]))
    expect(twice).toEqual(once)
  })
})

describe('playgroundSpanModel — orchestrator/manager-review reconciliation (Defect 2 regression)', () => {
  // Real orchestrator agent-execution ids the backend emits for its OWN passes.
  const ORCH1 = 'orch-exec-1', ORCH2 = 'orch-exec-2'

  // Mirrors the genuine run (taskId fbc063f9…): orchestrator PLANNING pass, then
  // Data + Research in parallel with tools, then an orchestrator SYNTHESIS pass
  // that coincides with the manager review. Pre-fix this produced 3 "Orchestrator"
  // nodes + a duplicate review; post-fix it must be one clean tree.
  const genuineLike = () => [
    ev('task_started', {}, T(0)),
    ev('agent_started', { agentExecutionId: ORCH1, agentType: 'Orchestrator' }, T(1)),
    ev('agent_completed', { agentExecutionId: ORCH1, agentType: 'Orchestrator', costUsd: 0.003 }, T(2)),
    ev('orchestrator_plan', {}, T(2)),
    ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(3)),
    ev('agent_started', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(3)),
    ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(4)),
    ev('tool_completed', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query', success: true }, T(5)),
    ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(4)),
    ev('tool_completed', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search', success: true }, T(6)),
    ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(6)),
    ev('tool_completed', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search', success: true }, T(8)),
    ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data' }, T(9)),
    ev('agent_completed', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(9)),
    ev('agent_started', { agentExecutionId: ORCH2, agentType: 'Orchestrator' }, T(10)), // synthesis
    ev('manager_review_started', {}, T(10)),
    ev('agent_completed', { agentExecutionId: ORCH2, agentType: 'Orchestrator', costUsd: 0.01 }, T(11)),
    ev('manager_review_completed', { result: 'synthesis' }, T(11)),
    ev('task_completed', {}, T(12)),
  ]

  it('D2.1: agent_started(agentType=Orchestrator) creates NO duplicate child Orchestrator node', () => {
    const spans = spansOf(genuineLike())
    const orchNodes = byLabel(spans, 'Orchestrator')
    expect(orchNodes).toHaveLength(1)
    expect(orchNodes[0].spanId).toBe(ORCHESTRATOR_ID)
    // The real orchestrator execution ids never became their own nodes.
    expect(spans.some(s => s.spanId === ORCH1)).toBe(false)
    expect(spans.some(s => s.spanId === ORCH2)).toBe(false)
  })

  it('D2.2: the synthetic Orchestrator root stays the single root (parent null)', () => {
    const spans = spansOf(genuineLike())
    const roots = spans.filter(s => s.parentSpanId == null)
    expect(roots).toHaveLength(1)
    expect(roots[0]).toMatchObject({ spanId: ORCHESTRATOR_ID, label: 'Orchestrator', spanType: 'Agent' })
  })

  it('D2.3: Data and Research remain single siblings under the root', () => {
    const spans = spansOf(genuineLike())
    expect(byLabel(spans, 'Data')).toHaveLength(1)
    expect(byLabel(spans, 'Research')).toHaveLength(1)
    expect(byLabel(spans, 'Data')[0].parentSpanId).toBe(ORCHESTRATOR_ID)
    expect(byLabel(spans, 'Research')[0].parentSpanId).toBe(ORCHESTRATOR_ID)
  })

  it('D2.4: Manager Review appears exactly once', () => {
    const spans = spansOf(genuineLike())
    expect(byLabel(spans, 'Manager Review')).toHaveLength(1)
    expect(spans.filter(s => s.spanId === MANAGER_REVIEW_ID)).toHaveLength(1)
  })

  it('D2.5: the orchestrator synthesis pass does not become a second review representation', () => {
    const spans = spansOf(genuineLike())
    expect(spans.some(s => s.spanId === ORCH2)).toBe(false)
    // The only non-agent child directly under the root is the single Manager Review.
    const otherRootChildren = spans.filter(s =>
      s.parentSpanId === ORCHESTRATOR_ID && !['Data', 'Research'].includes(s.label))
    expect(otherRootChildren).toHaveLength(1)
    expect(otherRootChildren[0].label).toBe('Manager Review')
  })

  it('D2.6: repeated same-tool calls keep distinct occurrence ids', () => {
    const spans = spansOf(genuineLike())
    const px = byLabel(spans, 'perplexity_search')
    expect(px).toHaveLength(2)
    expect(new Set(px.map(s => s.spanId)).size).toBe(2)
    expect(px.every(s => s.parentSpanId === RESEARCH)).toBe(true)
  })

  it('D2.7: clean total — root + Data + Research + 3 tools + review = 7 nodes (was 9 pre-fix)', () => {
    const spans = spansOf(genuineLike())
    expect(spans).toHaveLength(7)
    const byType = {}
    spans.forEach(s => { byType[s.spanType] = (byType[s.spanType] || 0) + 1 })
    expect(byType).toEqual({ Agent: 4, ToolCall: 3 }) // Orchestrator, Data, Research, Manager Review + 3 tools
  })

  it('D2.8: a failed orchestrator pass marks the root Failed with no phantom node', () => {
    const spans = spansOf([
      ev('task_started', {}, T(0)),
      ev('agent_started', { agentExecutionId: ORCH1, agentType: 'Orchestrator' }, T(1)),
      ev('agent_failed', { agentExecutionId: ORCH1, agentType: 'Orchestrator' }, T(2)),
    ])
    expect(byLabel(spans, 'Orchestrator')).toHaveLength(1)
    expect(spans.find(s => s.spanId === ORCHESTRATOR_ID).status).toBe('Failed')
    expect(spans.some(s => s.spanId === ORCH1)).toBe(false)
  })

  it('D2.9: normal Data/Research agent handling is unchanged (non-Orchestrator agents still get nodes)', () => {
    const spans = spansOf([
      ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(1)),
      ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data', costUsd: 0.006 }, T(2)),
    ])
    const data = spans.find(s => s.spanId === DATA)
    expect(data).toMatchObject({ label: 'Data', parentSpanId: ORCHESTRATOR_ID, status: 'Completed', costUsd: 0.006 })
  })
})
