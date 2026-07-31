// frontend/src/components/shell/AppShell.jsx
//
// P2-M1A (Application Shell Prototype) Task 2 — the structural application
// shell: responsive grid + shell state + accessible mobile drawer. This is
// the FIRST place any of these regions exist as real layout — no product
// page is touched, and `sidebar`/`topBar` are plain node SLOTS rendered with
// placeholder content in this task's own verification. The real Sidebar/
// TopBar CONTENT ships in later tasks (T3/T4) and consumes the context this
// file exports.
//
// ---------------------------------------------------------------------------
// Layout, by breakpoint (mobile-first, CSS grid, token-driven — breakpoint
// classification comes from `useBreakpoint()`, P2-M1A Task 1; no new
// resize-listener logic here)
// ---------------------------------------------------------------------------
//   - desktop/wide (>=1024): a 2-column CSS grid, `[sidebar][main]`. The
//     sidebar column's width is `SIDEBAR_WIDTH_EXPANDED` (~240px) or
//     `SIDEBAR_WIDTH_COLLAPSED` (~56px) per the `collapsed` state below.
//     `main` is a flex column: the TopBar slot (sticky top) sits above the
//     `<main role="main">` content region.
//   - tablet (640-1023): the identical 2-column grid — NOT a different
//     layout, just a different *initial* value for `collapsed` (see below).
//     The sidebar stays a real, toggle-able grid column at this width; it
//     does not move into the drawer until mobile.
//   - mobile (<640): a single-column grid (`1fr`) — TopBar + content only.
//     The sidebar is removed from the grid entirely and instead lives in the
//     drawer overlay described below, toggled by `drawerOpen`.
//
// `collapsed` is a single, shared boolean (not per-breakpoint) that persists
// to `localStorage['orchestai.sidebar.collapsed']`, read once on mount and
// written on every `toggleCollapse()` call, guarded so an environment
// without a working `localStorage` (SSR, private-mode quota, disabled
// storage) degrades to in-memory-only state rather than throwing. The
// brief's tablet note — "sidebar defaults to collapsed rail (still
// toggle-able)" — only applies to the very first render when nothing has
// been stored yet: the initial `useState` lazy-initializer resolves to
// `true` if the shell first mounts at tablet width and nothing is in
// storage, `false` otherwise (desktop/wide default expanded). Once a value
// exists (stored OR the user has toggled it this session), that value wins
// regardless of which breakpoint bucket the viewport is in — matching
// "still toggle-able": the tablet default is a starting point, not a floor
// the user can't move off of.
//
// `drawerOpen` is deliberately NOT persisted (in-memory only, always false
// on desktop/tablet/wide) — a drawer that reopened itself on page load would
// be a startling, un-dismissable-feeling default; a collapsed/expanded rail
// choice is a quiet layout preference, an open overlay is not.
//
// ---------------------------------------------------------------------------
// The mobile drawer — a real accessible dialog (the a11y crux of this task)
// ---------------------------------------------------------------------------
// Rendered only when `isMobile && drawerOpen` (conditionally mounted, not
// hidden-but-present — simpler to reason about, and the classic focus-trap
// footguns — trapping focus in an invisible dialog, tabbing into
// off-screen nodes — can't happen if the dialog literally isn't in the DOM
// when closed).
//   - Scrim: a `position: fixed` full-viewport backdrop (`surface.backdrop`)
//     behind the panel; `onClick` calls `closeDrawer()`. `aria-hidden="true"`
//     (it is not itself interactive content, just a dismiss target).
//   - Panel: `role="dialog"` + `aria-modal="true"` + `aria-label="Sidebar
//     navigation"` (the accessible name — T3's real Sidebar has no bearing
//     on this label since AppShell, not Sidebar, owns the dialog framing).
//     `tabIndex={-1}` so the panel itself is a valid programmatic-focus
//     target if the slot content happens to contain zero focusable elements
//     (a real Sidebar in T3 will have nav links, but the placeholder content
//     this task tests with may not).
//   - Focus enters on open: a `useEffect` keyed on `drawerOpen` runs after
//     the panel commits to the DOM, queries it for focusable descendants
//     (`FOCUSABLE_SELECTOR`, the standard interactive-elements list used by
//     every hand-rolled focus-trap implementation), and calls `.focus()` on
//     the first one (falling back to the panel itself if there are none).
//   - Focus trap while open: the same effect attaches a single `keydown`
//     listener on `document` (capture phase, so it runs before any page-level
//     handler). On `Tab`/`Shift+Tab` it re-queries the panel's current
//     focusable set (re-queried per keystroke, not cached, so it stays
//     correct even if the slot content changes while open) and, when focus
//     is on the last element and Tab is pressed (or the first element and
//     Shift+Tab is pressed), calls `preventDefault()` and moves focus to the
//     opposite end — the standard "cycle within" trap. If the panel somehow
//     has zero focusable nodes, Tab is simply swallowed (`preventDefault()`
//     with no focus move) rather than escaping to the page behind the scrim.
//   - Escape closes: the same listener calls `closeDrawer()` on `Escape`.
//   - Focus returns on close: `openDrawer()` records `document.activeElement`
//     (whatever had focus at the moment it was called — the future TopBar
//     menu button in T4, or, in this task's isolated testing, whatever a
//     caller focused before invoking it) into a ref. The trap effect's
//     cleanup function (which fires when `drawerOpen` flips back to `false`,
//     OR on unmount, since a `useEffect` cleanup always runs in both cases)
//     calls `.focus()` on that saved element, guarded with optional chaining
//     so a since-removed trigger doesn't throw.
//   - Body scroll lock: the same effect saves
//     `document.body.style.overflow` before setting it to `'hidden'`, and
//     the cleanup function restores the saved value — so a page that had
//     its own custom `overflow` value (unlikely today, but not assumed away)
//     round-trips correctly, and closing OR unmounting while open both
//     restore it (the cleanup path is shared, not open-close-specific code).
//   - No dependency was added for any of this. A correct trap does not
//     require one at this scope (single dialog, no nested dialogs, no
//     focus-trap-stack) — the `FOCUSABLE_SELECTOR` query + `Tab`/`Shift+Tab`
//     boundary check above is the well-known minimal implementation and is
//     entirely self-contained.
//
// Touch targets: the drawer's own close button is 44x44px (the mobile
// touch-target floor the brief requires for shell controls), independent of
// whatever size T3's real Sidebar nav items end up using.
//
// ---------------------------------------------------------------------------
// Horizontal overflow + sticky TopBar
// ---------------------------------------------------------------------------
// Both grid columns get `minWidth: 0` — the standard CSS-grid fix for a
// child's intrinsic content width (e.g. a wide table) silently forcing the
// grid track, and therefore the whole page, wider than the viewport. The
// outer grid also sets `overflowX: 'hidden'` as a second line of defense.
// Neither of these means "wide content is clipped and lost" — a child that
// needs to show more than its column's width is expected to wrap its own
// content in its own `overflow-x: auto` box (the brief's "wide children
// scroll inside their own box"); that is the child's responsibility, not
// something AppShell can or should do on the child's behalf.
//
// The TopBar slot uses `position: 'sticky', top: 0` rather than `'fixed'`:
// sticky elements reserve their own space in normal document flow (unlike
// fixed, which requires the content below to be manually padded to avoid
// being obscured) — so "reserve its height" is satisfied structurally, by
// construction, not by a magic-number padding value that could drift out of
// sync with the slot's actual rendered height.
//
// ---------------------------------------------------------------------------
// Landmarks
// ---------------------------------------------------------------------------
// AppShell provides `<main role="main">` for the content region. The
// sidebar grid column/drawer panel is plain layout chrome — AppShell does
// NOT render a `<nav>` itself; T3's real Sidebar component supplies that
// element inside the `sidebar` slot. This task's own placeholder slot
// content is deliberately NOT a `<nav>`, to keep this task's verification
// honest about what AppShell itself does vs. what a future Sidebar adds.
//
// ---------------------------------------------------------------------------
// `useAppShell()` — the context surface T3/T4 (Sidebar/TopBar) consume
// ---------------------------------------------------------------------------
// `{ breakpoint, isMobile, isTablet, isDesktop, isWide, collapsed,
//    toggleCollapse, drawerOpen, openDrawer, closeDrawer }`
//   - `breakpoint`: the current breakpoint NAME ('mobile'|'tablet'|
//     'desktop'|'wide'), i.e. `useBreakpoint().name` — not the whole
//     `useBreakpoint()` object, so a consumer can destructure a single
//     string rather than reaching into a nested object.
//   - `isMobile`/`isTablet`/`isDesktop`/`isWide`: passthrough booleans from
//     `useBreakpoint()`.
//   - `collapsed`/`toggleCollapse()`: the sidebar-rail collapse state and
//     its toggle (desktop/tablet/wide only — inert on mobile, where the
//     sidebar isn't in the grid).
//   - `drawerOpen`/`openDrawer()`/`closeDrawer()`: the mobile drawer's open
//     state and imperative open/close (mobile only — always `false` and a
//     no-op-safe elsewhere; `openDrawer()` records the current
//     `document.activeElement` as the focus-return target, so callers don't
//     need to pass a trigger ref themselves).
// `useAppShell()` throws if called outside an `<AppShell>` — the same
// fail-fast contract as any other required-provider context hook in this
// codebase; there is no sensible default shell state to fall back to.
//
// A calling component uses `useAppShell()`, not a prop — this context is
// exported alongside `AppShell` specifically so T3/T4 can consume it without
// AppShell needing to know anything about what Sidebar/TopBar look like.

