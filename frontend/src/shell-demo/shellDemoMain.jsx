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

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <ShellDemo />
  </StrictMode>,
)
