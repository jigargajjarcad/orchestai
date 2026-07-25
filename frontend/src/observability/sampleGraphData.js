// frontend/src/observability/sampleGraphData.js
//
// Hand-constructed sample data for Task 2's verification harness
// (GraphHarness.jsx) — NOT real span data (Task 3 supplies the real
// span-tree -> graph-position translation; Task 5 wires it to
// ObservabilityPage.jsx). Two sections:
//
//   - `statusMatrixGraph`: one AgentNode + one ToolCallNode at each of the
//     five `statusColors` states, side by side — direct coverage of
//     acceptance criterion 1 ("both node types render correctly at all five
//     states") as a single glanceable grid.
//   - `composedTraceGraph`: a small, realistic parent/child trace (an
//     Orchestrator dispatching Research/Code/Data/Writer/Browser, two of
//     which dispatch a tool call) demonstrating the required edge states
//     (§3b: completed/running/failed) and the per-node span-extent mini-bar
//     showing genuine overlap between parallel branches (§3a.3).
//
// `extentPct.offset`/`extentPct.width` are 0-100 numbers, trace-relative —
// exactly what Task 3's layout math will compute from real
// StartedAt/DurationMs vs. traceStart/traceDurationMs (SpanRow's own
// offsetPct/widthPct math). Hand-picked here, not derived from timestamps,
// per this task's scope (brief: "drive the mini-bar from props/hand-
// constructed sample values").

import { agentColors } from './timelineTheme'

const STATUSES = ['Pending', 'Running', 'Completed', 'Failed', 'Skipped']

const DURATION_BY_STATUS = {
  Pending: null,
  Running: null, // DurationMs is only known once a span completes (DTO: int?)
  Completed: 4200,
  Failed: 1830,
  Skipped: null,
}

export const statusMatrixGraph = {
  nodes: STATUSES.flatMap((status, i) => [
    {
      id: `matrix-agent-${status}`,
      type: 'agentNode',
      position: { x: 0, y: i * 110 },
      data: {
        label: 'Research',
        agentName: 'Research',
        status,
        durationMs: DURATION_BY_STATUS[status],
        costUsd: status === 'Completed' || status === 'Failed' ? 0.014231 : null,
        extentPct: { offset: i * 12, width: 18 + i * 6 },
      },
    },
    {
      id: `matrix-tool-${status}`,
      type: 'toolCallNode',
      position: { x: 320, y: i * 110 },
      data: {
        label: 'web_search',
        status,
        durationMs: DURATION_BY_STATUS[status] != null ? Math.round(DURATION_BY_STATUS[status] / 3) : null,
        extentPct: { offset: i * 12 + 4, width: 12 + i * 4 },
      },
    },
  ]),
  edges: [],
}

const TRACE_X = 700

export const composedTraceGraph = {
  nodes: [
    {
      id: 'trace-orchestrator',
      type: 'agentNode',
      position: { x: TRACE_X, y: 720 },
      data: {
        label: 'Orchestrator',
        agentName: 'Orchestrator',
        status: 'Completed',
        durationMs: 6100,
        costUsd: 0.002104,
        extentPct: { offset: 0, width: 100 },
      },
    },
    {
      id: 'trace-research',
      type: 'agentNode',
      position: { x: TRACE_X + 260, y: 600 },
      data: {
        label: 'Research',
        agentName: 'Research',
        status: 'Completed',
        durationMs: 1900,
        costUsd: 0.008021,
        extentPct: { offset: 6, width: 31 },
      },
    },
    {
      id: 'trace-code',
      type: 'agentNode',
      position: { x: TRACE_X + 280, y: 760 },
      data: {
        label: 'Code',
        agentName: 'Code',
        status: 'Running',
        durationMs: null,
        costUsd: null,
        extentPct: { offset: 22, width: 55 },
      },
    },
    {
      id: 'trace-data',
      type: 'agentNode',
      position: { x: TRACE_X + 260, y: 920 },
      data: {
        label: 'Data',
        agentName: 'Data',
        status: 'Failed',
        durationMs: 1120,
        costUsd: 0.003654,
        extentPct: { offset: 15, width: 20 },
      },
    },
    {
      id: 'trace-writer',
      type: 'agentNode',
      position: { x: TRACE_X + 260, y: 1080 },
      data: {
        label: 'Writer',
        agentName: 'Writer',
        status: 'Pending',
        durationMs: null,
        costUsd: null,
        extentPct: { offset: 78, width: 20 },
      },
    },
    {
      id: 'trace-browser',
      type: 'agentNode',
      position: { x: TRACE_X + 260, y: 1240 },
      data: {
        label: 'Browser',
        agentName: 'Browser',
        status: 'Skipped',
        durationMs: null,
        costUsd: null,
        extentPct: { offset: 0, width: 0 },
      },
    },
    {
      id: 'trace-research-search',
      type: 'toolCallNode',
      position: { x: TRACE_X + 560, y: 610 },
      data: {
        label: 'web_search',
        status: 'Completed',
        durationMs: 640,
        extentPct: { offset: 10, width: 14 },
      },
    },
    {
      id: 'trace-code-write',
      type: 'toolCallNode',
      position: { x: TRACE_X + 580, y: 772 },
      data: {
        label: 'write_file',
        status: 'Running',
        durationMs: null,
        extentPct: { offset: 38, width: 22 },
      },
    },
  ],
  edges: [
    {
      id: 'e-orch-research',
      source: 'trace-orchestrator',
      target: 'trace-research',
      type: 'traceEdge',
      data: { edgeStatus: 'completed', identityColor: agentColors.Orchestrator },
    },
    {
      id: 'e-orch-code',
      source: 'trace-orchestrator',
      target: 'trace-code',
      type: 'traceEdge',
      data: { edgeStatus: 'running', identityColor: agentColors.Orchestrator },
    },
    {
      id: 'e-orch-data',
      source: 'trace-orchestrator',
      target: 'trace-data',
      type: 'traceEdge',
      data: { edgeStatus: 'failed', identityColor: agentColors.Orchestrator },
    },
    {
      id: 'e-orch-writer',
      source: 'trace-orchestrator',
      target: 'trace-writer',
      type: 'traceEdge',
      data: { edgeStatus: 'pending', identityColor: agentColors.Orchestrator },
    },
    {
      id: 'e-orch-browser',
      source: 'trace-orchestrator',
      target: 'trace-browser',
      type: 'traceEdge',
      data: { edgeStatus: 'pending', identityColor: agentColors.Orchestrator },
    },
    {
      id: 'e-research-search',
      source: 'trace-research',
      target: 'trace-research-search',
      type: 'traceEdge',
      data: { edgeStatus: 'completed', identityColor: agentColors.Research },
    },
    {
      id: 'e-code-write',
      source: 'trace-code',
      target: 'trace-code-write',
      type: 'traceEdge',
      data: { edgeStatus: 'running', identityColor: agentColors.Code },
    },
  ],
}
