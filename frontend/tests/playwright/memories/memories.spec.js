import { test, expect } from '@playwright/test'
import { openApp, gotoView } from '../fixtures/app.js'

test.beforeEach(async ({ page }) => {
  await openApp(page)
  await gotoView(page, 'Memories')
  await expect(page.getByRole('table', { name: /Stored agent memories/ })).toBeVisible()
})

test.describe('table semantics', () => {
  test('scopes every column header and names the action column', async ({ page }) => {
    const table = page.getByRole('table', { name: /Stored agent memories/ })
    const headers = table.getByRole('columnheader')
    await expect(headers).toHaveCount(6)

    const unscoped = await table.evaluate(t =>
      [...t.querySelectorAll('th')].filter(th => th.getAttribute('scope') !== 'col').length)
    expect(unscoped).toBe(0)

    // The action column's name is off-screen so the header stays visually blank.
    await expect(table.getByRole('columnheader', { name: 'Actions' })).toBeAttached()
  })

  test('exposes the section title as a real heading', async ({ page }) => {
    await expect(page.getByRole('heading', { level: 2, name: 'Agent Memories' })).toBeVisible()
  })
})

test.describe('delete affordance', () => {
  test('gives each row a distinct accessible name', async ({ page }) => {
    // "Delete" repeated N times is unusable in a screen reader's button list —
    // and this action is destructive.
    const names = await page.locator('#main tbody button').evaluateAll(els =>
      els.map(e => e.getAttribute('aria-label')))
    expect(names.every(Boolean)).toBe(true)
    expect(new Set(names).size).toBe(names.length)
  })

  test('restores focus to the next row after a keyboard delete', async ({ page }) => {
    // Before M6.4 the row's button was destroyed and focus fell to <body>,
    // dumping a keyboard user back at the top of the document.
    const first = page.locator('#main tbody button').first()
    const firstName = await first.getAttribute('aria-label')
    await first.focus()
    await page.keyboard.press('Enter')

    await expect(page.getByRole('button', { name: firstName })).toHaveCount(0)

    const focused = await page.evaluate(() => ({
      isBody: document.activeElement === document.body,
      label: document.activeElement?.getAttribute?.('aria-label') ?? null,
    }))
    expect(focused.isBody).toBe(false)
    expect(focused.label).toBeTruthy()
    expect(focused.label).not.toBe(firstName)
  })

  test('falls back to the section heading once no rows remain', async ({ page }) => {
    const buttons = page.locator('#main tbody button')
    let remaining = await buttons.count()
    while (remaining > 0) {
      await buttons.first().focus()
      await page.keyboard.press('Enter')
      await expect(buttons).toHaveCount(remaining - 1)
      remaining -= 1
    }

    await expect(page.getByText('No memories yet')).toBeVisible()
    const onHeading = await page.evaluate(() =>
      document.activeElement === document.querySelector('#main h2'))
    expect(onHeading).toBe(true)
  })
})

test.describe('responsive table', () => {
  test('scrolls in place instead of crushing its columns', async ({ page }) => {
    const fit = await page.evaluate(() => {
      const table = document.querySelector('#main table')
      const wrap = table.parentElement
      return {
        tableWidth: Math.round(table.getBoundingClientRect().width),
        wrapperWidth: Math.round(wrap.getBoundingClientRect().width),
        overflowX: getComputedStyle(wrap).overflowX,
      }
    })
    // A table pinned to a narrow container "fits" while rendering one character
    // per line — the failure mode overflow checks cannot see.
    expect(fit.tableWidth).toBeGreaterThanOrEqual(600)
    expect(fit.overflowX).toBe('auto')
  })
})
