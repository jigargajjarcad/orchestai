// frontend/src/components/Label.jsx
//
// Label typography primitive — section eyebrows, column headers, and the
// "always above the field" caption DESIGN.md's Inputs/Fields rule requires.
// DESIGN.md §3 Hierarchy: "Label (700, 11px, 0.07em letter-spacing,
// uppercase) ... Always paired with overlay0 or surface2 color, never
// full-brightness text." overlay0 is used here as this component's default,
// matching .impeccable/design.json's ds-panel-label — but NOT App.jsx's
// "Agent Memories" eyebrow, which actually used surface2 (#585b70) and a
// non-bold weight; that call site overrides both via the `style` prop
// below rather than matching this default (see Task 3's App.jsx usage).
//
// Optional `style` merges over (wins over) the base style, matching
// Input/TextArea's existing override pattern.

import { colors, typography } from '../theme/tokens'

export function Label({ children, style }) {
  return (
    <span
      style={{
        fontFamily: typography.label.fontFamily,
        fontSize: typography.label.fontSize,
        fontWeight: typography.label.fontWeight,
        lineHeight: typography.label.lineHeight,
        letterSpacing: typography.label.letterSpacing,
        color: colors.overlay0,
        textTransform: 'uppercase',
        ...style,
      }}
    >
      {children}
    </span>
  )
}
