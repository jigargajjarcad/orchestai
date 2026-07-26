// frontend/src/components/shell/Sidebar.jsx
//
// P2-M1A (Application Shell Prototype) Task 3 — the real NAV CONTENT that
// fills AppShell's (Task 2) `sidebar` slot. No product page is touched here
// and Sidebar hardcodes no product routes — it is a generic list renderer;
// the demo (a later task) supplies actual product-like `items`.
//
// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------
//   <Sidebar items={[{ key, label, icon }, ...]} active={activeKey}
//            onNavigate={key => ...} brand={<Logo/>} />
//   - `items`: array of `{ key, label, icon }`. `icon` is a plain React node
//     (inline SVG, emoji-in-span, etc.) — Sidebar never interprets it.
//   - `active`: the active item's `key`. Compared per-item to derive each
//     NavItem's boolean `active` prop (which is what actually drives the
//     R1 active treatment + `aria-current="page"` — see NavItem.jsx).
//   - `onNavigate(key)`: called on item activation. Optional-safe
//     (`onNavigate?.(key)`) so an isolated render without a handler doesn't
//     throw on click.
//   - `brand`: optional node (logo/title) shown above the nav list. Hidden
//     entirely when the rail is collapsed (rather than a truncated/"compact"
//     variant) — a brand block is typically a logo *and* a subtitle line,
//     and picking one truncation strategy that reads well for an arbitrary
//     caller-supplied node is unknowable at this layer; hiding it entirely
//     avoids ever rendering clipped/overlapping text in a 56px rail. The
//     caller can pass a rail-friendly compact `brand` node of its own if it
//     wants something to show while collapsed — Sidebar doesn't force either
//     choice.
//
// Sidebar must be rendered as a descendant of `<AppShell>` — it reads
// `collapsed`/`toggleCollapse`/`isMobile` from `useAppShell()` (Task 2),
// which throws outside a provider. Usage:
//   <AppShell sidebar={<Sidebar items={...} active={...} onNavigate={...} />}
//             topBar={...}>
//     ...
//   </AppShell>
//
// ---------------------------------------------------------------------------
// Collapsed state is user-controlled, not re-derived here
// ---------------------------------------------------------------------------
// Per the controller decision this task honors: Sidebar renders strictly per
// the current `collapsed` value from `useAppShell()` context (itself
// persisted to `localStorage` by AppShell) — it never auto-forces collapse
// based on its own logic. The ONE exception is mobile: the brief is explicit
// that "the drawer shows the sidebar expanded... collapse control is a
// desktop/tablet affordance," so `effectiveCollapsed` ANDs the context value
// with `!isMobile` — on mobile the rail always renders expanded regardless
// of whatever was persisted, and the collapse toggle button itself is not
// rendered at all on mobile (nothing to toggle there).
//
// ---------------------------------------------------------------------------
// Items: `<nav aria-label="Primary">` + vertical `Nav` + extended `NavItem`
// ---------------------------------------------------------------------------
// `<nav aria-label="Primary">` is the real landmark AppShell deliberately
// does NOT provide itself (see AppShell.jsx's "Landmarks" section) — Sidebar
// is what supplies it. Items render through `Nav orientation="vertical"` +
// `NavItem` (both extended additively in this same task — see NavItem.jsx's
// header comment for the full icon/collapsed/tooltip contract). Expanded:
// icon + label. Collapsed: icon-only with NavItem's built-in accessible
// tooltip (aria-label + visible-on-hover-and-focus tooltip span). Active
// state reuses NavItem's existing R1 active treatment + `aria-current`
// as-is — Sidebar does not re-implement it.
//
// Touch targets: each NavItem gets an explicit `minHeight: 44` + full-width
// via `style` (NavItem's `style`-prop-last convention lets a caller override
// without needing a new NavItem prop) — meeting the brief's >=44px mobile
// touch floor independent of whatever the shared horizontal top-nav uses.
// Item-to-item spacing (>=8px) comes from `Nav`'s vertical-orientation
// `spacing.sm` gap, not a per-item margin.
//
// IMPORTANT — why this `<nav>` does NOT set its own `overflow`:
// NavItem's collapsed tooltip escapes the narrow rail via
// `position: absolute; left: 100%` (see NavItem.jsx). CSS's overflow-x/
// overflow-y are coupled: per the CSS Overflow spec, setting only one axis
// to a non-`visible` value (e.g. `overflowY: 'auto'` for a scrollable item
// list) forces the OTHER axis's used value to `auto` too — silently turning
// on horizontal clipping/scrolling exactly where the tooltip needs to escape
// horizontally, defeating the "must not be clipped" requirement. AppShell's
// own sidebar grid column also sets no `overflow` (verified against
// shell/AppShell.jsx) all the way up to its `overflowX: 'hidden'` root grid
// — but that root only clips content overflowing the ENTIRE grid's box
// (full viewport width), never a small tooltip rendered well within it. So:
// leaving `<nav>` (and everything else in this file) free of any `overflow`
// declaration is what keeps the tooltip's escape path unclipped end-to-end.
// The tradeoff — a very long item list isn't its own internally-scrollable
// region — is an accepted, documented limitation of this prototype; a real
// product build with dozens of nav items would need a different mechanism
// (e.g. scoping the scroll container to a wrapper that sits below/outside
// the collapsed NavItems, or switching the tooltip to viewport-relative
// `position: fixed` positioning computed via `getBoundingClientRect`) — out
// of scope for this generic T3 primitive.
//
// ---------------------------------------------------------------------------
// The collapse toggle button (the other a11y-sensitive piece)
// ---------------------------------------------------------------------------
// Desktop/tablet only (`!isMobile`) — calls `toggleCollapse()`.
//   - `aria-expanded={!collapsed}`: reflects whether the sidebar is
//     CURRENTLY expanded (the thing this button controls), not whether the
//     button itself is "active."
//   - `aria-label`: "Collapse sidebar" while expanded (clicking collapses
//     it), "Expand sidebar" while collapsed (clicking expands it) — the
//     label always names the action the NEXT click performs.
//   - Visible `.focus-ring` (shared keyboard-focus indicator, same class
//     every other interactive primitive in this codebase uses).
//   - Explicit `44x44` touch target, matching the same pattern AppShell's
//     own drawer close button already uses (shell/AppShell.jsx) — token
//     `radii.lg` + `colors.text`, no raw hex.
//   - A small inline chevron SVG (`currentColor`-stroked, so it inherits the
//     button's token-driven `color` rather than hardcoding its own) rotates
//     180° between the collapsed/expanded states as the only visual cue
//     beyond the tooltip-less icon+label pairing this button always shows
//     via its own `aria-label` (there's no separate tooltip needed here —
//     unlike collapsed NavItems, this button's label doesn't disappear, it
//     only lacks visible text, and `aria-label` alone is the standard,
//     sufficient pattern for an icon-only button whose accessible name never
//     needs a sighted-hover echo — it has no ambiguous multi-item context to
//     disambiguate the way a whole row of collapsed nav icons does).
//
// ---------------------------------------------------------------------------
// Tokens
// ---------------------------------------------------------------------------
// `spacing`/`radii`/`colors`/`surface` only — no raw hex anywhere in this
// file (the chevron SVG uses `currentColor`, not a literal color).

