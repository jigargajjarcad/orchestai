// frontend/src/components/Card.jsx
//
// R1 Task 5 (master plan §5: "likely a `Card`/`Surface` wrapper encoding the
// new elevation/surface hierarchy") — a NEW shared primitive, distinct from
// the existing `Panel`:
//   - `Panel` is the *base* card (fixed `surface.surface` background,
//     `colors.surface0` solid border, optional `accentStatus` status-tinted
//     left-accent) — it does not vary its surface tier or shadow, and is the
//     component every execution/approval card already migrated onto (Task 3).
//   - `Card` instead exposes the new 3-tier `surface`/`elevation` ramp
//     directly: pick a `level` and get the matching background + shadow +
//     hairline border as one unit, for the layered-surface compositions
//     (e.g. a raised card floating over the page, or an overlay-tier surface
//     inside a drawer) that `Panel`'s single fixed tier can't express. `Card`
//     has no `accentStatus` concept — that stays `Panel`'s job.
//
// Tier -> elevation mapping (tokens.js §"Surface hierarchy" / §"Elevation" —
// the two token exports are already a monotonic pair, this just wires them
// together per-tier so a caller can't pick a mismatched surface/shadow):
//   level='surface' (default) -> `surface.surface` (`colors.base`, #1e1e2e)
//                                  bg + `elevation.flat` ('none') shadow —
//                                  the resting tier, same visual weight as a
//                                  `Panel` at rest.
//   level='raised'            -> `surface.raised` (#232336) bg +
//                                  `elevation.raised` (soft two-layer shadow)
//                                  — hover/active/selected-adjacent surfaces
//                                  that need to visually lift off the page.
//   level='overlay'           -> `surface.overlay` (#2a2a40) bg +
//                                  `elevation.overlay` (the strongest, single
//                                  soft shadow) — drawers/menus/modal-adjacent
//                                  surfaces, the top of the ramp.
// All three tiers share `surface.border` (the translucent
// `rgba(205,214,244,0.08)` hairline generalized from the observability
// slice's validated `panelBorder`) rather than `Panel`'s solid `surface0` —
// a translucent hairline reads correctly against any of the three
// backgrounds above, where a fixed solid border color would not.
//
// Presentational only, by design: this component intentionally does NOT add
// a clickable/interactive variant. The task brief calls a plain
// presentational default "fine and preferred," and every one of this
// system's existing interactive controls (Button, NavItem, and now Toggle)
// already owns its own hover/active/focus state machine — bolting a second,
// less-exercised interactive contract onto a plain surface wrapper is scope
// this task does not need. A future interactive-card need should get its own
// reviewed a11y contract (role, keyboard activation, focus-visible) rather
// than an afterthought prop here.
//
// API: `Card({ children, level, padding, style })`
//   - `level` (optional, default `'surface'`): `'surface' | 'raised' |
//     'overlay'` — selects the tier per the mapping above. An unrecognized
//     value falls back to `'surface'` rather than rendering an `undefined`
//     background/shadow.
//   - `padding` (optional, default `spacing.lg`, 16px): CSS padding shorthand
//     for the card's inner spacing.
//   - `style` (optional): merges LAST, overriding every computed property
//     above (including `padding`) — matches every other primitive's override
//     pattern (Button, Input, Label, Panel, StatusBadge, StateText, Toggle).

import { radiusForElement, spacing, surface, elevation } from '../theme/tokens'

const LEVEL_TO_SURFACE = {
  surface: surface.surface,
  raised: surface.raised,
  overlay: surface.overlay,
}

const LEVEL_TO_ELEVATION = {
  surface: elevation.flat,
  raised: elevation.raised,
  overlay: elevation.overlay,
}

export function Card({ children, level = 'surface', padding, style }) {
  const resolvedLevel = Object.prototype.hasOwnProperty.call(LEVEL_TO_SURFACE, level) ? level : 'surface'

  return (
    <div
      style={{
        background: LEVEL_TO_SURFACE[resolvedLevel],
        border: `1px solid ${surface.border}`,
        borderRadius: radiusForElement.card,
        boxShadow: LEVEL_TO_ELEVATION[resolvedLevel],
        padding: padding ?? spacing.lg,
        ...style,
      }}
    >
      {children}
    </div>
  )
}
