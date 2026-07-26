// frontend/src/gallery/galleryMain.jsx
//
// R1 Task 6 — entry point for the standalone Component Gallery Vite page
// (gallery.html), mirroring src/main.jsx's shape exactly (StrictMode +
// createRoot + the shared index.css import, which is what actually loads
// the self-hosted @font-face rules and the `.focus-ring` CSS this gallery's
// primitives depend on). This file is NOT imported by the product entry
// (src/main.jsx) and does not modify it — gallery.html points here instead
// of at src/main.jsx, so the two entries are wired independently by Vite's
// multi-page-app convention (any root-level .html is its own entry).

import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '../index.css'
import ComponentGallery from './ComponentGallery'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <ComponentGallery />
  </StrictMode>,
)
