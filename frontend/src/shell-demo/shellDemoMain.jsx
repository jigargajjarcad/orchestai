// frontend/src/shell-demo/shellDemoMain.jsx
//
// P2-M1A Task 5 — entry point for the standalone Shell Demo Vite page
// (shell-demo.html), mirroring src/main.jsx's shape exactly (StrictMode +
// createRoot + the shared index.css import, which loads the self-hosted
// @font-face rules and the `.focus-ring` CSS the real shell/primitives
// depend on) — the same pattern src/gallery/galleryMain.jsx (R1) already
// established for gallery.html. This file is NOT imported by the product
// entry (src/main.jsx) and does not modify it — shell-demo.html points here
// instead of at src/main.jsx, so the two entries are wired independently by
// Vite's multi-page-app convention (any root-level .html is its own entry).

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../index.css'
import ShellDemo from './ShellDemo'

const rootEl = document.getElementById('root')

// Neutralize the legacy `#root` cap for the shell prototype ONLY.
//
// `index.css` sets `#root { width: 1126px; margin: 0 auto; text-align: center;
// border-inline: 1px solid var(--border) }` — a hardcoded 1126px CENTERED
// column. That rule is what constrains the *current product* to a centered
// ~1126px column (the "constrained centered layout" P2-M1B is chartered to
// replace); a child can't un-cap a fixed-width ancestor, so `AppShell` cannot
// fill the viewport while mounted inside it. The application shell is
// deliberately full-bleed (it owns its own per-surface width strategy via
// `PageContainer`), so the prototype must demonstrate that true behavior.
//
// These are INLINE styles on the demo's own mount element only (inline wins
// over the stylesheet rule without `!important`). They do NOT touch the shared
// `index.css` rule and do NOT affect the product entry (`src/main.jsx` /
// `index.html`) — the product's `#root` is unchanged. Removing the legacy cap
// for the product itself is explicit P2-M1B migration work, not P2-M1A.
if (rootEl) {
  rootEl.style.width = '100%'
  rootEl.style.maxWidth = 'none'
  rootEl.style.margin = '0'
  rootEl.style.textAlign = 'left'
  rootEl.style.borderInline = 'none'
}

createRoot(rootEl).render(
  <StrictMode>
    <ShellDemo />
  </StrictMode>,
)
