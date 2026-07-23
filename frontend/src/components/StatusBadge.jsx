// frontend/src/components/StatusBadge.jsx
//
// Tinted-pill status badge — DESIGN.md §5 Status Badges / §6 Do's ("20%
// alpha bg, 50-60% alpha border, full-opacity text"), matching
// .impeccable/design.json's .ds-status-badge shape (4px radius, 2px 10px
// padding, uppercase 11px/700 label).
//
// Covers all six execution-status values in use across the app (App.jsx's
// STATUS_COLORS + ObservabilityPage.jsx's STATUS_COLORS): Pending, Running,
// WaitingForApproval, Completed, Failed, Skipped.

import { typography, radii, statusBadgeColors } from '../theme/tokens'

const STATUS_TO_PALETTE_KEY = {
  Pending: 'pending',
  Running: 'running',
  WaitingForApproval: 'warning',
  Completed: 'completed',
  Failed: 'failed',
  Skipped: 'skipped',
}

export function StatusBadge({ status, label }) {
  const paletteKey = STATUS_TO_PALETTE_KEY[status] ?? 'pending'
  const palette = statusBadgeColors[paletteKey]
  const text = (label ?? status ?? '').toString().toUpperCase()

  return (
    <span
      style={{
        display: 'inline-block',
        background: palette.background,
        border: `1px solid ${palette.border}`,
        color: palette.text,
        borderRadius: radii.md,
        padding: '2px 10px',
        fontFamily: typography.label.fontFamily,
        fontSize: typography.label.fontSize,
        fontWeight: typography.label.fontWeight,
        // 0.06em, not typography.label's 0.07em: DESIGN.md/design.json pin
        // the badge specifically at "0.05-0.06em" (.ds-status-badge uses
        // 0.06em exactly), distinct from the general Label style.
        letterSpacing: '0.06em',
      }}
    >
      {text}
    </span>
  )
}
