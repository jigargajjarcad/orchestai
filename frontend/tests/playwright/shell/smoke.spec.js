import { test, expect } from '@playwright/test'
import { openApp, gotoView } from '../fixtures/app.js'

// Proves the harness itself works: both offline servers start, the app boots,
// the API-key gate can be passed, and fixture data reaches the UI. If this
// fails, every other failure in the suite is noise.

test('the app boots against the offline stub and renders fixture data', async ({ page }) => {
  await openApp(page)
  await expect(page.getByRole('heading', { level: 1, name: 'Playground' })).toBeVisible()
})

test('the stub backend serves fixture data, not an error state', async ({ page }) => {
  await openApp(page)
  await gotoView(page, 'Memories')
  // Fixture memories render as a real table — not an empty or error state.
  await expect(page.getByRole('table', { name: /Stored agent memories/ })).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})
