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
//
// Optional `style` merges over (wins over) the base style object, matching
// Input/TextArea's existing override pattern — needed for Task 3 call sites
// that require pixel parity with today's one-off inline styles. Uppercasing
// is CSS (`textTransform`), not a JS `.toUpperCase()` call, specifically so
// `style={{ textTransform: 'none' }}` can opt a call site out of it.
//
// Optional `borderAlphaSuffix` overrides just the border's hex alpha suffix
// (default `statusBadgeAlphaSuffix.border`, 55%) — needed because some
// pre-existing call sites used a different border alpha than the canonical
// one before migrating onto this component, and background alpha (the one
// approved normalization) must stay untouched while border alpha is
// restored to its original per-site value.

import { typography, radii, statusBadgeColors, statusBadgeAlphaSuffix } from '../theme/tokens'

const STATUS_TO_PALETTE_KEY = {
  Pending: 'pending',
  Running: 'running',
  WaitingForApproval: 'warning',
  Completed: 'completed',
  Failed: 'failed',
  Skipped: 'skipped',
}

export function StatusBadge({ status, label, style, borderAlphaSuffix }) {
  const paletteKey = STATUS_TO_PALETTE_KEY[status] ?? 'pending'
  const palette = statusBadgeColors[paletteKey]
  const text = (label ?? status ?? '').toString()

  return (
    <span
      style={{
        display: 'inline-block',
        background: palette.background,
        border: `1px solid ${palette.text}${borderAlphaSuffix ?? statusBadgeAlphaSuffix.border}`,
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
        // CSS-driven (not .toUpperCase() in JS) so a `style` override can
        // opt a call site out of uppercasing via `textTransform: 'none'`
        // without needing to also change the text it passes in.
        textTransform: 'uppercase',
        ...style,
      }}
    >
      {text}
    </span>
  )
}
