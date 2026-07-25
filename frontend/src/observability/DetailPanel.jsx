// frontend/src/observability/DetailPanel.jsx
//
// Node detail panel (Task 4, plan §7 Task 4 / §6 item 2). Given a selected
// span's data, renders a side panel: a header (identity dot + agent/tool
// name + StatusPill), a stat grid (status/duration/cost/tokens in-out), a
// tool-call input/output section when the span is a tool call, and an error
// section (category + message) when the span failed.
//
// Consumed by Task 5's TimelineView integration: driven entirely by props
// (`span`/`onClose`[/`returnFocusRef`]) so it is testable standalone here
// (DetailPanelHarness.jsx) and wireable to the real React Flow canvas later
// with no change to this file. Task 5's integration only needs to:
//   1. render `<DetailPanel span={selectedSpanData} onClose={() => setSelectedSpanData(null)} />`
//      when a node is clicked/keyboard-activated, setting `selectedSpanData`
//      to that node's extended `data` (see the shape below);
//   2. optionally pass `returnFocusRef` — a ref pointing at the clicked
//      node's DOM element — for an explicit, guaranteed focus-return
//      target. If omitted, the panel auto-captures `document.activeElement`
//      at open time and returns focus there instead, which is correct on
//      its own because React Flow's nodes are natively focusable and are
//      normally the focused element at the moment of click/keyboard
//      activation.
//   3. optionally pass `narrow` (boolean, Task 6) — whether the viewport is
//      at/below the ~768px breakpoint — to switch the panel from a
//      right-side drawer to a full-width bottom sheet. Defaults to `false`.
//
// `span` shape consumed (a superset of AgentNode/ToolCallNode's own `data`,
// plus the fields this panel itself needs):
//   spanType: 'AgentExecution' | 'ToolCall'
//   label, agentName (agent spans only), status, durationMs, costUsd
//   inputTokens, outputTokens
//   errorCategory, errorMessage (failed spans)
//   toolInput, toolOutput (tool-call spans)
//
// Focus management (§6 item 2 — required, not automatic from React Flow):
//   - Opening (this component mounting/receiving a non-null `span`) moves
//     focus onto the panel's close button.
//   - Escape, the close button, or clicking the backdrop all close the
//     panel and return focus to `returnFocusRef.current` if supplied,
//     otherwise the element that was focused immediately before the panel
//     opened.
//
// Focus trap decision (Task 6, carry-forward #3 — Task 4 deliberately
// deferred this): this panel renders `role="dialog" aria-modal="true"`, but
// background content (the graph canvas, its nodes, the SubNav/TaskPicker
// above it) is NOT actually made inert — nothing here applies `inert` or
// `aria-hidden` to it. Without a real trap, Shift+Tab from the panel's only
// focusable element would walk focus back into that live background
// content, which directly contradicts the "modal" claim `aria-modal="true"`
// makes to assistive tech. Decision: implement a real trap (below), rather
// than documenting focus-into + Escape + focus-return as "sufficient" —
// for a component that already asserts `aria-modal`, that assertion needs
// to be true, not just a hint.
//
// Responsive (Task 6, §6/§3a-style-guide requirement): at narrow widths
// (Milestone 2's own ~768px precedent, `App.jsx`), this switches from a
// right-side drawer (beside the graph) to a full-width bottom sheet
// (dropping below it) — a right-anchored 360px column reading as "beside"
// stops making sense once the viewport itself is that narrow. Taken as a
// `narrow` prop (boolean, default `false`) rather than calling
// `useViewportWidth` directly in here: that keeps this component
// SSR/`ssrLoadModule`-safe (the hook reads `window.innerWidth` unguarded,
// which crashes under Node-based isolated rendering) and preserves its
// existing "driven entirely by props, testable standalone" contract — the
// caller (`TimelineView`) already computes the same width/breakpoint for
// its own responsive canvas sizing, so this is the single source of truth,
// not a second one.

import { useEffect, useRef } from 'react'
import { colors as sharedColors } from '../theme/tokens'
import { agentColors, toolCallColor, statusColors, surface, elevation, typography } from './timelineTheme'
import { StatusPill } from './NodeParts'
import { formatDuration, formatCost } from './format'

