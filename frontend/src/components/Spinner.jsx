// frontend/src/components/Spinner.jsx
//
// P2-M2 Task C1 — the ONE shared loading affordance for the app. Composition:
// a rotating ring (`.orch-spinner` in index.css) + a visible text label. Every
// existing/added loading branch across the app (Memories, Playground, Evals
// Results, Observability's non-Timeline views) renders this instead of ad-hoc
// "Loading…" text or nothing at all.
//
// Never animation-alone: rotation is a purely decorative reinforcement of a
// state that is ALWAYS also communicated via the visible text `label`. This
// matters twice over — (1) `prefers-reduced-motion: reduce` disables the
// keyframe entirely (see index.css), and with motion off the label is the
// only signal, so it must never be optional/empty by default; (2) the
// wrapper carries `role="status"` + `aria-live="polite"` so assistive tech
// announces the label text the moment it mounts, independent of whether the
// user can (or wants to) perceive the spin itself.
//
// API: `Spinner({ label = 'Loading…', size = 16, style })`
//   - `label`: user-facing loading copy, UI face (`uiFontFamily`) per the
//     same face-assignment rule EmptyState's heading/description follow —
//     this is UI chrome, not a rendered data value.
//   - `size`: ring diameter in px (width/height); border thickness is fixed
//     at 2px regardless of size (matches every other primitive's fixed
//     stroke conventions rather than scaling proportionally).
//   - `style`: merges LAST onto the wrapper, overriding every computed
//     property above — matches every other primitive's override pattern
//     (Button, Input, Label, Panel, StatusBadge, StateText, EmptyState).

import { colors, spacing, typography, uiFontFamily } from '../theme/tokens'

export function Spinner({ label = 'Loading…', size = 16, style }) {
  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: spacing.sm,
        ...style,
      }}
    >
      <span
        aria-hidden="true"
        className="orch-spinner"
        style={{
          width: size,
          height: size,
          border: `2px solid ${colors.surface2}`,
          borderTopColor: colors.traceBlue,
          borderRadius: '50%',
          flex: '0 0 auto',
        }}
      />
      {label && (
        <span style={{ color: colors.subtext0, fontSize: typography.body.fontSize, fontFamily: uiFontFamily }}>
          {label}
        </span>
      )}
    </div>
  )
}
