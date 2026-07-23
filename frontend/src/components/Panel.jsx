// frontend/src/components/Panel.jsx
//
// Base card surface — DESIGN.md §5 Cards/Panels, matching
// .impeccable/design.json's .ds-panel (base background, surface0 border,
// 8px radius, 16px 18px padding).
//
// Optional `accentStatus` switches on the "Status-Accented Card" pattern
// (.ds-status-card): a 3px status-tinted border-left plus an outer border
// re-tinted to the same status color at `panelAccentBorderAlphaSuffix`
// (~25.1% alpha) — this deliberately preserves the exact rendering already
// shipped today by AgentCard/ApprovalCard/ManagerReviewCard's `${statusColor}
// 40` border, not DESIGN.md's stated-but-uncorrected 40% intent; see
// tokens.js for the full resolution. Status base colors mirror
// App.jsx/ObservabilityPage.jsx's STATUS_COLORS maps.
//
// Optional `style` merges over (wins over) every computed property above,
// including padding — matching Input/TextArea's existing override pattern.
// Needed for Task 3 call sites that require pixel parity with today's
// one-off inline styles that this component is replacing.

import { colors, radii, panelAccentBorderAlphaSuffix } from '../theme/tokens'

const ACCENT_COLOR = {
  Pending: colors.statusPending,
  Running: colors.statusRunning,
  WaitingForApproval: colors.statusWarning,
  Completed: colors.statusCompleted,
  Failed: colors.statusFailed,
  Skipped: colors.overlay0,
}

export function Panel({ children, accentStatus, style }) {
  const accent = accentStatus ? ACCENT_COLOR[accentStatus] : null

  return (
    <div
      style={{
        background: colors.base,
        border: `1px solid ${accent ? `${accent}${panelAccentBorderAlphaSuffix}` : colors.surface0}`,
        borderRadius: radii.xl,
        padding: '16px 18px',
        ...(accent ? { borderLeft: `3px solid ${accent}` } : null),
        ...style,
      }}
    >
      {children}
    </div>
  )
}
