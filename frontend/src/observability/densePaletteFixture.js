// frontend/src/observability/densePaletteFixture.js
//
// R1 Task 1 — dense, backend-independent synthetic graph fixture for the
// palette-density stress test (master plan §3.2/§11 "palette-density
// validation gap"). Verification-only: NOT wired into any product route
// (App.jsx/ObservabilityPage.jsx/routing untouched), same discipline as
// `sampleGraphData.js` and `GraphHarness.jsx`.
//
// Why this exists: `sampleGraphData.js` covers the palette's *dimensions*
// (all 6 identities, all 5 statuses, both node types, completed/running/
// failed edges, one parallel fan-out) but is small (~10-11 nodes) — too
// sparse to reveal color competition/ambiguity/noise at realistic density.
// This fixture is deliberately denser and exercises, simultaneously:
//   - 47 spans total (22 agent spans + 25 tool-call spans)
//   - every one of the 6 agent identities repeated 3-6x each (NOT one-of-
//     each — the point is to see e.g. five Research cards and six Code
//     cards competing for attention at once)
//   - all 5 statuses (Pending/Running/Completed/Failed/Skipped) on BOTH
//     node types
//   - a wide, genuinely-parallel fan-out (16 siblings dispatched directly
//     under the root Orchestrator, overlapping start times)
//   - nested/deep hierarchy: agents that themselves dispatch sub-agents
///     (not just tool calls), reaching depth 4 (5 levels: root -> ... ->
//     deepest tool call) via two different paths
//   - successful AND failed paths, including one failed-tool-then-retry-
//     succeeded sequence (browser-3-nav -> browser-3-nav-retry)
//
// Span shape matches `TimelineSpanDto` exactly (spanId/parentSpanId/
// spanType/label/status/startedAt/completedAt/durationMs/costUsd) — the
// same shape `computeGraphLayout` (layout.js) consumes, so this fixture
// drives the REAL layout engine + REAL AgentNode/ToolCallNode/TraceEdge
// components unchanged (no hand-picked positions/extentPct, unlike
// sampleGraphData.js — those are derived from these timestamps by
// `computeGraphLayout`, exactly as they would be from a real trace).

const BASE_MS = Date.parse('2026-07-26T10:00:00.000Z')

/** ISO timestamp `offsetMs` after the fixture's base time. */
function t(offsetMs) {
  return new Date(BASE_MS + offsetMs).toISOString()
}

/**
 * Builds one span. `startOffsetMs`/`durationMs` of `null` produce a span
 * with no `startedAt`/`completedAt` (Pending/Skipped spans that never ran —
 * matches how `ObservabilityPage.jsx`'s real data models "hasn't started
 * yet": `offsetPct`/`computeOffsetPct` both treat a falsy `startedAt` as 0,
 * exactly the code path this exercises).
 */
function span({ id, parent, type, label, status, startOffsetMs, durationMs, costUsd = null }) {
  const hasStarted = startOffsetMs != null
  const hasCompleted = hasStarted && durationMs != null
  return {
    spanId: id,
    parentSpanId: parent,
    spanType: type, // 'Agent' | 'ToolCall'
    label,
    status,
    startedAt: hasStarted ? t(startOffsetMs) : null,
    completedAt: hasCompleted ? t(startOffsetMs + durationMs) : null,
    durationMs: hasCompleted ? durationMs : null,
    costUsd,
  }
}

const agent = (fields) => span({ ...fields, type: 'Agent' })
const tool = (fields) => span({ ...fields, type: 'ToolCall' })

