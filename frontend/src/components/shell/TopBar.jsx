// frontend/src/components/shell/TopBar.jsx
//
// P2-M1A (Application Shell Prototype) Task 4 — the sticky top context-bar
// CONTENT that fills AppShell's (Task 2) `topBar` slot, including the
// mobile menu button that opens the nav drawer. No product page is touched
// here; the demo (a later task) supplies real title/context values.
//
// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------
//   <TopBar title="Playground">
//     ...surface-scoped context/controls (task id, status, cost, etc.)...
//   </TopBar>
// - `title`: rendered as the current-surface heading (see "Heading level"
//   below). Omitted entirely (not rendered as an empty element) when falsy —
//   an empty `<h1>` is worse for a11y than no heading at all.
// - `children`: rendered on the right as a generic "context slot" — TopBar
//   never interprets what's inside it (the demo passes placeholder
//   task-id/status/cost nodes; a real product surface would pass its own).
//
// Must be rendered as a descendant of `<AppShell>` (via its `topBar` slot,
// or any descendant position) — it reads `isMobile`/`openDrawer` from
// `useAppShell()` (Task 2), which throws outside a provider. Usage:
//   <AppShell topBar={<TopBar title="Playground">{contextNodes}</TopBar>}
//             sidebar={...}>
//     ...
//   </AppShell>
//
// ---------------------------------------------------------------------------
// Landmark: `<header>` here, NOT doubled with AppShell
// ---------------------------------------------------------------------------
// Checked against `AppShell.jsx` before writing this file: the `topBar` slot
// is rendered inside a plain sticky `<div>` (see AppShell's
// `position: 'sticky', top: 0` wrapper around `{topBar}`) — AppShell does
// NOT wrap the slot in its own `<header>`. That plain-div choice mirrors
// AppShell's own "Landmarks" section, which explains it supplies `<main
// role="main">` itself but deliberately leaves the sidebar's `<nav>` (T3)
// and, by the same logic, the top bar's `<header>` (this task) to the real
// content component — so each slot's landmark is owned by exactly one file,
// not implicitly assumed by the shell. TopBar therefore renders the real
// `<header>` element itself, as its outermost node — one landmark, no
// double-`<header>` nesting.
//
// ---------------------------------------------------------------------------
// Heading level: `<h1>`, using the `typography.heading` role token
// ---------------------------------------------------------------------------
// `theme/tokens.js` documents `heading` explicitly as "the once-per-view
// page title" (R1 Task 3's per-key face-assignment comment) — that is
// exactly what this slot's `title` is: the current view's name, shown once,
// in the persistent chrome. `title`, tokens.js's *other* heading-shaped role,
// is documented there as a *panel's section header* (smaller, 14px) — the
// wrong semantic fit for a whole-view heading. So: one level, `<h1>`, used
// consistently for every TopBar instance; `typography.heading` (20px/700/UI
// face) supplies the type, no per-element font-family invented.
//
// ---------------------------------------------------------------------------
// Mobile menu button -> `openDrawer()` -> focus return
// ---------------------------------------------------------------------------
// Rendered only when `isMobile` (desktop/tablet/wide never show it — the
// sidebar is a real grid column there, per AppShell). `onClick={openDrawer}`
// is the entire wiring: verified against `AppShell.jsx`'s `openDrawer`
// (`drawerTriggerRef.current = document.activeElement; setDrawerOpen(true)`)
// — at the moment a user clicks/activates this button, the button itself
// IS `document.activeElement` (browsers focus a button on click/Enter/Space
// activation), so `openDrawer()` captures exactly this button as the
// focus-return target with zero extra code here. AppShell's focus-trap
// effect cleanup (fires when `drawerOpen` flips back to `false`, i.e. on
// close, Escape, or scrim-click) then calls `.focus()` on that saved
// element — returning focus to this button automatically. TopBar does not
// pass a ref or otherwise participate beyond the `onClick` call; the whole
// contract lives in AppShell (Task 2), already built and unit-verified
// there. Live keyboard/focus verification in a real browser is the
// controller's job at T5/T7 (per the brief), not attempted here.
//
// Button requirements: `aria-label="Open navigation menu"`, `.focus-ring`
// (shared keyboard-focus indicator), explicit 44x44 touch target (matches
// the identical pattern already used by AppShell's own drawer close button
// and Sidebar's collapse toggle — token `radii.lg` + `colors.text`, no raw
// hex), token-driven background/border (transparent/none, matching those
// same two reference buttons).
//
// ---------------------------------------------------------------------------
// Layout + no horizontal overflow at 390px
// ---------------------------------------------------------------------------
// AppShell's sticky `topBar` slot already reserves height/positioning (T2) —
// TopBar supplies only the inner flex row: `[menu?] [title] ... [context]`.
// Two independent guards keep this from overflowing a 390px viewport:
//   1. The left group (menu + title) gets `minWidth: 0` and the title itself
//      gets `whiteSpace: 'nowrap', overflow: 'hidden', textOverflow:
//      'ellipsis', minWidth: 0` — a flex item's default min-width is its
//      content size, so `minWidth: 0` is what actually lets it shrink below
//      that and truncate instead of forcing the row wider.
//   2. The whole `<header>` row is `flexWrap: 'wrap'` with the right-hand
//      context group given `marginLeft: 'auto'` — on a single line this
//      pins the context slot to the far right (auto margin absorbs the
//      gap); if the two groups together still don't fit (e.g. an unusually
//      wide set of context nodes on a very narrow screen), the context
//      group wraps to its own row below the title rather than clipping or
//      pushing the page wider. The context group also gets its own
//      `flexWrap: 'wrap'` + `minWidth: 0` so its individual children
//      (task-id/status/cost, etc.) wrap among themselves before ever
//      forcing horizontal scroll.
// No `overflow-x` is set anywhere in this file — AppShell's outer grid
// already has `overflowX: 'hidden'` as the structural backstop (see
// AppShell.jsx's "Horizontal overflow" section); this file's job is only to
// not need it via graceful wrap/truncate.
//
// ---------------------------------------------------------------------------
// Tokens
// ---------------------------------------------------------------------------
// `colors`, `spacing`, `radii`, `typography` — no raw hex anywhere in this
// file. `interactiveState`/`focusRing` are not consumed directly: the menu
// button's keyboard-focus affordance comes from the shared `.focus-ring`
// class (same as every other shell control), and this button has no
// hover/active/selected treatment of its own to token-drive (matching the
// identical, already-shipped drawer-close/collapse-toggle buttons it
// mirrors).

