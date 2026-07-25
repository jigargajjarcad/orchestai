// frontend/src/observability/format.js
//
// Small formatting helpers for the Observability Execution Graph node
// components (Task 2). Deliberately mirrors ObservabilityPage.jsx's own
// local `fmtDuration`/`fmtCost` (SpanRow) output format exactly, so the
// figures on a graph node read identically to the equivalent List-view row
// once Task 5 wires both views to the same data — but is not imported from
// there (ObservabilityPage.jsx is out of scope to touch or import from for
// this task; see task-2-brief.md scope boundaries).

export function formatDuration(ms) {
  if (ms == null) return '—'
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

export function formatCost(v) {
  if (v == null) return null
  return `$${Number(v).toFixed(6)}`
}
