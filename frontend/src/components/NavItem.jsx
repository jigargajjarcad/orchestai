// frontend/src/components/NavItem.jsx
//
// Flat text nav button — DESIGN.md §5 Navigation: "flat text buttons in a
// horizontal row, no underline, active state gets a surface0 background
// fill and brightens text from overlay0 to text. No hover-lift, no shadow —
// background-fill is the only interactive cue." Matches the current
// Playground/Memories/Observability/Evals nav buttons in App.jsx exactly
// (background/color/border/radius/padding/fontSize below are transcribed
// from that literal, currently-duplicated implementation).
//
// R1 Task 4 — interactive-state matrix (master plan §3.7):
//   - active ("selected"): background stays exactly `colors.surface0` (not
//     re-derived via `interactiveState.selectedAlphaSuffix` composed onto a
//     base) — the header comment above pins this to a specific, already-
//     shipped solid color, and compositing a translucent tint onto some
//     other base would shift the rendered hex away from what's live today.
//     Preserving that exact, already-correct rendering wins over routing it
//     through the alpha-suffix idiom for its own sake.
//   - hover (non-active only): a subtle background fill using Trace Blue
//     (the design system's One Accent Rule color for all interactive
//     affordances) at `interactiveState.hoverAlphaSuffix`. Text color is
//     unaffected by hover — per the header comment, background-fill is the
//     *only* cue (no hover-lift/shadow), so hover extends that same rule
//     rather than introducing a new one.
//   - disabled: not part of NavItem's public surface (no `disabled` prop) —
//     out of scope.
// Hover uses self-contained React state (inline styles can't express
// `:hover`) via mouse handlers; keyboard focus is untouched, still the
// existing `.focus-ring` class.
//
// ---------------------------------------------------------------------------
// P2-M1A Task 3 — additive icon / collapsed / vertical extension
// ---------------------------------------------------------------------------
// Everything above shipped as a horizontal, text-only primitive (App.jsx's
// top nav — untouched by this task, still renders through the exact same
// code paths below). This task extends NavItem/Nav additively so the new
// `shell/Sidebar.jsx` can reuse the same primitive vertically, with icons,
// and in a collapsed icon-only rail, without changing a single byte of the
// existing horizontal/no-icon/no-collapsed render output. Every new prop is
// optional; every new style key is `undefined` (React/ReactDOMServer omit
// `undefined` style values from the rendered `style` attribute entirely)
// unless the new prop is actually used — see task-3-report.md for a direct
// `renderToStaticMarkup` string comparison proving the legacy path is
// byte-identical to the pre-edit component.
//
//   - `icon` (optional): a leading node (inline SVG / emoji span / etc.),
//     rendered inside its own `aria-hidden="true"` wrapper — decorative; the
//     accessible name always comes from the button's text content or (in
//     collapsed mode) `aria-label`, never from the icon itself.
//   - `collapsed` (optional): icon-only rendering. The label is NOT dropped
//     for assistive tech — it's promoted to `aria-label` (an explicit
//     `label` prop wins, else the plain-string `children`) — AND surfaced
//     visually as a tooltip, so a sighted keyboard-only user (who tabs, not
//     hovers) gets the same affordance a mouse user gets on hover. Tooltip
//     specifics:
//       - Always present in the DOM once `collapsed` is true (not
//         conditionally mounted only while hovered/focused) — only its
//         opacity/visibility toggle in response to hover/focus. This is what
//         lets an isolated `renderToStaticMarkup` snapshot assert the
//         tooltip markup exists without simulating real pointer/focus events
//         (see task-3-report.md verification (c)).
//       - Shows on BOTH `hovered` (mouse, existing `onMouseEnter`/
//         `onMouseLeave` pattern above) and `focused` (keyboard — the same
//         pattern, added via `onFocus`/`onBlur`) — deliberately not a
//         `title` attribute, which never fires on keyboard focus and is an
//         unstyleable, inconsistent browser-native tooltip even on hover.
//       - `aria-hidden="true"`: its text is the same `derivedLabel` already
//         exposed via `aria-label` — a visual-only aid, not a second,
//         competing accessible name a screen reader would announce twice.
//       - Positioned `absolute` (the button gets `position: relative` only
//         when collapsed), escaping to the right of the rail via
//         `left: 100%` + a small gap, rather than confined to the collapsed
//         rail's own ~56px column. This only works because nothing between
//         the button and the viewport sets `overflow` other than `visible`
//         — see `shell/Sidebar.jsx`'s header comment for why its own `<nav>`
//         deliberately does not add scroll overflow, and confirm against
//         `shell/AppShell.jsx`'s sidebar grid column (no `overflow` set
//         there either).
//       - Self-contained: plain React state + inline styles, no dependency.
//   - `Nav`'s `orientation="vertical"` (default unchanged: `'horizontal'`):
//     switches the wrapping div to `flexDirection: 'column'`, a larger item
//     gap (`spacing.sm`, meeting the brief's >=8px mobile item-spacing
//     floor), and `alignItems: 'stretch'` so items fill the rail's width.
//     The horizontal branch keeps `flexDirection`/`alignItems` as
//     `undefined` (omitted from the rendered style, exactly as before) and
//     the same `spacing.xs` gap — byte-identical to the pre-edit `Nav`.

