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
//
// NOTE re: `display: 'block'` — tried and reverted (Task 3 follow-up).
// Input/TextArea/MemoriesPage all wrap this component in their own
// `<div style={{marginBottom:4}}>`. Forcing the inner span block-level did
// close the ~2px glyph-position gap versus the original bare
// `<label style={{display:'block',...}}>` it replaced, but at a much larger
// cost: it removed an inline-formatting-context "strut" effect that was
// keeping the wrapping div's total height close to the original single
// block element's — with it gone, each Label instance's contributed row
// height collapsed by ~13px, cascading into a ~26px upward shift of
// everything below two stacked Label usages (measured directly: the
// Playground's submit button moved from y=356 to y=330). That's a bigger,
// more visible regression than the 2px it fixed, so it was reverted. Fixing
// this properly likely needs a change to the wrapping div (Input.jsx/
// MemoriesPage), not just this component in isolation — left as a known,
// precisely-measured, open gap rather than accepting a worse regression.

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
