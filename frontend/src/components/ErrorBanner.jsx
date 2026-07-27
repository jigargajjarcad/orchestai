// frontend/src/components/ErrorBanner.jsx
//
// P2-M2 Task C2 — the ONE shared error presentation. A soft errorBg box with a
// full statusFailed-tinted border wrapping StateText(tone="error") — extracted
// verbatim from the identical inline banner previously duplicated in App.jsx
// (Memories + Playground) and applied consistently to every fetch/operation
// error across Playground, Memories, Evals, and Observability's non-Timeline
// views. Keeps error visually distinct from loading (Spinner) and empty
// (EmptyState). `role="alert"` so assistive tech announces the failure the
// moment it mounts. `style` merges onto the OUTER box for per-site layout
// (e.g. marginBottom/marginTop), matching each call site's original spacing.
import { colors, radii } from '../theme/tokens'
import { StateText } from './StateText'

export function ErrorBanner({ children, style }) {
  return (
    <div
      role="alert"
      style={{
        background: colors.errorBg,
        border: `1px solid ${colors.statusFailed}80`,
        borderRadius: radii.lg,
        padding: '10px 12px',
        ...style,
      }}
    >
      <StateText tone="error" style={{ fontSize: 12 }}>{children}</StateText>
    </div>
  )
}