import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { useBreakpoint } from '../../hooks/useBreakpoint'
import { colors, radii, spacing, surface, elevation } from '../../theme/tokens'

const STORAGE_KEY = 'orchestai.sidebar.collapsed'

// Sidebar grid-column widths (desktop/tablet/wide). Not a theme token today
// (P2-M1A Task 1 only added `contentWidth`/`breakpoints`) — local constants,
// matching the brief's "~240px"/"~56px" figures exactly.
const SIDEBAR_WIDTH_EXPANDED = '240px'
const SIDEBAR_WIDTH_COLLAPSED = '56px'

// Mobile drawer panel width — deliberately NOT tied to the desktop rail
// widths above: on mobile the drawer is the sole navigation surface (not a
// secondary rail alongside content), so it gets a comfortable reading width
// of its own, capped against very narrow viewports via `maxWidth: '85vw'`
// (set inline below) so the scrim is always visibly present at the edge.
const DRAWER_WIDTH = '280px'

// TopBar minimum height — comfortably clears the 44px mobile touch-target
// floor with room for vertical padding around a control.
const TOPBAR_MIN_HEIGHT = '56px'

// Stacking order: TopBar sits above normal content (in case content itself
// uses a low z-index/transform), the drawer scrim above that, and the
// drawer panel above its own scrim. Plain, deliberately sparse integers —
// this shell doesn't need a whole z-index scale, just three ordered layers.
const Z_TOPBAR = 10
const Z_DRAWER_SCRIM = 1000
const Z_DRAWER_PANEL = 1001

