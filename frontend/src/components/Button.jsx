// frontend/src/components/Button.jsx
//
// Two variants only, per the plan's §8 decision 2 (final): no success/danger
// variants. ApprovalCard's Approve/Reject buttons stay as local one-offs
// (Task 3), not migrated onto this component.
//
// - primary: DESIGN.md §5 Buttons + .impeccable/design.json's .ds-btn-primary
//   (Trace Blue bg, Crust text, 700 weight, full-width, 9px vertical
//   padding, 6px radius; disabled drops to surface0 bg / overlay0 text).
// - ghost: transparent bg, 1px colored border at 50% alpha, text matching
//   the border's base color — used for Cancel/secondary actions. Colored
//   with Trace Blue per DESIGN.md's "One Accent Rule" ("Trace Blue is the
//   only color used for interactive affordances... buttons, links, focus");
//   DESIGN.md's own ghost example uses a red tone, but only as an
//   illustration of a *destructive* one-off (explicitly out of scope here
//   per the no-danger-variant decision above), not the generic secondary
//   color.
//
// Optional `style` merges over (wins over) the variant/disabled-computed
// style, matching Input/TextArea's existing override pattern — lets a
// caller override e.g. just the enabled-state text color without needing
// to know the disabled-state values.
//
// R1 Task 4 — interactive-state matrix (master plan §3.7): hover/active use
// self-contained React state (inline styles can't express `:hover`/
// `:active`) and are computed from `interactiveState`'s hex-alpha-suffix
// idiom, never a hardcoded color:
//   - primary: bg is already an opaque solid (Trace Blue), so tinting it
//     with *more* Trace Blue at low alpha is a no-op (same hue over itself).
//     Instead this composes the button's own content color (`colors.crust`)
//     as a translucent "state layer" *inset box-shadow* on top of the solid
//     bg — the standard darken-on-hover pattern for a solid-fill button,
//     expressed as an inset shadow (not a second DOM node) so it still
//     respects `borderRadius` for free.
//   - ghost: bg is transparent, so the border/text base color (Trace Blue)
//     is applied directly as a translucent background fill on hover/active
//     — same idiom NavItem uses for its non-selected hover fill.
// Hover/active are suppressed whenever `disabled` (guarded, not just
// visually inert) and never touch focus-visible, which stays on the
// existing `.focus-ring` class/CSS (index.css already matches the
// `focusRing` token's values — not re-tokenized here, see task report).
// Kept deliberately shadow-free otherwise (no `elevation.raised` on
// default/hover) — this product's understated, flat-by-default identity
// per DESIGN.md; a permanent drop-shadow on every primary button would
// over-decorate a single full-width action button.

import { useState } from 'react'
import { colors, radii, typography, buttonGhostBorderAlphaSuffix, interactiveState } from '../theme/tokens'

export function Button({ variant = 'primary', disabled, onClick, type = 'button', children, style }) {
  const [hovered, setHovered] = useState(false)
  const [pressed, setPressed] = useState(false)

  const stateAlphaSuffix = disabled
    ? null
    : pressed
      ? interactiveState.activeAlphaSuffix
      : hovered
        ? interactiveState.hoverAlphaSuffix
        : null

  const variantStyle =
    variant === 'ghost'
      ? {
          background: stateAlphaSuffix ? `${colors.traceBlue}${stateAlphaSuffix}` : 'transparent',
          color: colors.traceBlue,
          border: `1px solid ${colors.traceBlue}${buttonGhostBorderAlphaSuffix}`,
        }
      : {
          background: disabled ? interactiveState.disabledBackground : colors.traceBlue,
          color: disabled ? interactiveState.disabledText : colors.crust,
          border: 'none',
          boxShadow: stateAlphaSuffix ? `inset 0 0 0 999px ${colors.crust}${stateAlphaSuffix}` : 'none',
        }

  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className="focus-ring"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => {
        setHovered(false)
        setPressed(false)
      }}
      onMouseDown={() => setPressed(true)}
      onMouseUp={() => setPressed(false)}
      style={{
        width: '100%',
        padding: '9px 0',
        borderRadius: radii.lg,
        fontWeight: 700,
        fontSize: typography.body.fontSize,
        cursor: disabled ? 'not-allowed' : 'pointer',
        ...variantStyle,
        ...style,
      }}
    >
      {children}
    </button>
  )
}
