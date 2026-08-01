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
//
// Optional `style` merges over (wins over) the tone-driven base style,
// matching Input/TextArea's existing override pattern.
//
// R1 Task 4 — considered aliasing tone='error' onto `semantic.critical`
// (`colors.statusFailed`, #dc2626) since this is a semantic-critical text
// role. Rejected: this component's whole documented purpose for
// `colors.alertRed` is to be the *soft* identity-palette error tone,
// explicitly distinct from "the harder Status Failed tone" (see above) —
// swapping to `semantic.critical` would collapse that intentional
// distinction and change the rendered color. Left as `colors.alertRed`.
// Static text primitive — no interactive-state matrix applies.

import { colors, typography, uiFontFamily } from '../theme/tokens'

export function StateText({ children, tone = 'muted', style }) {
  return (
    <div
      style={{
        color: tone === 'error' ? colors.alertRed : colors.textMuted,
        fontSize: typography.body.fontSize,
        fontFamily: uiFontFamily,
        ...style,
      }}
    >
      {children}
    </div>
  )
}