// The standard "is this focusable" selector used by hand-rolled focus traps
// — anchors with an href, non-disabled form controls/buttons, and anything
// with an explicit non-negative tabindex.
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'textarea:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

const AppShellContext = createContext(null)

/**
 * Reads the shell state/API that Sidebar (T3) and TopBar (T4) consume.
 * Must be called from a descendant of `<AppShell>` — throws otherwise (no
 * sensible default shell state exists to fall back to silently).
 *
 * Usage: const { collapsed, toggleCollapse, drawerOpen, openDrawer,
 *   closeDrawer, breakpoint, isMobile, isTablet, isDesktop, isWide } =
 *   useAppShell()
 */
// The context-reader hook lives alongside <AppShell> (which provides the context) rather than in a
// separate file, matching this task's 2-file scope; see task-2-report.md.
// eslint-disable-next-line react/only-export-components -- intentional, see comment above
export function useAppShell() {
  const ctx = useContext(AppShellContext)
  if (ctx === null) {
    throw new Error('useAppShell() must be called from a descendant of <AppShell>')
  }
  return ctx
}

// Reads the persisted collapse preference. Returns `null` (not `false`) when
// nothing is stored yet, so the caller can distinguish "no preference saved"
// from "saved as expanded" — the two need different handling for the
// tablet-defaults-collapsed initial value described above.
function readStoredCollapsed() {
  try {
    if (typeof window === 'undefined' || !window.localStorage) return null
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (raw === 'true') return true
    if (raw === 'false') return false
    return null
  } catch {
    // localStorage can throw (Safari private mode with quota 0, storage
    // disabled by policy, etc.) — treat exactly like "unavailable".
    return null
  }
}