function getFocusableElements(container) {
  if (!container) return []
  const selector = [
    'a[href]',
    'button:not([disabled])',
    'textarea:not([disabled])',
    'input:not([disabled])',
    'select:not([disabled])',
    '[tabindex]:not([tabindex="-1"])',
  ].join(', ')
  return Array.from(container.querySelectorAll(selector)).filter((el) => el.offsetParent !== null)
}

function StatItem({ label, value }) {
  return (
    <div>
      <div
        style={{
          fontFamily: typography.uiFontFamily,
          ...typography.panelLabel,
          color: sharedColors.subtext0,
          marginBottom: 4,
        }}
      >
        {label}
      </div>
      <div
        style={{
          fontFamily: typography.dataFontFamily,
          fontSize: typography.nodeData.fontSize,
          fontWeight: typography.nodeData.fontWeight,
          color: sharedColors.text,
        }}
      >
        {value}
      </div>
    </div>
  )
}

function CodeBlock({ label, value }) {
  return (
    <div>
      <div
        style={{
          fontFamily: typography.uiFontFamily,
          ...typography.panelLabel,
          color: sharedColors.subtext0,
          marginBottom: 6,
        }}
      >
        {label}
      </div>
      <pre
        // Scrollable region with its own keyboard access (WCAG 2.1.1) — a
        // mouse-only scroll target would otherwise strand keyboard users on
        // long tool input/output. Also participates in DetailPanel's focus
        // trap below (it matches the trap's `[tabindex]` selector).
        tabIndex={0}
        aria-label={label}
        style={{
          margin: 0,
          padding: '8px 10px',
          background: surface.canvas,
          border: `1px solid ${surface.panelBorder}`,
          borderRadius: 6,
          fontFamily: typography.dataFontFamily,
          fontSize: 12,
          color: sharedColors.text,
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          maxHeight: 160,
          overflow: 'auto',
        }}
      >
        {value}
      </pre>
    </div>
  )
}