import { useAppShell } from './AppShell'
import { colors, radii, spacing, typography } from '../../theme/tokens'

// Decorative hamburger glyph for the mobile menu button. `currentColor`
// inherits the button's own token-driven `color` (`colors.text` below) — no
// hex literal lives in this icon. Not exported: an internal rendering detail
// of the menu button only (mirrors Sidebar.jsx's `CollapseChevron` pattern).
function MenuIcon() {
  return (
    <svg width="20" height="20" viewBox="0 0 20 20" fill="none" aria-hidden="true">
      <path
        d="M3 5.5h14M3 10h14M3 14.5h14"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
      />
    </svg>
  )
}

/**
 * The top bar's content — fills AppShell's `topBar` slot. Must render inside
 * `<AppShell>` (reads `useAppShell()`). See file header for the full
 * API/landmark/heading/overflow contract.
 */
export function TopBar({ title, children }) {
  const { isMobile, openDrawer } = useAppShell()

  return (
    <header
      style={{
        display: 'flex',
        alignItems: 'center',
        flexWrap: 'wrap',
        rowGap: spacing.xs,
        columnGap: spacing.md,
        width: '100%',
        height: '100%',
        minWidth: 0,
        boxSizing: 'border-box',
        padding: `0 ${spacing.lg}`,
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: spacing.md,
          minWidth: 0,
          flex: '1 1 auto',
        }}
      >
        {isMobile && (
          <button
            type="button"
            onClick={openDrawer}
            aria-label="Open navigation menu"
            className="focus-ring"
            style={{
              // Explicit 44x44 touch target (brief's mobile shell-control
              // floor), independent of the glyph's own visual size — matches
              // AppShell's drawer close button / Sidebar's collapse toggle.
              width: 44,
              height: 44,
              flexShrink: 0,
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
            <MenuIcon />
          </button>
        )}

        {title && (
          <h1
            style={{
              ...typography.heading,
              margin: 0,
              minWidth: 0,
              color: colors.text,
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
            }}
          >
            {title}
          </h1>
        )}
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: spacing.sm,
          minWidth: 0,
          marginLeft: 'auto',
        }}
      >
        {children}
      </div>
    </header>
  )
}
