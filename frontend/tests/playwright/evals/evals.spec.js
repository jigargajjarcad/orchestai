import { test, expect } from '@playwright/test'
import { openApp, gotoView, gotoSubView } from '../fixtures/app.js'

test.beforeEach(async ({ page }) => {
  await openApp(page)
  await gotoView(page, 'Evals')
})

test.describe('suite list', () => {
  test('renders suite rows as real buttons, reachable and operable by keyboard', async ({ page }) => {
    // These were click-only <div>s before M6.3 — invisible to keyboard users.
    const row = page.getByRole('button', { name: /Roster analysis regression suite/ })
    await expect(row).toBeVisible()

    await row.focus()
    await expect(row).toBeFocused()
    await page.keyboard.press('Enter')
    await expect(row).toHaveAttribute('aria-current', 'true')
  })

  test('activates a suite row with Space as well as Enter', async ({ page }) => {
    const second = page.getByRole('button', { name: /Code generation smoke suite/ })
    await second.focus()
    await page.keyboard.press(' ')
    await expect(second).toHaveAttribute('aria-current', 'true')
  })

  test('announces the settled suite count in a live region', async ({ page }) => {
    // Scoped to the resolved state on purpose: the Spinner (role=status) owns
    // "loading" and ErrorBanner (role=alert) owns failure. Announcing those
    // here too would make a screen reader read each transition twice.
    const status = page.getByRole('status').filter({ hasText: /eval suite/ })
    await expect(status).toHaveText(/2 eval suites loaded\./)
  })
})

test.describe('sub-view switching', () => {
  test('exposes the switcher as a named group of buttons', async ({ page }) => {
    const group = page.getByRole('group', { name: 'Evaluation views' })
    await expect(group).toBeVisible()
    await expect(group.getByRole('button')).toHaveCount(4)
  })

  test('wraps rather than overflowing at narrow widths', async ({ page }) => {
    const wrap = await page.getByRole('group', { name: 'Evaluation views' })
      .evaluate(el => getComputedStyle(el).flexWrap)
    expect(wrap).toBe('wrap')
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBe(0)
  })
})

test.describe('run and results', () => {
  test('names every control on the Run view', async ({ page }) => {
    await page.getByRole('button', { name: /Roster analysis regression suite/ }).click()
    await gotoSubView(page, 'Run')
    await expect(page.getByRole('textbox', { name: 'Subject version' })).toBeVisible()
    await expect(page.getByRole('combobox', { name: 'Baseline run' })).toBeVisible()
  })

  test('captions the results tables and scopes their headers', async ({ page }) => {
    await page.getByRole('button', { name: /Roster analysis regression suite/ }).click()
    await gotoSubView(page, 'Results')

    const runPicker = page.getByRole('combobox', { name: 'Run' })
    await expect(runPicker).toBeVisible()
    const runs = await runPicker.locator('option').evaluateAll(els =>
      els.map(e => e.value).filter(Boolean))
    await runPicker.selectOption(runs[0])

    const table = page.getByRole('table', { name: /Pass\/fail summary/ })
    await expect(table).toBeVisible()
    const unscoped = await table.evaluate(t =>
      [...t.querySelectorAll('th')].filter(th => th.getAttribute('scope') !== 'col').length)
    expect(unscoped).toBe(0)
  })

  test('names every control on the Post-Hoc view', async ({ page }) => {
    await gotoSubView(page, 'Post-Hoc')
    await expect(page.getByRole('combobox', { name: 'Agent type' })).toBeVisible()
    await expect(page.getByRole('spinbutton', { name: 'Max traces' })).toBeVisible()
    await expect(page.getByRole('textbox', { name: 'Rubric' })).toBeVisible()
  })
})
