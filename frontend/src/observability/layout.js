// frontend/src/observability/layout.js
//
// Task 3 (plan §3a) — the layout engine: pure data transformation from a
// flat span array (the `/tasks/{id}/timeline` DTO's `spans`) into React
// Flow's `{nodes, edges}` shape with computed positions. NOT wired into
// ObservabilityPage.jsx (Task 5's job) — this module is import-free of any
// app routing/rendering concern and is fully unit-testable without a
// browser.
//
// §3a.2's binding decision, implemented exactly:
//   - X comes from real timing (`startedAt` against the trace's
//     `traceStart`/`traceDurationMs`) — the SAME offset math
//     `ObservabilityPage.jsx`'s `SpanRow` already uses. Two spans that
//     started at the same moment get the same (or near-same) X.
//   - Y comes from HIERARCHY (`parentSpanId` → depth / traversal order),
//     per ADR-011 — never inferred from timestamps. Every node gets a
//     unique row assigned via depth-first traversal of the span tree
//     (parent before children, siblings in `buildSpanTree`'s own
//     start-time order), so two nodes can only ever share a Y if they are
//     literally the same traversal slot — impossible, since the row
//     counter strictly increases. This guarantees zero Y-overlap for any
//     tree shape, including the required parallel-fan-out case, without
//     needing a general tree-layout (Reingold-Tilford-style) algorithm.
//   - Span-extent {offset, width} (§3a.3 item 2) — the exact same
//     offsetPct/widthPct math as SpanRow, trace-relative, 0-100.
//   - Edges: parent -> child from parentSpanId, with edgeStatus derived
//     from the child (target) span's status and identityColor from the
//     PARENT (source) span's identity — matching sampleGraphData.js's
//     existing convention (e.g. `e-orch-research`'s identityColor is
//     Orchestrator's, not Research's).
//
// `buildSpanTree`/the traceStart-traceDurationMs computation are
// deliberately REPLICATED here (byte-for-byte equivalent logic), not
// imported from `ObservabilityPage.jsx` — that file is out of scope to
// modify (it doesn't `export` either), matching Task 2's `format.js`'s own
// precedent of replicating rather than importing from an out-of-scope file.

import { colors as sharedColors } from '../theme/tokens'
import { agentColors, toolCallColor } from './timelineTheme'

// ---------------------------------------------------------------------------
// Replicated verbatim from ObservabilityPage.jsx (lines ~131-147, ~214-221).
// Any change to the original must be mirrored here.
// ---------------------------------------------------------------------------

/** Reconstructs the parent/child span tree from spanId/parentSpanId (ADR-011:
 * hierarchy from IDs, never from timestamp ordering). Children are sorted by
 * startedAt purely to decide left-to-right/top-to-bottom sibling ORDER —
 * identical to ObservabilityPage.jsx's own buildSpanTree. */