export function DetailPanel({ span, onClose, returnFocusRef, narrow = false }) {
  const closeButtonRef = useRef(null)
  const previouslyFocusedRef = useRef(null)
  const panelRef = useRef(null)

  useEffect(() => {
    if (!span) return undefined

    previouslyFocusedRef.current = document.activeElement
    closeButtonRef.current?.focus()

    function handleKeyDown(event) {
      if (event.key === 'Escape') {
        event.preventDefault()
        requestClose()
        return
      }

      // Focus trap (carry-forward #3, see header comment): Tab/Shift+Tab
      // cycle only among the panel's own focusable elements. Today that's
      // typically just the close button (plus any tool-input/output <pre>
      // blocks, per CodeBlock's tabIndex above) — with a single focusable
      // element, this simply keeps focus pinned there, which is correct.
      if (event.key === 'Tab') {
        const focusable = getFocusableElements(panelRef.current)
        if (focusable.length === 0) return
        const first = focusable[0]
        const last = focusable[focusable.length - 1]
        const active = document.activeElement
        const isInsidePanel = panelRef.current?.contains(active)

        if (event.shiftKey) {
          if (!isInsidePanel || active === first) {
            event.preventDefault()
            last.focus()
          }
        } else if (!isInsidePanel || active === last) {
          event.preventDefault()
          first.focus()
        }
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
    // `requestClose` reads `onClose`/`returnFocusRef` fresh via closure at
    // call time rather than being tracked as a dependency here — this effect
    // is deliberately scoped to "(re)run only when which span is open
    // changes", i.e. the open/mount moment, not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [span])

  function requestClose() {
    const returnTarget = returnFocusRef?.current ?? previouslyFocusedRef.current
    onClose?.()
    if (returnTarget && typeof returnTarget.focus === 'function' && document.contains(returnTarget)) {
      returnTarget.focus()
    }
  }

  if (!span) return null

  const {
    spanType,
    label,
    agentName,
    status = 'Pending',
    durationMs = null,
    costUsd = null,
    inputTokens = null,
    outputTokens = null,
    errorCategory = null,
    errorMessage = null,
    toolInput = null,
    toolOutput = null,
  } = span

  const isToolCall = spanType === 'ToolCall'
  const isFailed = status === 'Failed'
  const identityColor = isToolCall ? toolCallColor : (agentColors[agentName] ?? statusColors.Pending)
  const cost = formatCost(costUsd)
  const tokens =
    inputTokens != null || outputTokens != null
      ? `${inputTokens != null ? inputTokens.toLocaleString() : '—'} / ${
          outputTokens != null ? outputTokens.toLocaleString() : '—'
        }`
      : '—'

  const headingId = 'observability-detail-panel-heading'

  return (
    <div
      role="presentation"
      onClick={(event) => {
        if (event.target === event.currentTarget) requestClose()
      }}
      style={{
        position: 'fixed',
        inset: 0,
        background: surface.backdrop,
        display: 'flex',
        // Wide: right-side drawer, beside the graph. Narrow (§6/§3a-style-
        // guide requirement, ~768px precedent): bottom sheet, below it.
        justifyContent: narrow ? 'center' : 'flex-end',
        alignItems: narrow ? 'flex-end' : 'stretch',
        zIndex: 40,
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={headingId}
        style={{
          width: narrow ? '100%' : 360,
          maxWidth: narrow ? '100%' : '90vw',
          height: narrow ? '85vh' : '100%',
          maxHeight: '100%',
          background: surface.card,
          borderLeft: narrow ? 'none' : `1px solid ${surface.panelBorder}`,
          borderTop: narrow ? `1px solid ${surface.panelBorder}` : 'none',
          borderRadius: narrow ? '12px 12px 0 0' : 0,
          boxShadow: elevation.panel,
          boxSizing: 'border-box',
          padding: '20px 20px 24px',
          overflowY: 'auto',
          display: 'flex',
          flexDirection: 'column',
          gap: 20,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10 }}>
          <span
            aria-hidden="true"
            style={{
              width: 10,
              height: 10,
              borderRadius: '50%',
              background: identityColor,
              flexShrink: 0,
              marginTop: 4,
            }}
          />
          <div style={{ flex: 1, minWidth: 0 }}>
            <div
              id={headingId}
              style={{
                fontFamily: typography.uiFontFamily,
                ...typography.panelHeading,
                color: sharedColors.text,
                wordBreak: 'break-word',
              }}
            >
              {isToolCall ? `Tool call: ${label}` : label}
            </div>
            {!isToolCall && agentName && (
              <div
                style={{
                  fontFamily: typography.uiFontFamily,
                  fontSize: 12,
                  color: sharedColors.subtext0,
                  marginTop: 2,
                }}
              >
                {agentName}
              </div>
            )}
          </div>
          <StatusPill status={status} />
          <button
            ref={closeButtonRef}
            type="button"
            onClick={requestClose}
            aria-label="Close detail panel"
            style={{
              background: 'transparent',
              border: `1px solid ${surface.panelBorder}`,
              borderRadius: 6,
              color: sharedColors.subtext0,
              cursor: 'pointer',
              width: 26,
              height: 26,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: 14,
              lineHeight: 1,
              flexShrink: 0,
            }}
          >
            ×
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: narrow ? '1fr' : '1fr 1fr', gap: 14 }}>
          <StatItem label="Status" value={status} />
          <StatItem label="Duration" value={formatDuration(durationMs)} />
          <StatItem label="Cost" value={cost ?? '—'} />
          <StatItem label="Tokens (in / out)" value={tokens} />
        </div>

        {isToolCall && (toolInput != null || toolOutput != null) && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            {toolInput != null && <CodeBlock label="Input" value={toolInput} />}
            {toolOutput != null && <CodeBlock label="Output" value={toolOutput} />}
          </div>
        )}

        {isFailed && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div
              style={{
                fontFamily: typography.uiFontFamily,
                ...typography.panelLabel,
                color: statusColors.Failed,
              }}
            >
              Error{errorCategory ? ` · ${errorCategory}` : ''}
            </div>
            <div
              style={{
                fontFamily: typography.uiFontFamily,
                ...typography.panelBody,
                color: sharedColors.text,
                padding: '8px 10px',
                background: `${statusColors.Failed}14`,
                border: `1px solid ${statusColors.Failed}40`,
                borderRadius: 6,
              }}
            >
              {errorMessage ?? 'No additional error detail available.'}
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
