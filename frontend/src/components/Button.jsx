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
import {
  colors,
  radii,
  spacing,
  typography,
  buttonGhostBorderAlphaSuffix,
  interactiveState,
  uiFontFamily,
} from '../theme/tokens'

// P2-M1B Task 1 — additive `fullWidth` / `size` / `icon` props (deferred
// from P2-M1A, needed by the shell migration's inline "Change key" control).
// Not a redesign: existing callers (App.jsx's full-width "Run Agents", etc.)
// pass none of these three props and must render byte-identically to the
// pre-existing component (see Button.jsx's git history / task-1-report.md
// for the renderToStaticMarkup proof).
//
// Per-size padding + fontSize. `md` reproduces today's only values exactly
// (fullWidth padding '9px 0', fontSize typography.body.fontSize) so the
// zero-new-props default path is unaffected. `fullWidth`'s vertical padding
// for each size equals the vertical component of that size's inline
// padding (sm 6 / md 9 / lg 11) — full-width mode only needs vertical
// padding (the 100%-width already supplies horizontal extent); inline mode
// needs real horizontal padding since the button now hugs its content.
const SIZE_STYLES = {
  sm: { fullWidthPadding: '6px 0', inlinePadding: '6px 12px', fontSize: '12px' },
  md: { fullWidthPadding: '9px 0', inlinePadding: '9px 14px', fontSize: typography.body.fontSize },
  lg: { fullWidthPadding: '11px 0', inlinePadding: '11px 18px', fontSize: '15px' },
}

// F1.1 — `ariaDisabled` is additive, and exists for exactly one situation: a
// control that becomes unavailable WHILE IT HOLDS FOCUS.
//
// The `disabled` attribute makes an element unfocusable, so the browser drops
// focus to <body> the moment it is applied. Measured on the Playground: press
// Enter on "Run Agents" and focus goes BUTTON -> BODY and never returns, at the
// exact instant the user commits to a run. To reach the reconnect control, the
// approval card, or the results afterwards, a keyboard user has to Tab from the
// top of the document again (WCAG 2.4.3 Focus Order).
//
// `ariaDisabled` renders the identical disabled treatment and exposes the same
// state to assistive tech, but keeps the control in the tab order so focus
// survives. This is the WAI-ARIA Authoring Practices recommendation for
// transiently-unavailable controls. Enforcement stays with the caller's
// handler, which is where a real guard belongs — a control that merely LOOKS
// inert must not be trusted to be inert.
//
// Use `disabled` for "unavailable and not focused" (Evals' submit buttons,
// which are already inert before the user reaches them); use `ariaDisabled`
// for "became unavailable under the user's fingers".
export function Button({
  variant = 'primary',
  disabled,
  ariaDisabled,
  onClick,
  type = 'button',
  children,
  style,
  fullWidth = true,
  size = 'md',
  icon,
}) {
  const [hovered, setHovered] = useState(false)
  const [pressed, setPressed] = useState(false)
  const sizeStyle = SIZE_STYLES[size] ?? SIZE_STYLES.md
  // Drives every visual: the two states are indistinguishable on screen, and
  // deliberately so — only their focusability differs.
  const inactive = Boolean(disabled || ariaDisabled)

  const stateAlphaSuffix = inactive
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
          background: inactive ? interactiveState.disabledBackground : colors.traceBlue,
          color: inactive ? interactiveState.disabledText : colors.crust,
          border: 'none',
          boxShadow: stateAlphaSuffix ? `inset 0 0 0 999px ${colors.crust}${stateAlphaSuffix}` : 'none',
        }

  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      aria-disabled={ariaDisabled ? true : undefined}
      className="focus-ring"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => {
        setHovered(false)
        setPressed(false)
      }}
      onMouseDown={() => setPressed(true)}
      onMouseUp={() => setPressed(false)}
      style={{
        width: fullWidth ? '100%' : 'auto',
        padding: fullWidth ? sizeStyle.fullWidthPadding : sizeStyle.inlinePadding,
        borderRadius: radii.lg,
        fontWeight: 700,
        fontSize: sizeStyle.fontSize,
        fontFamily: uiFontFamily,
        cursor: inactive ? 'not-allowed' : 'pointer',
        display: icon ? 'inline-flex' : undefined,
        alignItems: icon ? 'center' : undefined,
        gap: icon ? spacing.xs : undefined,
        ...variantStyle,
        ...style,
      }}
    >
      {icon && (
        <span aria-hidden="true" style={{ display: 'inline-flex', alignItems: 'center' }}>
          {icon}
        </span>
      )}
      {children}
    </button>
  )
}
