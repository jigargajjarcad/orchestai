// frontend/src/components/NavItem.jsx
//
// Flat text nav button — DESIGN.md §5 Navigation: "flat text buttons in a
// horizontal row, no underline, active state gets a surface0 background
// fill and brightens text from overlay0 to text. No hover-lift, no shadow —
// background-fill is the only interactive cue." Matches the current
// Playground/Memories/Observability/Evals nav buttons in App.jsx exactly
// (background/color/border/radius/padding/fontSize below are transcribed
// from that literal, currently-duplicated implementation).
//
// R1 Task 4 — interactive-state matrix (master plan §3.7):
//   - active ("selected"): background stays exactly `colors.surface0` (not
//     re-derived via `interactiveState.selectedAlphaSuffix` composed onto a
//     base) — the header comment above pins this to a specific, already-
//     shipped solid color, and compositing a translucent tint onto some
//     other base would shift the rendered hex away from what's live today.
//     Preserving that exact, already-correct rendering wins over routing it
//     through the alpha-suffix idiom for its own sake.
//   - hover (non-active only): a subtle background fill using Trace Blue
//     (the design system's One Accent Rule color for all interactive
//     affordances) at `interactiveState.hoverAlphaSuffix`. Text color is
//     unaffected by hover — per the header comment, background-fill is the
//     *only* cue (no hover-lift/shadow), so hover extends that same rule
//     rather than introducing a new one.
//   - disabled: not part of NavItem's public surface (no `disabled` prop) —
//     out of scope.
// Hover uses self-contained React state (inline styles can't express
// `:hover`) via mouse handlers; keyboard focus is untouched, still the
// existing `.focus-ring` class.

import { useState } from 'react'
import { colors, radii, spacing, interactiveState } from '../theme/tokens'

export function NavItem({ active, onClick, children, style, ...rest }) {
  const [hovered, setHovered] = useState(false)
  const showHoverFill = hovered && !active

  return (
    <button
      onClick={onClick}
      aria-current={active ? 'page' : undefined}
      className="focus-ring"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        background: active
          ? colors.surface0
          : showHoverFill
            ? `${colors.traceBlue}${interactiveState.hoverAlphaSuffix}`
            : 'transparent',
        color: active ? colors.text : colors.overlay0,
        border: 'none',
        borderRadius: radii.lg,
        padding: '5px 12px',
        fontSize: 12,
        cursor: 'pointer',
        ...style,
      }}
      {...rest}
    >
      {children}
    </button>
  )
}

export function Nav({ children }) {
  return (
    <div style={{ display: 'flex', gap: spacing.xs }}>
      {children}
    </div>
  )
}