export const denseSpans = [
  // ---------------------------------------------------------------------
  // Root
  // ---------------------------------------------------------------------
  agent({ id: 'orchestrator', parent: null, label: 'Orchestrator', status: 'Completed', startOffsetMs: 0, durationMs: 9400, costUsd: 0.00612 }),

  // ---------------------------------------------------------------------
  // Depth 1 — 16-way parallel fan-out directly under the root, overlapping
  // start times (genuinely parallel, not just visually stacked).
  // ---------------------------------------------------------------------
  agent({ id: 'research-1', parent: 'orchestrator', label: 'Research', status: 'Completed', startOffsetMs: 80, durationMs: 3820, costUsd: 0.01180 }),
  agent({ id: 'research-2', parent: 'orchestrator', label: 'Research', status: 'Running', startOffsetMs: 140, durationMs: null }),
  agent({ id: 'research-3', parent: 'orchestrator', label: 'Research', status: 'Failed', startOffsetMs: 190, durationMs: 1240, costUsd: 0.00390 }),
  agent({ id: 'research-5', parent: 'orchestrator', label: 'Research', status: 'Completed', startOffsetMs: 260, durationMs: 2960, costUsd: 0.00910 }),

  agent({ id: 'code-1', parent: 'orchestrator', label: 'Code', status: 'Completed', startOffsetMs: 310, durationMs: 5080, costUsd: 0.02140 }),
  agent({ id: 'code-2', parent: 'orchestrator', label: 'Code', status: 'Running', startOffsetMs: 360, durationMs: null }),
  agent({ id: 'code-3', parent: 'orchestrator', label: 'Code', status: 'Pending', startOffsetMs: null, durationMs: null }),
  agent({ id: 'code-5', parent: 'orchestrator', label: 'Code', status: 'Failed', startOffsetMs: 420, durationMs: 980, costUsd: 0.00280 }),
  agent({ id: 'code-6', parent: 'orchestrator', label: 'Code', status: 'Completed', startOffsetMs: 470, durationMs: 4110, costUsd: 0.01760 }),

  agent({ id: 'data-1', parent: 'orchestrator', label: 'Data', status: 'Completed', startOffsetMs: 520, durationMs: 2340, costUsd: 0.00640 }),
  agent({ id: 'data-2', parent: 'orchestrator', label: 'Data', status: 'Failed', startOffsetMs: 560, durationMs: 890, costUsd: 0.00190 }),
  agent({ id: 'data-4', parent: 'orchestrator', label: 'Data', status: 'Running', startOffsetMs: 610, durationMs: null }),

  agent({ id: 'writer-1', parent: 'orchestrator', label: 'Writer', status: 'Completed', startOffsetMs: 660, durationMs: 3010, costUsd: 0.00520 }),
  agent({ id: 'writer-2', parent: 'orchestrator', label: 'Writer', status: 'Skipped', startOffsetMs: null, durationMs: null }),

  agent({ id: 'browser-1', parent: 'orchestrator', label: 'Browser', status: 'Completed', startOffsetMs: 700, durationMs: 4560, costUsd: 0.00310 }),
  agent({ id: 'browser-2', parent: 'orchestrator', label: 'Browser', status: 'Pending', startOffsetMs: null, durationMs: null }),

  // ---------------------------------------------------------------------
  // Depth 2 — tool calls + nested sub-agents under the depth-1 agents.
  // The nested sub-agents (code-4, browser-3, research-4, data-3) are what
  // push identity-repetition counts up further AND create the deep-
  // hierarchy paths below.
  // ---------------------------------------------------------------------
  tool({ id: 'research-1-search', parent: 'research-1', label: 'web_search', status: 'Completed', startOffsetMs: 200, durationMs: 640, costUsd: 0.00040 }),
  tool({ id: 'research-1-fetch', parent: 'research-1', label: 'fetch_url', status: 'Completed', startOffsetMs: 900, durationMs: 510, costUsd: 0.00020 }),
  agent({ id: 'code-4', parent: 'research-1', label: 'Code', status: 'Completed', startOffsetMs: 1500, durationMs: 2200, costUsd: 0.00870 }),

  tool({ id: 'research-2-search', parent: 'research-2', label: 'web_search', status: 'Running', startOffsetMs: 260, durationMs: null }),
  tool({ id: 'research-3-search', parent: 'research-3', label: 'web_search', status: 'Failed', startOffsetMs: 300, durationMs: 520, costUsd: 0.00030 }),
  tool({ id: 'research-4-search', parent: null, label: 'web_search', status: 'Completed', startOffsetMs: 1200, durationMs: 640, costUsd: 0.00040 }), // parent set below (research-4)
  tool({ id: 'research-5-search', parent: 'research-5', label: 'web_search', status: 'Completed', startOffsetMs: 340, durationMs: 700, costUsd: 0.00040 }),

  tool({ id: 'code-1-write', parent: 'code-1', label: 'write_file', status: 'Completed', startOffsetMs: 420, durationMs: 310, costUsd: 0.00060 }),
  tool({ id: 'code-1-test', parent: 'code-1', label: 'run_tests', status: 'Completed', startOffsetMs: 800, durationMs: 1650, costUsd: 0.00090 }),
  agent({ id: 'browser-3', parent: 'code-1', label: 'Browser', status: 'Completed', startOffsetMs: 2500, durationMs: 2400, costUsd: 0.00280 }),

  tool({ id: 'code-2-write', parent: 'code-2', label: 'write_file', status: 'Running', startOffsetMs: 470, durationMs: null }),
  tool({ id: 'code-2-test', parent: 'code-2', label: 'run_tests', status: 'Pending', startOffsetMs: null, durationMs: null }),

  tool({ id: 'code-5-write', parent: 'code-5', label: 'write_file', status: 'Failed', startOffsetMs: 500, durationMs: 380, costUsd: 0.00030 }),

  tool({ id: 'code-6-write', parent: 'code-6', label: 'write_file', status: 'Completed', startOffsetMs: 550, durationMs: 340, costUsd: 0.00060 }),
  tool({ id: 'code-6-test', parent: 'code-6', label: 'run_tests', status: 'Completed', startOffsetMs: 950, durationMs: 1400, costUsd: 0.00090 }),

  tool({ id: 'data-1-query', parent: 'data-1', label: 'query_db', status: 'Completed', startOffsetMs: 600, durationMs: 480, costUsd: 0.00050 }),
  agent({ id: 'research-4', parent: 'data-1', label: 'Research', status: 'Completed', startOffsetMs: 1150, durationMs: 1500, costUsd: 0.00610 }),

  tool({ id: 'data-2-query', parent: 'data-2', label: 'query_db', status: 'Failed', startOffsetMs: 620, durationMs: 260, costUsd: 0.00020 }),
  tool({ id: 'data-4-query', parent: 'data-4', label: 'query_db', status: 'Running', startOffsetMs: 680, durationMs: null }),

  tool({ id: 'writer-1-draft', parent: 'writer-1', label: 'draft_doc', status: 'Completed', startOffsetMs: 730, durationMs: 2200, costUsd: 0.00070 }),
  tool({ id: 'writer-2-draft', parent: 'writer-2', label: 'draft_doc', status: 'Skipped', startOffsetMs: null, durationMs: null }),

  tool({ id: 'browser-1-nav', parent: 'browser-1', label: 'navigate', status: 'Completed', startOffsetMs: 780, durationMs: 410, costUsd: 0.00020 }),
  tool({ id: 'browser-1-shot', parent: 'browser-1', label: 'screenshot', status: 'Completed', startOffsetMs: 1250, durationMs: 890, costUsd: 0.00030 }),
  agent({ id: 'data-3', parent: 'browser-1', label: 'Data', status: 'Completed', startOffsetMs: 2200, durationMs: 1900, costUsd: 0.00450 }),

  // ---------------------------------------------------------------------
  // Depth 3 — tool calls under the depth-2 nested sub-agents, plus one
  // more nested sub-agent (writer-3) that goes one level deeper still.
  // ---------------------------------------------------------------------
  tool({ id: 'code-4-write', parent: 'code-4', label: 'write_file', status: 'Completed', startOffsetMs: 1700, durationMs: 640, costUsd: 0.00060 }),

  tool({ id: 'browser-3-nav', parent: 'browser-3', label: 'navigate', status: 'Failed', startOffsetMs: 2600, durationMs: 380, costUsd: 0.00020 }),

  tool({ id: 'data-3-query', parent: 'data-3', label: 'query_db', status: 'Completed', startOffsetMs: 2350, durationMs: 420, costUsd: 0.00040 }),
  agent({ id: 'writer-3', parent: 'data-3', label: 'Writer', status: 'Running', startOffsetMs: 2900, durationMs: null }),

  // ---------------------------------------------------------------------
  // Depth 4 — deepest nodes: a failed tool call immediately retried and
  // succeeding (browser-3-nav-retry), and a still-running tool call
  // dispatched by the deepest nested agent (writer-3-draft). Two
  // independent paths reach depth 4 (5 levels total from the root).
  // ---------------------------------------------------------------------
  tool({ id: 'browser-3-nav-retry', parent: 'browser-3-nav', label: 'navigate_retry', status: 'Completed', startOffsetMs: 3050, durationMs: 340, costUsd: 0.00020 }),
  tool({ id: 'writer-3-draft', parent: 'writer-3', label: 'draft_doc', status: 'Running', startOffsetMs: 3150, durationMs: null }),
]

// `research-4-search`'s parent is set after `research-4` is defined above it
// in read order but declared earlier in the array for grouping-by-branch
// readability — patched here rather than reordering the array, so the file
// stays organized depth-first/branch-first for human review.
denseSpans.find(s => s.spanId === 'research-4-search').parentSpanId = 'research-4'

// ---------------------------------------------------------------------------
// Fixture self-description (for the evaluation writeup + sanity checks) —
// NOT consumed by layout.js/the harness, just documents what this fixture
// is asserting about itself so a reviewer can cross-check counts without
// re-deriving them by hand.
// ---------------------------------------------------------------------------
export const denseFixtureSummary = {
  totalSpans: denseSpans.length, // 47
  agentSpans: denseSpans.filter(s => s.spanType === 'Agent').length, // 22
  toolCallSpans: denseSpans.filter(s => s.spanType === 'ToolCall').length, // 25
  identityCounts: denseSpans
    .filter(s => s.spanType === 'Agent')
    .reduce((acc, s) => ({ ...acc, [s.label]: (acc[s.label] ?? 0) + 1 }), {}),
  statusCounts: denseSpans.reduce((acc, s) => ({ ...acc, [s.status]: (acc[s.status] ?? 0) + 1 }), {}),
}
