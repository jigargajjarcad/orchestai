// frontend/src/components/StateText.jsx
//
// Minimal inline text wrapper for loading/empty/error copy (e.g.
// MemoriesPage's "Loading…" / "No memories saved yet…" / fetch-error
// messages in App.jsx). Carries only styling — the wording itself is
// unchanged wherever this is consumed in Tasks 3-5.
//
// tone='muted': surface2, matching the app's existing Loading/empty-state
// text color (App.jsx's MemoriesPage uses color: '#585b70' for both).
// tone='error': alert-red, DESIGN.md's "soft error tone" for inline error
// banners/validation text (distinct from the harder Status Failed tone).

import { colors, typography } from '../theme/tokens'

export function StateText({ children, tone = 'muted' }) {
  return (
    <div
      style={{
        color: tone === 'error' ? colors.alertRed : colors.surface2,
        fontSize: typography.body.fontSize,
      }}
    >
      {children}
    </div>
  )
}
