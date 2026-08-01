// frontend/src/components/Toggle.jsx
//
// R1 Task 5 (master plan §5: "a `Toggle`/segmented-control [generalizing]
// the slice built one inline in `TimelineView`; extract/generalize it") — a
// NEW shared primitive generalizing the inline List/Graph segmented control
// ObservabilityPage.jsx (~lines 333-376) built for its `viewMode` state
// (`toggleButtonStyle(active)`, `aria-pressed`, a `role="group"` wrapper).
// ObservabilityPage.jsx is read-only reference here and is NOT modified —
// this is the reusable version other call sites (Phase 2) will consume.
//
// Reinterpretation, not a verbatim copy: the source used `role="group"` +
// `aria-pressed` buttons (WAI-ARIA's "Toggle Button Group" pattern, correct
// for a set of *independent* on/off toggles). A segmented control is instead
// a single mutually-exclusive choice from a small fixed set — semantically
// the WAI-ARIA "Radio Group" pattern — so this component uses
// `role="radiogroup"` with `role="radio"`/`aria-checked` children instead.
// That is a deliberate upgrade for a *reusable* primitive (correct native
// semantics + the standard roving-tabindex/arrow-key contract AT users
// already expect from a radio group), not a defect in the original inline
// control (which was fine as a one-off).
//
// ---------------------------------------------------------------------------
// a11y contract (required, per task brief — this is the crux of the component)
// ---------------------------------------------------------------------------
// - Container: `role="radiogroup"`. Callers MUST supply an accessible name —
//   pass `aria-label="..."` (or `aria-labelledby`), forwarded via `...rest`
//   onto the group `div` — matching the source control's
//   `aria-label="Timeline view mode"`. Without one, the group is unnamed for
//   assistive tech; this component does not invent a default label since it
//   has no idea what the segments represent.
// - Each segment: a real `<button type="button">` with `role="radio"` and
//   `aria-checked={selected}` (never a native `<input type="radio">` — a
//   filled-segment visual needs full style control the native widget can't
//   give without heavy re-skinning, same reasoning Button/NavItem already
//   apply to their own custom controls).
// - Roving tabindex: only the selected segment (or the first segment, if
//   nothing is selected yet) is `tabIndex={0}`; every other segment is
//   `tabIndex={-1}`. This is the standard radiogroup keyboard contract — Tab
//   enters/exits the group once, not once per segment.
// - Arrow-key navigation: ArrowRight/ArrowDown move selection to the next
//   segment, ArrowLeft/ArrowUp to the previous, Home/End jump to the
//   first/last; movement wraps around both ends. Per the native radio-group
//   convention, moving the arrow *also* selects (fires `onChange`) — it is
//   not focus-only navigation — and moves DOM focus to the newly selected
//   segment so the roving tabindex and visible focus ring track the
//   selection.
// - Visible keyboard focus: `className="focus-ring"` on every segment,
//   reusing the shared `.focus-ring:focus-visible` CSS rule (index.css) —
//   the same mechanism every other primitive uses; not a new focus system.
// - Selected-segment fill: background is `colors.traceBlue` at
//   `interactiveState.selectedAlphaSuffix` (18% alpha) over transparent, text
//   `colors.traceBlue`, matching the "active segment filled" intent of the
//   observability toggle's `active ? traceBlue20 : transparent` rule (here
//   routed through the token-driven alpha-suffix idiom instead of the
//   source's hand-rolled `20` literal, same correction Task 4 already made
//   to StatusBadge/Button/NavItem elsewhere in R1).
//
// API: `Toggle({ options, value, onChange, style, ...rest })`
//   - `options`: array of `{ value, label }`, 2 or more segments supported.
//   - `value`: the currently-selected option's `value`.
//   - `onChange(nextValue)`: fired on click or on arrow-key selection.
//   - `style`: merges LAST onto the group container, overriding every
//     computed property — matches every other primitive's override pattern.
//     (Per-segment styling is intentionally not independently overridable
//     via props; a caller needing that has outgrown this primitive.)
//   - `...rest`: forwarded onto the group `div` — this is how a caller
//     supplies the required `aria-label`/`aria-labelledby`.

import { useRef } from 'react'
import { colors, radii, spacing, interactiveState, typography } from '../theme/tokens'

export function Toggle({ options, value, onChange, style, ...rest }) {
  const segmentRefs = useRef([])
  const selectedIndex = options.findIndex(option => option.value === value)

  function selectAt(index) {
    const wrapped = ((index % options.length) + options.length) % options.length
    onChange(options[wrapped].value)
    segmentRefs.current[wrapped]?.focus()
  }

  function handleKeyDown(event, index) {
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        event.preventDefault()
        selectAt(index + 1)
        break
      case 'ArrowLeft':
      case 'ArrowUp':
        event.preventDefault()
        selectAt(index - 1)
        break
      case 'Home':
        event.preventDefault()
        selectAt(0)
        break
      case 'End':
        event.preventDefault()
        selectAt(options.length - 1)
        break
      default:
        break
    }
  }

  return (
    <div
      role="radiogroup"
      style={{ display: 'inline-flex', gap: spacing.xs, ...style }}
      {...rest}
    >
      {options.map((option, index) => {
        const selected = option.value === value
        const focusable = selected || (selectedIndex === -1 && index === 0)

        return (
          <button
            key={option.value}
            ref={el => {
              segmentRefs.current[index] = el
            }}
            type="button"
            role="radio"
            aria-checked={selected}
            tabIndex={focusable ? 0 : -1}
            className="focus-ring touch-target"
            onClick={() => onChange(option.value)}
            onKeyDown={event => handleKeyDown(event, index)}
            style={{
              padding: `${spacing.xs} ${spacing.md}`,
              borderRadius: radii.lg,
              border: `1px solid ${selected ? colors.traceBlue : colors.surface0}`,
              background: selected ? `${colors.traceBlue}${interactiveState.selectedAlphaSuffix}` : 'transparent',
              color: selected ? colors.traceBlue : colors.subtext0,
              ...typography.label,
              fontWeight: selected ? 700 : 400,
              cursor: 'pointer',
            }}
          >
            {option.label}
          </button>
        )
      })}
    </div>
  )
}
