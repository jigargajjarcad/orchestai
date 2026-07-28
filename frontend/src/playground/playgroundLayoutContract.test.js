import { describe, it, expect } from 'vitest'
import { emptyGraphState, applyEvent, toSpanArray, ORCHESTRATOR_ID, MANAGER_REVIEW_ID } from './playgroundSpanModel'
import { computeGraphLayout } from '../observability/layout'

// P2-M5 regression guard — this LOCKS the reused contract between the live
// Playground span model and the (unmodified) observability layout engine.
// It stands in for the Timeline "freeze": if a future change to layout.js or
// to the span model breaks the node/edge/parent structure the Playground graph
// depends on, this test fails loudly rather than the regression only surfacing
// in a live paid run. It asserts the STRUCTURE (ids, types, parent→child edges,
// parallel geometry), not pixel positions, so legitimate spacing tweaks still pass.

const ev = (event, payload = {}, timestamp) => ({ event, payload, timestamp })
const fold = (events) => events.reduce(applyEvent, emptyGraphState())
const T = (s) => `2026-07-28T08:00:${String(s).padStart(2, '0')}.000Z`

const DATA = 'a-data', RESEARCH = 'a-research'

// A canonical full trace: orchestrator plans, dispatches Data + Research in
// PARALLEL, each runs a tool, then a manager review, then completion.
const CANONICAL = [
  ev('task_started', {}, T(0)),
  ev('orchestrator_plan', {}, T(1)),
  ev('agent_started', { agentExecutionId: DATA, agentType: 'Data' }, T(2)),
  ev('agent_started', { agentExecutionId: RESEARCH, agentType: 'Research' }, T(2)),
  ev('tool_started', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query' }, T(3)),
  ev('tool_completed', { agentExecutionId: DATA, agentType: 'Data', toolName: 'db_query', success: true, durationMs: 20 }, T(4)),
  ev('tool_started', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search' }, T(3)),
  ev('tool_completed', { agentExecutionId: RESEARCH, agentType: 'Research', toolName: 'perplexity_search', success: true, durationMs: 30 }, T(5)),
  ev('agent_completed', { agentExecutionId: DATA, agentType: 'Data', costUsd: 0.006 }, T(6)),
  ev('agent_completed', { agentExecutionId: RESEARCH, agentType: 'Research', costUsd: 0.011 }, T(6)),
  ev('manager_review_started', {}, T(7)),
  ev('manager_review_completed', { result: 'synthesis' }, T(8)),
  ev('task_completed', { totalCostUsd: 0.017 }, T(9)),
]

describe('Playground ⇄ observability layout contract (P2-M5 freeze-replacement guard)', () => {
  const spans = toSpanArray(fold(CANONICAL))
  const { nodes, edges } = computeGraphLayout(spans)
  const nodeById = new Map(nodes.map(n => [n.id, n]))
  const dbId = spans.find(s => s.label === 'db_query').spanId
  const pxId = spans.find(s => s.label === 'perplexity_search').spanId

  it('produces exactly the expected node set with the correct React Flow types', () => {
    expect(new Set(nodes.map(n => n.id))).toEqual(
      new Set([ORCHESTRATOR_ID, DATA, RESEARCH, dbId, pxId, MANAGER_REVIEW_ID]),
    )
    // Agents/review render as agentNode; tool calls as toolCallNode.
    for (const id of [ORCHESTRATOR_ID, DATA, RESEARCH, MANAGER_REVIEW_ID]) {
      expect(nodeById.get(id).type).toBe('agentNode')
    }
    for (const id of [dbId, pxId]) {
      expect(nodeById.get(id).type).toBe('toolCallNode')
    }
  })

  it('wires the parent→child edge topology the graph depends on', () => {
    const pairs = new Set(edges.map(e => `${e.source}->${e.target}`))
    expect(pairs).toEqual(new Set([
      `${ORCHESTRATOR_ID}->${DATA}`,
      `${ORCHESTRATOR_ID}->${RESEARCH}`,
      `${ORCHESTRATOR_ID}->${MANAGER_REVIEW_ID}`,
      `${DATA}->${dbId}`,
      `${RESEARCH}->${pxId}`,
    ]))
    // Every edge uses the reused custom edge type.
    for (const e of edges) expect(e.type).toBe('traceEdge')
  })

  it('emits the node data fields the reused node components read', () => {
    const data = nodeById.get(DATA).data
    expect(data).toMatchObject({ label: 'Data', agentName: 'Data', status: 'Completed', costUsd: 0.006 })
    expect(data.extentPct).toHaveProperty('offset')
    expect(data.extentPct).toHaveProperty('width')
    expect(nodeById.get(dbId).data).toMatchObject({ label: 'db_query', status: 'Completed', durationMs: 20 })
  })

  it('lays parallel siblings on the same X but distinct rows (Y) — no overlap', () => {
    const d = nodeById.get(DATA).position, r = nodeById.get(RESEARCH).position
    expect(d.x).toBe(r.x)      // same start time -> same column
    expect(d.y).not.toBe(r.y)  // parallel -> different rows
  })

  it('never carries the manager-review result text into the graph (§12 — no duplicate render)', () => {
    const review = spans.find(s => s.spanId === MANAGER_REVIEW_ID)
    expect(review.status).toBe('Completed')
    expect(review.result).toBeUndefined()
  })
})