function writeStoredCollapsed(value) {
  try {
    if (typeof window === 'undefined' || !window.localStorage) return
    window.localStorage.setItem(STORAGE_KEY, value ? 'true' : 'false')
  } catch {
    // Unavailable/quota-exceeded — the toggle still works for this session
    // via in-memory state, it just won't survive a reload. Not fatal.
  }
}

/**
 * The structural application shell: responsive grid + shell state + the
 * accessible mobile drawer. See the file header above for the full
 * breakpoint/drawer/context contract.
 *
 * Usage:
 *   <AppShell sidebar={<MySidebarContent />} topBar={<MyTopBarContent />}>
 *     <PageContainer>...page content...</PageContainer>
 *   </AppShell>
 *   - `sidebar`/`topBar`/`children`: plain node slots (T3/T4 fill
 *     sidebar/topBar with real content in later tasks; this task tests both
 *     with placeholder nodes).
 */
export function AppShell({ sidebar, topBar, children }) {
  const breakpoint = useBreakpoint()
  const { isMobile, isTablet, isDesktop, isWide } = breakpoint

  const [collapsed, setCollapsed] = useState(() => {
    const stored = readStoredCollapsed()
    if (stored !== null) return stored
    // No stored preference yet: tablet's documented default is the
    // collapsed rail; desktop/wide default expanded. See file header.
    return breakpoint.name === 'tablet'
  })

  const [drawerOpen, setDrawerOpen] = useState(false)

  // The element that had focus when `openDrawer()` was called — restored on
  // close. `useRef`, not state: writing it must never trigger a re-render.
  const drawerTriggerRef = useRef(null)
  const drawerPanelRef = useRef(null)

  const toggleCollapse = useCallback(() => {
    setCollapsed(prev => {
      const next = !prev
      writeStoredCollapsed(next)
      return next
    })
  }, [])

  const openDrawer = useCallback(() => {
    drawerTriggerRef.current = document.activeElement
    setDrawerOpen(true)
  }, [])

  const closeDrawer = useCallback(() => {
    setDrawerOpen(false)
  }, [])

  // Growing past mobile while the drawer happens to be open (e.g. rotating
  // a tablet, or a desktop window drag) closes it — the drawer is a
  // mobile-only affordance, and leaving it "open" as a phantom, invisible
  // dialog (it stops rendering once `isMobile` is false, per the JSX below)
  // would still leave the focus-trap/scroll-lock effect active for no
  // visible surface.
  useEffect(() => {
    if (!isMobile && drawerOpen) {
      closeDrawer()
    }
  }, [isMobile, drawerOpen, closeDrawer])

  // The drawer's full a11y contract: focus-in, focus-trap, Escape, scroll
  // lock, and their symmetric cleanup (close OR unmount). See the file
  // header's "mobile drawer" section for the rationale behind each piece.
  useEffect(() => {
    if (!drawerOpen) return undefined

    const panel = drawerPanelRef.current
    const initialFocusable = panel ? Array.from(panel.querySelectorAll(FOCUSABLE_SELECTOR)) : []
    ;(initialFocusable[0] ?? panel)?.focus()

    const previousBodyOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    function handleKeyDown(event) {
      if (event.key === 'Escape') {
        event.preventDefault()
        closeDrawer()
        return
      }

      if (event.key !== 'Tab' || !panel) return

      const focusable = Array.from(panel.querySelectorAll(FOCUSABLE_SELECTOR))
      if (focusable.length === 0) {
        // Nothing to cycle to — swallow Tab rather than let it escape to
        // whatever is behind the scrim.
        event.preventDefault()
        return
      }

      const first = focusable[0]
      const last = focusable[focusable.length - 1]

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    // Capture phase so this runs before any handler on the page content
    // behind the scrim could see the keystroke.
    document.addEventListener('keydown', handleKeyDown, true)

    return () => {
      document.removeEventListener('keydown', handleKeyDown, true)
      document.body.style.overflow = previousBodyOverflow
      drawerTriggerRef.current?.focus?.()
    }
  }, [drawerOpen, closeDrawer])

  const shellApi = {
    breakpoint: breakpoint.name,
    isMobile,
    isTablet,
    isDesktop,
    isWide,
    collapsed,
    toggleCollapse,
    drawerOpen,
    openDrawer,
    closeDrawer,
  }

  // Mobile removes the sidebar from the grid entirely (it moves to the
  // drawer below); tablet/desktop/wide keep it as a real, width-varying
  // column.
  const showGridSidebar = !isMobile
  const sidebarWidth = collapsed ? SIDEBAR_WIDTH_COLLAPSED : SIDEBAR_WIDTH_EXPANDED

  return (
    <AppShellContext.Provider value={shellApi}>
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: showGridSidebar ? `${sidebarWidth} 1fr` : '1fr',
          minHeight: '100vh',
          width: '100%',
          background: surface.canvas,
          overflowX: 'hidden',
        }}
      >
        {/* P2-M6.1 — skip-to-content link: first focusable element in the shell,
            visually hidden until keyboard focus (see .skip-link in index.css),
            jumps focus to the `<main id="main">` region below. Position:absolute
            (via .skip-link) keeps it out of the grid layout. */}
        <a className="skip-link" href="#main">Skip to main content</a>
        {showGridSidebar && (
          <div
            style={{
              minWidth: 0,
              background: surface.surface,
              borderRight: `1px solid ${surface.border}`,
            }}
          >
            {sidebar}
          </div>
        )}

        <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
          <div
            style={{
              position: 'sticky',
              top: 0,
              zIndex: Z_TOPBAR,
              minHeight: TOPBAR_MIN_HEIGHT,
              background: surface.surface,
              borderBottom: `1px solid ${surface.border}`,
              boxSizing: 'border-box',
            }}
          >
            {topBar}
          </div>

          <main
            id="main"
            role="main"
            tabIndex={-1}
            style={{
              flex: 1,
              minWidth: 0,
              overflowX: 'hidden',
              outline: 'none',
            }}
          >
            {children}
          </main>
        </div>

        {isMobile && drawerOpen && (
          <>
            <div
              aria-hidden="true"
              onClick={closeDrawer}
              style={{
                position: 'fixed',
                inset: 0,
                background: surface.backdrop,
                zIndex: Z_DRAWER_SCRIM,
              }}
            />
            <div
              ref={drawerPanelRef}
              role="dialog"
              aria-modal="true"
              aria-label="Sidebar navigation"
              tabIndex={-1}
              style={{
                position: 'fixed',
                top: 0,
                bottom: 0,
                left: 0,
                width: DRAWER_WIDTH,
                maxWidth: '85vw',
                display: 'flex',
                flexDirection: 'column',
                background: surface.overlay,
                boxShadow: elevation.overlay,
                zIndex: Z_DRAWER_PANEL,
                outline: 'none',
              }}
            >
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'flex-end',
                  padding: spacing.sm,
                  flexShrink: 0,
                }}
              >
                <button
                  type="button"
                  onClick={closeDrawer}
                  aria-label="Close navigation"
                  className="focus-ring"
                  style={{
                    // Explicit 44x44 touch target (brief's mobile
                    // shell-control floor), independent of the glyph's own
                    // visual size.
                    width: 44,
                    height: 44,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    background: 'transparent',
                    border: 'none',
                    borderRadius: radii.lg,
                    color: colors.text,
                    fontSize: 20,
                    lineHeight: 1,
                    cursor: 'pointer',
                  }}
                >
                  ×
                </button>
              </div>

              <div style={{ flex: 1, minWidth: 0, overflowY: 'auto' }}>{sidebar}</div>
            </div>
          </>
        )}
      </div>
    </AppShellContext.Provider>
  )
}
