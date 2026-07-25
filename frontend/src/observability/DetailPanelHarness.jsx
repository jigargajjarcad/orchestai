// frontend/src/observability/DetailPanelHarness.jsx
//
// Local verification harness for Task 4's DetailPanel (plan §7 Task 4, §6
// item 2). NOT wired into the running app's routes (App.jsx/routing
// untouched, same discipline as Task 2/3's GraphHarness.jsx) — a standalone
// component supporting:
//   - isolated content-correctness checks (SSR/`ssrLoadModule`) of the three
//     required span-type examples (agent / tool-call / failed), and
//   - a live dev-server check of the open/close focus-management behavior
//     §6 item 2 requires: one stand-in "node" trigger button per sample
//     span. Clicking (or keyboard-activating) a trigger opens DetailPanel
//     with that span and moves focus into the panel (§6 item 2, "opening
//     moves focus into the panel"); closing (Escape, the panel's own close
//     button, or clicking the backdrop) returns focus to the trigger button
//     that opened it, via the same `returnFocusRef` mechanism Task 5's real
//     React Flow node click handler will use.
//
// Task 5 integrates the real thing by rendering <DetailPanel> the same way
// — capture the clicked node's DOM element in a ref, set the selected span
// state, pass both to DetailPanel — driven by the real canvas's click/
// keyboard-activation handlers instead of these stand-in buttons.

import { useRef, useState } from 'react'
import { DetailPanel } from './DetailPanel'
import { agentSpanSample, toolCallSpanSample, failedSpanSample } from './sampleDetailData'
import { surface, typography } from './timelineTheme'
import { colors as sharedColors } from '../theme/tokens'

const SAMPLES = [
  { key: 'agent', title: 'Agent span — Research (Completed)', span: agentSpanSample },
  { key: 'tool', title: 'Tool-call span — web_search (Completed)', span: toolCallSpanSample },
  { key: 'failed', title: 'Failed span — Data (Failed)', span: failedSpanSample },
]

export function DetailPanelHarness() {
  const [openKey, setOpenKey] = useState(null)
  const triggerRefs = useRef({})
  const returnFocusRef = useRef(null)

  const openSample = SAMPLES.find((sample) => sample.key === openKey) ?? null

  function handleTriggerActivate(key) {
    // Mirrors what Task 5's real node click/keyboard-activation handler
    // will do: capture the originating element before opening, so the
    // panel can return focus to it on close.
    returnFocusRef.current = triggerRefs.current[key]
    setOpenKey(key)
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        background: surface.canvas,
        padding: 40,
        display: 'flex',
        flexDirection: 'column',
        gap: 16,
      }}
    >
      <h1 style={{ fontFamily: typography.uiFontFamily, color: sharedColors.text, fontSize: 18 }}>
        DetailPanel harness — stand-in node triggers
      </h1>
      <p style={{ fontFamily: typography.uiFontFamily, color: sharedColors.subtext0, fontSize: 13, maxWidth: 520 }}>
        Each button below stands in for a Task 5 React Flow node. Click one (or Tab to it and
        press Enter/Space) to open the detail panel for that sample span; focus should land on
        the panel's close button. Close via Escape, the × button, or clicking outside the panel —
        focus should return to the trigger button you started from.
      </p>
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        {SAMPLES.map(({ key, title }) => (
          <button
            key={key}
            ref={(el) => {
              triggerRefs.current[key] = el
            }}
            type="button"
            data-testid={`trigger-${key}`}
            onClick={() => handleTriggerActivate(key)}
            style={{
              padding: '10px 14px',
              borderRadius: 8,
              border: `1px solid ${surface.panelBorder}`,
              background: surface.card,
              color: sharedColors.text,
              fontFamily: typography.uiFontFamily,
              fontSize: 13,
              cursor: 'pointer',
            }}
          >
            {title}
          </button>
        ))}
      </div>

      {openSample && (
        <DetailPanel span={openSample.span} onClose={() => setOpenKey(null)} returnFocusRef={returnFocusRef} />
      )}
    </div>
  )
}
