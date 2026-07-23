// frontend/src/components/NavItem.jsx
//
// Flat text nav button — DESIGN.md §5 Navigation: "flat text buttons in a
// horizontal row, no underline, active state gets a surface0 background
// fill and brightens text from overlay0 to text. No hover-lift, no shadow —
// background-fill is the only interactive cue." Matches the current
// Playground/Memories/Observability/Evals nav buttons in App.jsx exactly
// (background/color/border/radius/padding/fontSize below are transcribed
// from that literal, currently-duplicated implementation).

import { colors, radii, spacing } from '../theme/tokens'

export function NavItem({ active, onClick, children, style }) {
  return (
    <button
      onClick={onClick}
      style={{
        background: active ? colors.surface0 : 'transparent',
        color: active ? colors.text : colors.overlay0,
        border: 'none',
        borderRadius: radii.lg,
        padding: '5px 12px',
        fontSize: 12,
        cursor: 'pointer',
        ...style,
      }}
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
