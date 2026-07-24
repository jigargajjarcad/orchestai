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

import { colors, radii, typography, buttonGhostBorderAlphaSuffix } from '../theme/tokens'

export function Button({ variant = 'primary', disabled, onClick, type = 'button', children, style }) {
  const variantStyle =
    variant === 'ghost'
      ? {
          background: 'transparent',
          color: colors.traceBlue,
          border: `1px solid ${colors.traceBlue}${buttonGhostBorderAlphaSuffix}`,
        }
      : {
          background: disabled ? colors.surface0 : colors.traceBlue,
          color: disabled ? colors.overlay0 : colors.crust,
          border: 'none',
        }

  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className="focus-ring"
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
