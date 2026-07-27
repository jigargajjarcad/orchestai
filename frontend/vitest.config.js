import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// Isolated test config (kept separate from vite.config.js so the build stays
// untouched). Component-level tests only for P2-M3 — jsdom + Testing Library.
export default defineConfig({
  plugins: [react()],
  // The app relies on the automatic JSX runtime (no `import React`); ensure any
  // esbuild-transformed JSX uses it too, so module-level JSX resolves correctly.
  esbuild: { jsx: 'automatic' },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.js'],
    include: ['src/**/*.{test,spec}.{js,jsx}'],
    css: false,
  },
})
