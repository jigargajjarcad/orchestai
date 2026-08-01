import { test, expect } from '@playwright/test'
import { openApp, gotoView } from '../fixtures/app.js'

// Shell: the paths a keyboard user needs to move around the app at all.
// jsdom cannot verify these — it does not implement fragment-navigation focus,
// so the skip-link assertion in the vitest suite deliberately stops at the
// contract (href + tabindex) and defers real activation to here.

test.describe('skip link', () => {
  test('is the first focusable element and moves focus into <main>', async ({ page }) => {
    await openApp(page)
    await page.keyboard.press('Tab')

    const focusedClass = await page.evaluate(() => document.activeElement?.className)
    expect(focusedClass).toBe('skip-link')

    await page.keyboard.press('Enter')
    const landedInMain = await page.evaluate(() => {
      const main = document.getElementById('main')
      return document.activeElement === main || main?.contains(document.activeElement)
    })
    expect(landedInMain).toBe(true)
  })

  test('is visible once focused, hidden otherwise', async ({ page }) => {
    await openApp(page)
    const skip = page.locator('a.skip-link')
    // Visually hidden until focus: it must not occupy the layout when unfocused.
    const before = await skip.boundingBox()
    await page.keyboard.press('Tab')
    const after = await skip.boundingBox()
    expect(after.width).toBeGreaterThan(before?.width ?? 0)
  })
})

test.describe('primary navigation', () => {
  const VIEWS = ['Playground', 'Memories', 'Observability', 'Evals']

  for (const view of VIEWS) {
    test(`reaches ${view} and marks it current`, async ({ page }) => {
      await openApp(page)
      await gotoView(page, view)
      await expect(page.getByRole('heading', { level: 1, name: view })).toBeVisible()
    })
  }

  test('nav items are real buttons, operable by keyboard', async ({ page }) => {
    await openApp(page)
    // Phone/tablet hide the sidebar behind the drawer; open it so the items exist.
    const drawerToggle = page.getByRole('button', { name: /open navigation|menu/i })
    if (await drawerToggle.count() && await drawerToggle.first().isVisible()) {
      await drawerToggle.first().click()
    }
    const memories = page.getByRole('button', { name: 'Memories', exact: true }).first()
    await memories.focus()
    await page.keyboard.press('Enter')
    await expect(page.getByRole('heading', { level: 1, name: 'Memories' })).toBeVisible()
  })

  test('exactly one h1 per view, and no skipped heading levels', async ({ page }) => {
    await openApp(page)
    for (const view of VIEWS) {
      await gotoView(page, view)
      const levels = await page.evaluate(() =>
        [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map(h => Number(h.tagName[1])))
      expect(levels.filter(l => l === 1), `${view} h1 count`).toHaveLength(1)
      let prev = levels[0]
      for (const l of levels) {
        if (l > prev) expect(l - prev, `${view} heading jump`).toBeLessThanOrEqual(1)
        prev = l
      }
    }
  })
})

test.describe('mobile drawer', () => {
  // Per AppShell's own contract: the sidebar "does not move into the drawer
  // until mobile". Tablet keeps it in the grid as a collapsed rail, so the
  // drawer exists at phone width only.
  test.skip(({ viewport }) => viewport.width >= 768, 'tablet and up keep the sidebar in the grid')

  test('opens, traps focus, and closes on Escape', async ({ page }) => {
    await openApp(page)
    await page.getByRole('button', { name: /open navigation|menu/i }).first().click()

    const drawer = page.getByRole('dialog')
    await expect(drawer).toBeVisible()
    await expect(drawer).toHaveAttribute('aria-modal', 'true')

    // Focus must start inside the drawer, not left behind on the page.
    const focusInside = await page.evaluate(() => {
      const d = document.querySelector('[role="dialog"]')
      return !!d && d.contains(document.activeElement)
    })
    expect(focusInside).toBe(true)

    // Tab must cycle within the drawer rather than escaping to the page behind.
    for (let i = 0; i < 12; i++) await page.keyboard.press('Tab')
    const stillInside = await page.evaluate(() => {
      const d = document.querySelector('[role="dialog"]')
      return !!d && d.contains(document.activeElement)
    })
    expect(stillInside).toBe(true)

    await page.keyboard.press('Escape')
    await expect(drawer).toBeHidden()
  })

  test('locks background scroll while open', async ({ page }) => {
    await openApp(page)
    await page.getByRole('button', { name: /open navigation|menu/i }).first().click()
    await expect(page.getByRole('dialog')).toBeVisible()
    expect(await page.evaluate(() => document.body.style.overflow)).toBe('hidden')

    await page.keyboard.press('Escape')
    await expect(page.getByRole('dialog')).toBeHidden()
    expect(await page.evaluate(() => document.body.style.overflow)).not.toBe('hidden')
  })
})

test.describe('responsive shell', () => {
  // Three distinct layouts, one per width — asserting the actual contract
  // rather than a two-way desktop/mobile guess.
  test('presents the correct navigation affordance for the viewport', async ({ page, viewport }) => {
    await openApp(page)
    const toggle = page.getByRole('button', { name: /open navigation|menu/i })

    if (viewport.width >= 768) {
      // Desktop AND tablet keep the sidebar in the grid (expanded, or collapsed
      // to an icon rail on tablet) — no drawer toggle at either width.
      await expect(page.getByRole('navigation').first()).toBeVisible()
      await expect(toggle).toHaveCount(0)
    } else {
      // Phone: navigation lives behind the drawer.
      await expect(toggle.first()).toBeVisible()
    }
  })

  test('keeps the primary view reachable at every width', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Observability')
    await expect(page.getByRole('heading', { level: 1, name: 'Observability' })).toBeVisible()
  })
})