export function buildSpanTree(spans) {
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

/** Trace-wide start/duration bounds — identical to TimelineView's own
 * useMemo body, minus the `data` wrapper (takes the flat spans array
 * directly). */
export function computeTraceBounds(spans) {
  if (!spans || spans.length === 0) return { traceStart: new Date(), traceDurationMs: 0 }
  const starts = spans.filter(s => s.startedAt).map(s => new Date(s.startedAt))
  const ends = spans.filter(s => s.completedAt).map(s => new Date(s.completedAt))
  const start = new Date(Math.min(...starts))
  const end = new Date(Math.max(...ends, ...starts))
  return { traceStart: start, traceDurationMs: Math.max(end - start, 1) }
}

/** Trace-relative start offset, 0-100 — identical math to SpanRow's
 * offsetPct. */
export function computeOffsetPct(startedAt, traceStart, traceDurationMs) {
  return startedAt && traceDurationMs > 0
    ? ((new Date(startedAt) - traceStart) / traceDurationMs) * 100
    : 0
}

/** Trace-relative duration width, 0-100 — identical math to SpanRow's
 * widthPct (including its 0.5 floor/default for missing duration). */
export function computeWidthPct(durationMs, traceDurationMs) {
  return durationMs && traceDurationMs > 0
    ? Math.max((durationMs / traceDurationMs) * 100, 0.5)
    : 0.5
}

// ---------------------------------------------------------------------------
// New: hierarchy-derived Y (depth + traversal order), never timestamp-derived
// ---------------------------------------------------------------------------

/** Depth-first traversal of the span tree (parent before children, siblings
 * in `buildSpanTree`'s own order), assigning every node a strictly-
 * increasing row index and its tree depth. Because the counter only ever
 * increases, no two nodes can ever receive the same row — including the two
 * children of a parallel fan-out, which is exactly what acceptance
 * criterion (b) requires. */
function assignRows(tree) {
  let rowCounter = 0
  const rowOf = new Map()
  const depthOf = new Map()

  function visit(span, depth) {
    rowOf.set(span.spanId, rowCounter)
    depthOf.set(span.spanId, depth)
    rowCounter += 1
    span.children.forEach(child => visit(child, depth + 1))
  }

  tree.forEach(root => visit(root, 0))
  return { rowOf, depthOf }
}

// ---------------------------------------------------------------------------
// Edge data derivation
// ---------------------------------------------------------------------------

/** Edge status is derived from the CHILD (target) span's own status — a
 * completed edge means the call it represents finished, a running edge
 * means the child is still executing, etc. `Skipped` maps to `pending`
 * (there is no dedicated `skipped` edge treatment in TraceEdge.jsx). */
function edgeStatusFor(status) {
  switch (status) {
    case 'Completed':
      return 'completed'
    case 'Running':
      return 'running'
    case 'Failed':
      return 'failed'
    default:
      return 'pending'
  }
}

/** Identity color is the SOURCE (parent) span's identity — matching
 * sampleGraphData.js's existing convention and SpanRow's own color rule
 * (`span.spanType === 'ToolCall' ? colors.agentMauve : (AGENT_COLORS[span.label]
 * ?? colors.traceBlue)`). */
function identityColorFor(span) {
  return span.spanType === 'ToolCall' ? toolCallColor : (agentColors[span.label] ?? sharedColors.traceBlue)
}

function nodeTypeFor(span) {
  return span.spanType === 'ToolCall' ? 'toolCallNode' : 'agentNode'
}

// ---------------------------------------------------------------------------
// Geometry defaults — pixel scale for the time axis and row spacing.
// Callers (Task 5) may override any of these via `options`.
// ---------------------------------------------------------------------------

export const DEFAULT_ROW_HEIGHT = 140 // > tallest node (AgentNode: 84px) + margin
export const DEFAULT_X_SCALE = 1400 // pixels representing the full trace duration
export const DEFAULT_X_MARGIN = 40

/**
 * Converts a flat span array into React Flow's `{nodes, edges}` shape with
 * computed positions (plan §3a). Pure function — no DOM/React dependency.
 *
 * @param {Array} spans - flat span array (TimelineSpanDto shape: spanId,
 *   parentSpanId, startedAt, completedAt, durationMs, status, spanType,
 *   label, costUsd, ...).
 * @param {Object} [options]
 * @param {Date} [options.traceStart] - override the computed trace start
 *   (mainly for testing determinism); defaults to `computeTraceBounds`.
 * @param {number} [options.traceDurationMs] - override the computed trace
 *   duration; must be supplied together with `traceStart`.
 * @param {number} [options.rowHeight] - vertical pixel spacing between rows.
 * @param {number} [options.xScale] - pixel width representing 100% of the
 *   trace duration.
 * @param {number} [options.xMargin] - left pixel margin before the earliest
 *   span (offset 0%).
 * @returns {{nodes: Array, edges: Array, traceStart: Date, traceDurationMs: number}}
 */
export function computeGraphLayout(spans, options = {}) {
  const {
    traceStart: overrideStart,
    traceDurationMs: overrideDuration,
    rowHeight = DEFAULT_ROW_HEIGHT,
    xScale = DEFAULT_X_SCALE,
    xMargin = DEFAULT_X_MARGIN,
  } = options

  const { traceStart, traceDurationMs } =
    overrideStart != null && overrideDuration != null
      ? { traceStart: overrideStart, traceDurationMs: overrideDuration }
      : computeTraceBounds(spans)

  const tree = buildSpanTree(spans)
  const { rowOf } = assignRows(tree)

  const nodes = []
  const edges = []

  function visit(span, parentSpan) {
    const offset = computeOffsetPct(span.startedAt, traceStart, traceDurationMs)
    const width = computeWidthPct(span.durationMs, traceDurationMs)
    const row = rowOf.get(span.spanId)

    nodes.push({
      id: span.spanId,
      type: nodeTypeFor(span),
      position: {
        x: xMargin + (offset / 100) * xScale,
        y: row * rowHeight,
      },
      data: {
        label: span.label,
        agentName: span.label,
        status: span.status,
        durationMs: span.durationMs ?? null,
        costUsd: span.costUsd ?? null,
        extentPct: { offset, width },
      },
    })

    if (parentSpan) {
      edges.push({
        id: `e-${parentSpan.spanId}-${span.spanId}`,
        source: parentSpan.spanId,
        target: span.spanId,
        type: 'traceEdge',
        // F1.1 — decorative to assistive tech.
        //
        // React Flow gives every edge `role="img"` plus an auto-generated
        // `aria-label` of "Edge from <sourceId> to <targetId>" — raw span ids
        // and, in the Playground, raw GUIDs. Taking edges out of the tab order
        // (`edgesFocusable={false}`) fixed the WCAG 2.4.7 failure but left
        // those labels in the accessibility tree, so a VoiceOver/NVDA user
        // browsing with a virtual cursor still walked three meaningless
        // announcements before reaching the first useful node.
        //
        // Hiding them is correct rather than merely convenient: an edge carries
        // no information that is not already stated, in words, on the nodes it
        // connects — Observability's node `ariaLabel` says "Child of X, running
        // alongside Y", and the graph container names itself. The edge is the
        // visual rendering of that sentence, so it is exactly the decorative
        // case `aria-hidden` exists for. Same reasoning the time-axis tick row
        // already uses in PlaygroundExecutionGraph.
        //
        // `domAttributes` is spread onto the edge wrapper AFTER React Flow's
        // own aria attributes (verified in @xyflow/react 12.11.2), so this
        // reliably wins over the generated role/label.
        domAttributes: { 'aria-hidden': 'true' },
        data: {
          edgeStatus: edgeStatusFor(span.status),
          identityColor: identityColorFor(parentSpan),
        },
      })
    }

    span.children.forEach(child => visit(child, span))
  }

  tree.forEach(root => visit(root, null))

  return { nodes, edges, traceStart, traceDurationMs }
}
