// frontend/src/components/shell/PageContainer.jsx
//
// P2-M1A (Application Shell Prototype) Task 2 — content-width wrapper.
//
// A page's content region (the `children` slot AppShell's `<main>` renders)
// needs a soft max-width cap so lines of text/panels don't stretch edge-to-
// edge on a 2560px monitor, without hard-coding that cap per-page. This
// component is that one wrapper: it maps a semantic `width` name onto the
// `contentWidth` token (theme/tokens.js, P2-M1A Task 1) and centers the
// result. It does NOT touch page content itself (no product page is migrated
// onto this in this task — that is later Phase 2 work) and it does NOT know
// about AppShell; it is a plain, standalone wrapper usable anywhere a page
// needs a capped/centered content column, including nested inside AppShell's
// `<main>`.
//
// API: `PageContainer({ width = 'standard', children, style })`
//   - `width` (optional, default `'standard'`): `'readable' | 'standard' |
//     'wide' | 'full'` — selects the `contentWidth` token to use as
//     `maxWidth`. An unrecognized value falls back to `'standard'` rather
//     than rendering an `undefined` maxWidth (same guard idiom as Card's
//     `level` prop: `Object.prototype.hasOwnProperty.call(...)`).
//       - `readable` (68ch) / `standard` (1440px) / `wide` (1600px): soft
//         caps — centered via `margin: 0 auto` once the viewport exceeds the
//         cap, full viewport width (minus padding) below it.
//       - `full` (100%): no cap, no auto-centering — the container fills
//         whatever width its parent region provides (e.g. a canvas/graph
//         view that must use all available space).
//   - `children`: the page content.
//   - `style` (optional): merges LAST, overriding every computed property
//     above — matches every other primitive's override pattern (Button,
//     Input, Label, Panel, Card, StatusBadge, StateText, Toggle).
//
// Horizontal padding is breakpoint-aware (brief: "apply horizontal padding
// from spacing, e.g. spacing.lg desktop"): `spacing.lg` (16px) at tablet and
// above, `spacing.md` (12px) on mobile, where every horizontal pixel is
// scarcer. This reads `useBreakpoint()` (P2-M1A Task 1) directly rather than
// taking a breakpoint prop, so callers don't have to thread viewport state
// through just to get sensible padding.
//
// `maxWidthForContentWidth` is exported standalone (not just used
// internally) so the pure width-name -> `contentWidth` mapping — including
// the unknown-value fallback — can be unit-tested without mounting a
// component or touching `window`/hooks (see task-2-report.md's verification
// section for the assertions run against this export).

import { contentWidth, spacing } from '../../theme/tokens'
import { useBreakpoint } from '../../hooks/useBreakpoint'

const WIDTH_TO_MAX = {
  readable: contentWidth.readable,
  standard: contentWidth.standard,
  wide: contentWidth.wide,
  full: contentWidth.full,
}

/**
 * Pure width-name -> `contentWidth` token mapping used by PageContainer.
 * Exported standalone so it's unit-testable without rendering a component.
 *
 * Usage: maxWidthForContentWidth('wide') -> '1600px'
 *   - width: one of 'readable' | 'standard' | 'wide' | 'full', or any other
 *     value (falls back to 'standard' per the Card `level`-prop guard idiom)
 *   - returns: the resolved `contentWidth` token string
 */
// A pure helper exported standalone specifically so it's unit-testable without mounting
// PageContainer (see task-2-report.md).
// eslint-disable-next-line react/only-export-components -- intentional, see comment above
export function maxWidthForContentWidth(width) {
  const resolvedWidth = Object.prototype.hasOwnProperty.call(WIDTH_TO_MAX, width) ? width : 'standard'
  return WIDTH_TO_MAX[resolvedWidth]
}

export function PageContainer({ width = 'standard', children, style }) {
  const { isMobile } = useBreakpoint()
  const maxWidth = maxWidthForContentWidth(width)
  const isFull = maxWidth === contentWidth.full

  return (
    <div
      style={{
        width: '100%',
        maxWidth,
        // `full` fills its parent with no cap, so auto-centering margins
        // would be a no-op at best and a misleading override at worst if a
        // caller later inspects computed style — omit them entirely rather
        // than setting '0 auto' redundantly.
        ...(isFull ? null : { marginLeft: 'auto', marginRight: 'auto' }),
        boxSizing: 'border-box',
        paddingLeft: isMobile ? spacing.md : spacing.lg,
        paddingRight: isMobile ? spacing.md : spacing.lg,
        ...style,
      }}
    >
      {children}
    </div>
  )
}