import { useAppShell } from './AppShell'
import { Nav, NavItem } from '../NavItem'
import { colors, radii, spacing, surface } from '../../theme/tokens'

// Decorative chevron for the collapse-toggle button. `currentColor` inherits
// the button's own token-driven `color` (set via `colors.text` below) — no
// hex literal lives in this icon. Not exported: an internal rendering detail
// of the toggle button only.
function CollapseChevron({ collapsed }) {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 16 16"
      fill="none"
      aria-hidden="true"
      style={{ transform: collapsed ? 'rotate(180deg)' : 'none' }}
    >
      <path
        d="M10 3L5 8l5 5"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

/**
 * The sidebar's nav content — fills AppShell's `sidebar` slot. Must render
 * inside `<AppShell>` (reads `useAppShell()`). See file header for the full
 * API/behavior contract.
 */
export function Sidebar({ items, active, onNavigate, brand }) {
  const { collapsed, toggleCollapse, isMobile } = useAppShell()

  // Mobile always shows the expanded rail (labels visible) regardless of the
  // persisted `collapsed` preference — the collapse affordance itself is
  // desktop/tablet-only. See file header.
  const effectiveCollapsed = collapsed && !isMobile

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', minWidth: 0 }}>
      {brand && !effectiveCollapsed && (
        <div
          style={{
            padding: spacing.md,
            borderBottom: `1px solid ${surface.border}`,
            flexShrink: 0,
          }}
        >
          {brand}
        </div>
      )}

      <nav aria-label="Primary" style={{ flex: 1, minWidth: 0, padding: spacing.sm }}>
        <Nav orientation="vertical">
          {(items ?? []).map(item => (
            <NavItem
              key={item.key}
              icon={item.icon}
              collapsed={effectiveCollapsed}
              active={item.key === active}
              onClick={() => onNavigate?.(item.key)}
              style={{ minHeight: 44, width: '100%', textAlign: 'left' }}
            >
              {item.label}
            </NavItem>
          ))}
        </Nav>
      </nav>

      {!isMobile && (
        <div
          style={{
            display: 'flex',
            justifyContent: collapsed ? 'center' : 'flex-end',
            padding: spacing.sm,
            borderTop: `1px solid ${surface.border}`,
            flexShrink: 0,
          }}
        >
          <button
            type="button"
            onClick={toggleCollapse}
            aria-expanded={!collapsed}
            aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            className="focus-ring"
            style={{
              // Explicit 44x44 touch target, independent of the chevron
              // glyph's own visual size — matches AppShell's own drawer
              // close-button pattern.
              width: 44,
              height: 44,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              background: 'transparent',
              border: 'none',
              borderRadius: radii.lg,
              color: colors.text,
              cursor: 'pointer',
            }}
          >
            <CollapseChevron collapsed={collapsed} />
          </button>
        </div>
      )}
    </div>
  )
}
