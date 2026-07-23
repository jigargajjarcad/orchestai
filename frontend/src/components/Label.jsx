// frontend/src/components/Label.jsx
//
// Label typography primitive — section eyebrows, column headers, and the
// "always above the field" caption DESIGN.md's Inputs/Fields rule requires.
// DESIGN.md §3 Hierarchy: "Label (700, 11px, 0.07em letter-spacing,
// uppercase) ... Always paired with overlay0 or surface2 color, never
// full-brightness text." overlay0 is used here to match the codebase's
// existing label usages (e.g. .impeccable/design.json's ds-panel-label,
// App.jsx's "Agent Memories" eyebrow).

import { colors, typography } from '../theme/tokens'

export function Label({ children }) {
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
      }}
    >
      {children}
    </span>
  )
}
