// frontend/src/components/Input.jsx
//
// Text-input + textarea field primitives — DESIGN.md §5 Inputs/Fields,
// matching .impeccable/design.json's .ds-input (surface0 1px border, 6px
// radius, 13px text) with one deliberate override: background is mantle
// (#181825), per DESIGN.md's literal spec and the app's actual current
// textarea usage (App.jsx's rejection-note textarea), not design.json's
// worked example (which shows base/#1e1e2e). `mantle` isn't one of the new
// `surface` ramp's rungs (canvas/surface/raised/overlay) — it's a distinct,
// deliberately-different neutral one step darker than `surface.surface`
// (`colors.base`) — so this task leaves it as the existing `colors.mantle`
// reference rather than forcing a `surface.*` token that would visibly
// lighten every field.
//
// R1 Task 4 — focus is now tokenized product-wide (`focusRing` in
// theme/tokens.js matches index.css's `.focus-ring:focus-visible` rule
// exactly), superseding DESIGN.md's older "no custom focus treatment" note
// referenced below (kept for history) — the `.focus-ring` class already
// applied to both fields is sufficient, so no new focus styling is added
// here.
//
// New: a `disabled` visual state (previously unstyled — the fields relied
// on the browser's default disabled rendering). Detected via `rest.disabled`
// (not destructured out of `...rest`, so it's still forwarded to the native
// element as an attribute exactly as before) and rendered with
// `interactiveState.disabledBackground`/`disabledText` — the same
// surface0/overlay0 convention Button/DESIGN.md already use for disabled
// controls, so disabled fields read consistently with disabled buttons.
//
// Both variants render an optional Label directly above the field, per
// DESIGN.md:227-228 ("always the Label typography style directly above the
// field, never inline placeholder-only").
//
// Optional `labelStyle` is threaded to the inner `Label`'s own `style`
// override — needed for call sites whose original label wasn't bold, unlike
// Label's default (matches Input/TextArea/Panel/Button/StateText's existing
// override pattern).

import { colors, radii, typography, interactiveState, dataFontFamily } from '../theme/tokens'
import { Label } from './Label'

const fieldStyle = {
  width: '100%',
  boxSizing: 'border-box',
  padding: '8px 10px',
  borderRadius: radii.lg,
  border: `1px solid ${colors.surface0}`,
  background: colors.mantle,
  color: colors.text,
  fontSize: typography.body.fontSize,
  fontFamily: dataFontFamily,
}

const disabledFieldStyle = {
  background: interactiveState.disabledBackground,
  color: interactiveState.disabledText,
  cursor: 'not-allowed',
}

export function Input({ label, labelStyle, value, onChange, style, ...rest }) {
  return (
    <div>
      {label && (
        <div style={{ marginBottom: 4 }}>
          <Label style={labelStyle}>{label}</Label>
        </div>
      )}
      <input
        value={value}
        onChange={onChange}
        className="focus-ring"
        style={{
          ...fieldStyle,
          ...(rest.disabled ? disabledFieldStyle : null),
          ...style,
        }}
        {...rest}
      />
    </div>
  )
}

export function TextArea({ label, labelStyle, value, onChange, style, ...rest }) {
  return (
    <div>
      {label && (
        <div style={{ marginBottom: 4 }}>
          <Label style={labelStyle}>{label}</Label>
        </div>
      )}
      <textarea
        value={value}
        onChange={onChange}
        className="focus-ring"
        style={{
          ...fieldStyle,
          resize: 'vertical',
          ...(rest.disabled ? disabledFieldStyle : null),
          ...style,
        }}
        {...rest}
      />
    </div>
  )
}