import { useState } from 'react'
import { colors, radii, spacing, interactiveState, surface, uiFontFamily } from '../theme/tokens'

export function NavItem({ active, onClick, children, style, icon, collapsed, label, ...rest }) {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const showHoverFill = hovered && !active
  const showTooltip = collapsed && (hovered || focused)

  // Accessible-name source for collapsed (icon-only) mode: an explicit
  // `label` prop wins; otherwise fall back to `children` when it's a plain
  // string (the common case for every current/anticipated nav item). If
  // `children` isn't a plain string and no `label` is given, `derivedLabel`
  // stays `undefined` — a collapsed usage with composite children should
  // pass `label` explicitly.
  const derivedLabel = label ?? (typeof children === 'string' ? children : undefined)

  // Built as a spread-in object, not a literal `aria-label={...}` JSX
  // attribute, and deliberately `{}` (no key at all) rather than
  // `{ 'aria-label': undefined }` when not collapsed: a literal attribute
  // would insert an `aria-label` key into the underlying props object at
  // THIS position even when its value is `undefined`, which then wins the
  // *position* (first-occurrence-wins ordering, per JS object-literal
  // semantics) over any `aria-label` a caller passes through `...rest` —
  // shifting that attribute earlier in the rendered HTML and breaking the
  // legacy byte-identical-output guarantee for callers like App.jsx's
  // `aria-label="Memories"` usage. An empty object contributes zero keys,
  // so the legacy (non-collapsed) path is unaffected either way.
  const collapsedAriaLabelProps = collapsed ? { 'aria-label': derivedLabel } : {}

  return (
    <button
      onClick={onClick}
      aria-current={active ? 'page' : undefined}
      {...collapsedAriaLabelProps}
      className="focus-ring"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
      style={{
        background: active
          ? colors.surface0
          : showHoverFill
            ? `${colors.traceBlue}${interactiveState.hoverAlphaSuffix}`
            : 'transparent',
        color: active ? colors.text : colors.subtext0,
        border: 'none',
        borderRadius: radii.lg,
        padding: '5px 12px',
        fontSize: 12,
        fontFamily: uiFontFamily,
        cursor: 'pointer',
        // Icon/collapsed layout — additive. Every key here is `undefined`
        // (omitted from the rendered style) unless `icon`/`collapsed` is
        // actually used, so the plain text-only/expanded case keeps none of
        // these keys, exactly as before this task.
        display: icon || collapsed ? 'inline-flex' : undefined,
        alignItems: icon || collapsed ? 'center' : undefined,
        justifyContent: collapsed ? 'center' : undefined,
        gap: icon ? spacing.xs : undefined,
        position: collapsed ? 'relative' : undefined,
        ...style,
      }}
      {...rest}
    >
      {icon && (
        <span aria-hidden="true" style={{ display: 'inline-flex', flexShrink: 0 }}>
          {icon}
        </span>
      )}
      {!collapsed && children}
      {collapsed && (
        <span
          aria-hidden="true"
          style={{
            position: 'absolute',
            left: '100%',
            top: '50%',
            transform: 'translateY(-50%)',
            marginLeft: spacing.sm,
            padding: `${spacing.xs} ${spacing.sm}`,
            borderRadius: radii.md,
            background: surface.overlay,
            border: `1px solid ${surface.border}`,
            color: colors.text,
            fontSize: 12,
            whiteSpace: 'nowrap',
            pointerEvents: 'none',
            zIndex: 40,
            opacity: showTooltip ? 1 : 0,
            visibility: showTooltip ? 'visible' : 'hidden',
            transition: 'opacity 0.1s ease',
          }}
        >
          {derivedLabel}
        </span>
      )}
    </button>
  )
}

export function Nav({ children, orientation = 'horizontal' }) {
  const vertical = orientation === 'vertical'
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: vertical ? 'column' : undefined,
        alignItems: vertical ? 'stretch' : undefined,
        gap: vertical ? spacing.sm : spacing.xs,
      }}
    >
      {children}
    </div>
  )
}
