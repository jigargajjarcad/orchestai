// frontend/src/playground/playgroundSpanModel.js
//
// P2-M5 — pure derivation of a live "span array" from the Playground SSE stream,
// shaped exactly like the `/tasks/{id}/timeline` DTO's `spans` so it can be fed
// DIRECTLY into the reused, unmodified `observability/layout.js`
// `computeGraphLayout(spans)`. No React, no DOM, no backend/SSE-contract change.
//
// A span mirrors the TimelineSpanDto subset computeGraphLayout consumes:
//   { spanId, parentSpanId, spanType: 'Agent'|'ToolCall', label, status,
//     startedAt, completedAt, durationMs, costUsd }
//
// Hierarchy is DERIVED (the live SSE events carry no spanId/parentSpanId):
//   - a synthetic Orchestrator root (id ORCHESTRATOR_ID)
//   - agents are children of the orchestrator (id = backend agentExecutionId)
//   - tool calls are children of their agent (synthetic occurrence id)
//   - manager review is a child of the orchestrator (id MANAGER_REVIEW_ID)
//
// The reducer is a pure, idempotent fold over events keyed by id, so duplicate /
// out-of-order / replayed events converge to the same result.

export const ORCHESTRATOR_ID = 'orchestrator'
export const MANAGER_REVIEW_ID = 'manager-review'

// The backend emits agent_started/agent_completed/agent_failed with this
// agentType for the orchestrator's OWN planning/synthesis passes — these are the
// synthetic root's lifecycle, not separate specialist agents. Routing them to
// the root (instead of creating a child node keyed by agentExecutionId) prevents
// the duplicate "Orchestrator" nodes, and stops the synthesis pass from becoming
// a second visual representation of the Manager Review. (Data/Research and every
// other agentType keep their normal child-node handling.)
export const ORCHESTRATOR_AGENT_TYPE = 'Orchestrator'

/** Initial reducer state. `byId`+`order` materialize the span array;
 * `toolSeq`/`running` support synthetic tool-call identity + started→completed
 * matching for repeated same-tool calls (e.g. Research × N perplexity_search). */
export function emptyGraphState() {
  return { byId: {}, order: [], toolSeq: {}, running: {} }
}

/** Ordered span array for computeGraphLayout. */
export function toSpanArray(state) {
  return state.order.map(id => state.byId[id])
}

function tsOf(event) {
  // Server emission time when present (SseEvent.Timestamp), else client receipt.
  return event?.timestamp ?? new Date().toISOString()
}

function msBetween(startedAt, completedAt) {
  if (!startedAt || !completedAt) return null
  const d = new Date(completedAt) - new Date(startedAt)
  return Number.isFinite(d) && d >= 0 ? d : null
}

// Shallow-clone the top-level state so React sees a new reference; span objects
// are cloned individually on mutation so prior state is never mutated.
function cloneState(state) {
  return {
    byId: { ...state.byId },
    order: [...state.order],
    toolSeq: { ...state.toolSeq },
    running: { ...state.running },
  }
}

function upsert(next, span) {
  const existing = next.byId[span.spanId]
  if (existing) {
    // Merge; never regress an earlier startedAt or a terminal status.
    next.byId[span.spanId] = {
      ...existing,
      ...span,
      startedAt: existing.startedAt ?? span.startedAt,
    }
  } else {
    next.byId[span.spanId] = span
    next.order.push(span.spanId)
  }
  return next.byId[span.spanId]
}

function patch(next, spanId, changes) {
  const existing = next.byId[spanId]
  if (!existing) return // caller ensures existence first
  next.byId[spanId] = { ...existing, ...changes }
}

function ensureOrchestrator(next, now) {
  if (!next.byId[ORCHESTRATOR_ID]) {
    upsert(next, {
      spanId: ORCHESTRATOR_ID, parentSpanId: null, spanType: 'Agent',
      label: 'Orchestrator', status: 'Running', startedAt: now,
      completedAt: null, durationMs: null, costUsd: null,
    })
  }
}

function ensureAgent(next, agentExecutionId, agentType, now) {
  ensureOrchestrator(next, now)
  if (!next.byId[agentExecutionId]) {
    upsert(next, {
      spanId: agentExecutionId, parentSpanId: ORCHESTRATOR_ID, spanType: 'Agent',
      label: agentType ?? 'Agent', status: 'Running', startedAt: now,
      completedAt: null, durationMs: null, costUsd: null,
    })
  }
}

/**
 * Pure reducer: fold one normalized SSE event (`{ event, payload, timestamp }`)
 * into the graph state. Unknown/irrelevant events (approval_required,
 * message_written, agent_retry, checkpoint_saved, …) are ignored for the graph.
 */
