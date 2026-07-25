// frontend/src/observability/sampleDetailData.js
//
// Hand-constructed span-detail sample data for Task 4's DetailPanel
// verification harness (DetailPanelHarness.jsx) — NOT real span data, same
// discipline as Task 2/3's sampleGraphData.js. Each entry is shaped like the
// extended span object DetailPanel.jsx consumes: Task 2's own node `data`
// contract (`label`/`agentName`/`status`/`durationMs`/`costUsd`) plus the
// additional fields this task's panel introduces (`spanType`,
// `inputTokens`/`outputTokens`, and — depending on span type —
// `errorCategory`/`errorMessage` or `toolInput`/`toolOutput`).
//
// Honest scope note: the real backend DTO (`TimelineSpanDto`,
// src/OrchestAI.Application/Queries/GetExecutionTimeline/
// GetExecutionTimelineResponse.cs) genuinely carries `InputTokens`/
// `OutputTokens`/`ErrorCategory` today, but has NO `ErrorMessage`,
// `ToolInput`, or `ToolOutput` fields — those three are hand-constructed
// here to demonstrate DetailPanel's full content contract (matching this
// task's acceptance criteria). When Task 5 wires this panel to real data,
// those three will simply render their "no data available" fallback until/
// unless a future backend change adds them; DetailPanel already handles
// that (see its `errorMessage ?? 'No additional error detail available.'`
// and the tool-I/O section only rendering when at least one of
// `toolInput`/`toolOutput` is non-null).

export const agentSpanSample = {
  spanType: 'AgentExecution',
  label: 'Research',
  agentName: 'Research',
  status: 'Completed',
  durationMs: 1900,
  costUsd: 0.008021,
  inputTokens: 1284,
  outputTokens: 512,
}

export const toolCallSpanSample = {
  spanType: 'ToolCall',
  label: 'web_search',
  status: 'Completed',
  durationMs: 640,
  costUsd: 0.000412,
  inputTokens: 96,
  outputTokens: 340,
  toolInput: JSON.stringify({ query: 'NHL athlete workload management platforms 2026' }, null, 2),
  toolOutput: JSON.stringify(
    {
      results: [
        { title: 'Edge10 Group — Athlete Performance Management', url: 'https://example.com/edge10' },
      ],
    },
    null,
    2
  ),
}

export const failedSpanSample = {
  spanType: 'AgentExecution',
  label: 'Data',
  agentName: 'Data',
  status: 'Failed',
  durationMs: 1120,
  costUsd: 0.003654,
  inputTokens: 640,
  outputTokens: 0,
  errorCategory: 'ToolExecutionError',
  errorMessage: 'DatabaseTool query timed out after 30000ms while aggregating athlete workload metrics.',
}
