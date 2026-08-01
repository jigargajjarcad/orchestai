import { defineConfig, devices } from '@playwright/test'

// F2 — Verification Durability.
//
// These tests preserve the runtime verification performed throughout the P2
// redesign, which previously existed only as throwaway scratchpad scripts. They
// cover what jsdom structurally cannot: real layout, real overflow, real focus
// movement, and a real dropped network connection.
//
// Fully offline and deterministic. Two servers are started automatically — a
// stub backend serving fixture data, and the app pointed at it — so no test
// touches a live backend, an external API, or any paid service.

const STUB_PORT = 5188
const APP_PORT = 5199
export const APP_URL = `http://localhost:${APP_PORT}`
export const STUB_URL = `http://localhost:${STUB_PORT}`

// The three widths every P2 responsive verification was performed at.
const VIEWPORTS = {
  desktop: { width: 1280, height: 900 },
  tablet: { width: 768, height: 1024 },
  phone: { width: 375, height: 812 },
}

export default defineConfig({
  testDir: './tests/playwright',
  // Suites share one stub backend, so they must not interleave requests that
  // arm server-side behaviour (see the /__control endpoints).
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],

  use: {
    baseURL: APP_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    // Bundled Chromium, not the machine's Chrome: the scratchpad scripts used
    // channel:'chrome', which is not reproducible on a CI runner.
    browserName: 'chromium',
  },

  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: VIEWPORTS.desktop } },
    { name: 'tablet', use: { ...devices['Desktop Chrome'], viewport: VIEWPORTS.tablet } },
    { name: 'phone', use: { ...devices['Desktop Chrome'], viewport: VIEWPORTS.phone } },
  ],

  webServer: [
    {
      command: 'node tests/playwright/fixtures/stub-api.mjs',
      url: `${STUB_URL}/api/v1/eval-suites`,
      reuseExistingServer: !process.env.CI,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      command: `npx vite --port ${APP_PORT} --strictPort`,
      url: APP_URL,
      reuseExistingServer: !process.env.CI,
      env: { VITE_API_URL: STUB_URL },
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
})