export function applyEvent(state, rawEvent) {
  const type = rawEvent?.event
  const p = rawEvent?.payload ?? {}
  const now = tsOf(rawEvent)
  const next = cloneState(state)

  switch (type) {
    case 'task_started': {
      ensureOrchestrator(next, now)
      return next
    }

    case 'orchestrator_plan': {
      // Orchestrator's PLANNING phase ends here; the manager review is a
      // separate node created later (matching the genuine 2-Orchestrator trace).
      ensureOrchestrator(next, now)
      const orch = next.byId[ORCHESTRATOR_ID]
      if (orch.status === 'Running') {
        patch(next, ORCHESTRATOR_ID, {
          status: 'Completed', completedAt: now,
          durationMs: msBetween(orch.startedAt, now),
        })
      }
      return next
    }

    case 'agent_started': {
      if (!p.agentExecutionId) return state
      // Orchestrator's own passes belong to the synthetic root — never a
      // separate child node (see ORCHESTRATOR_AGENT_TYPE).
      if (p.agentType === ORCHESTRATOR_AGENT_TYPE) {
        ensureOrchestrator(next, now)
        return next
      }
      ensureAgent(next, p.agentExecutionId, p.agentType, now)
      return next
    }

    case 'agent_completed': {
      if (!p.agentExecutionId) return state
      // The root's lifecycle is governed by orchestrator_plan + task_completed/
      // task_failed; an orchestrator pass completing never creates/mutates a node.
      if (p.agentType === ORCHESTRATOR_AGENT_TYPE) {
        ensureOrchestrator(next, now)
        return next
      }
      ensureAgent(next, p.agentExecutionId, p.agentType, now)
      const s = next.byId[p.agentExecutionId]
      patch(next, p.agentExecutionId, {
        status: 'Completed', completedAt: now,
        durationMs: msBetween(s.startedAt, now),
        costUsd: p.costUsd ?? s.costUsd ?? null,
      })
      return next
    }

    case 'agent_failed': {
      if (!p.agentExecutionId) return state
      // An orchestrator failure is an overall orchestration failure → mark the
      // synthetic root Failed; still never a separate child node.
      if (p.agentType === ORCHESTRATOR_AGENT_TYPE) {
        ensureOrchestrator(next, now)
        const root = next.byId[ORCHESTRATOR_ID]
        patch(next, ORCHESTRATOR_ID, {
          status: 'Failed', completedAt: now, durationMs: msBetween(root.startedAt, now),
        })
        return next
      }
      ensureAgent(next, p.agentExecutionId, p.agentType, now)
      const s = next.byId[p.agentExecutionId]
      patch(next, p.agentExecutionId, {
        status: 'Failed', completedAt: now, durationMs: msBetween(s.startedAt, now),
      })
      return next
    }

    case 'tool_started': {
      if (!p.agentExecutionId || !p.toolName) return state
      ensureAgent(next, p.agentExecutionId, p.agentType, now)
      const key = `${p.agentExecutionId}:${p.toolName}`
      const n = next.toolSeq[key] ?? 0
      const spanId = `${key}:${n}`
      next.toolSeq[key] = n + 1
      next.running[key] = [...(next.running[key] ?? []), spanId]
      upsert(next, {
        spanId, parentSpanId: p.agentExecutionId, spanType: 'ToolCall',
        label: p.toolName, status: 'Running', startedAt: now,
        completedAt: null, durationMs: null, costUsd: null,
      })
      return next
    }

    case 'tool_completed': {
      if (!p.agentExecutionId || !p.toolName) return state
      const key = `${p.agentExecutionId}:${p.toolName}`
      const queue = next.running[key] ?? []
      let spanId
      if (queue.length > 0) {
        // Match the oldest still-running call of this (agent, tool) — FIFO.
        spanId = queue[0]
        next.running[key] = queue.slice(1)
      } else {
        // Out-of-order: completed before started — synthesize the tool node now.
        ensureAgent(next, p.agentExecutionId, p.agentType, now)
        const n = next.toolSeq[key] ?? 0
        spanId = `${key}:${n}`
        next.toolSeq[key] = n + 1
        upsert(next, {
          spanId, parentSpanId: p.agentExecutionId, spanType: 'ToolCall',
          label: p.toolName, status: 'Running', startedAt: now,
          completedAt: null, durationMs: null, costUsd: null,
        })
      }
      const s = next.byId[spanId]
      patch(next, spanId, {
        status: p.success === false ? 'Failed' : 'Completed',
        completedAt: now,
        durationMs: p.durationMs ?? msBetween(s.startedAt, now),
      })
      return next
    }

    case 'manager_review_started': {
      ensureOrchestrator(next, now)
      upsert(next, {
        spanId: MANAGER_REVIEW_ID, parentSpanId: ORCHESTRATOR_ID, spanType: 'Agent',
        label: 'Manager Review', status: 'Running', startedAt: now,
        completedAt: null, durationMs: null, costUsd: null,
      })
      return next
    }

    case 'manager_review_completed': {
      if (!next.byId[MANAGER_REVIEW_ID]) {
        ensureOrchestrator(next, now)
        upsert(next, {
          spanId: MANAGER_REVIEW_ID, parentSpanId: ORCHESTRATOR_ID, spanType: 'Agent',
          label: 'Manager Review', status: 'Running', startedAt: now,
          completedAt: null, durationMs: null, costUsd: null,
        })
      }
      const s = next.byId[MANAGER_REVIEW_ID]
      patch(next, MANAGER_REVIEW_ID, {
        status: 'Completed', completedAt: now, durationMs: msBetween(s.startedAt, now),
      })
      return next
    }

    case 'task_completed': {
      ensureOrchestrator(next, now)
      const orch = next.byId[ORCHESTRATOR_ID]
      if (orch.status !== 'Failed' && orch.status !== 'Completed') {
        patch(next, ORCHESTRATOR_ID, {
          status: 'Completed', completedAt: now, durationMs: msBetween(orch.startedAt, now),
        })
      }
      return next
    }

    case 'task_failed': {
      ensureOrchestrator(next, now)
      const orch = next.byId[ORCHESTRATOR_ID]
      patch(next, ORCHESTRATOR_ID, {
        status: 'Failed', completedAt: now, durationMs: msBetween(orch.startedAt, now),
      })
      return next
    }

    default:
      // approval_required, task_approved, task_rejected, message_written,
      // agent_retry, checkpoint_saved — no graph effect.
      return state
  }
}
