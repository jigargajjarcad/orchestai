// frontend/src/components/Input.jsx
//
// Text-input + textarea field primitives — DESIGN.md §5 Inputs/Fields,
// matching .impeccable/design.json's .ds-input (surface0 1px border, 6px
// radius, 13px text) with one deliberate override: background is mantle
// (#181825), per DESIGN.md's literal spec and the app's actual current
// textarea usage (App.jsx's rejection-note textarea), not design.json's
// worked example (which shows base/#1e1e2e).
//
// No custom focus ring: DESIGN.md's Inputs/Fields section states "this
// system has not yet defined a custom focus treatment" — design.json's
// .ds-input:focus rule (border-color: Trace Blue) is not carried over here,
// per the task brief's explicit instruction not to invent one.
//
// Both variants render an optional Label directly above the field, per
// DESIGN.md:227-228 ("always the Label typography style directly above the
// field, never inline placeholder-only").
//
// Optional `labelStyle` is threaded to the inner `Label`'s own `style`
// override — needed for call sites whose original label wasn't bold, unlike
// Label's default (matches Input/TextArea/Panel/Button/StateText's existing
// override pattern).

import { colors, radii, typography } from '../theme/tokens'
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
        style={{ ...fieldStyle, ...style }}
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
        style={{ ...fieldStyle, resize: 'vertical', ...style }}
        {...rest}
      />
    </div>
  )
}
