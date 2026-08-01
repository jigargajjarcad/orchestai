import { test, expect } from '@playwright/test'
import { openApp, gotoView, gotoSubView, STUB_URL } from '../fixtures/app.js'

test.beforeEach(async ({ page }) => {
  await openApp(page)
  await gotoView(page, 'Observability')
})

test.describe('sub-view switching', () => {
  test('exposes the switcher as a named group of real buttons', async ({ page }) => {
    const group = page.getByRole('group', { name: 'Observability views' })
    await expect(group).toBeVisible()
    await expect(group.getByRole('button')).toHaveCount(5)
  })

  test('marks the active sub-view with aria-current', async ({ page }) => {
    await gotoSubView(page, 'Cost Dashboard')
    const group = page.getByRole('group', { name: 'Observability views' })
    await expect(group.getByRole('button', { name: 'Cost Dashboard' })).toHaveAttribute('aria-current', /.+/)
    await expect(group.getByRole('button', { name: 'Summary' })).not.toHaveAttribute('aria-current', /.+/)
  })
})

test.describe('cost dashboard', () => {
  test('gives the bar chart a text alternative with a real data table', async ({ page }) => {
    await gotoSubView(page, 'Cost Dashboard')

    const chart = page.getByRole('img', { name: /Cost over time/ })
    await expect(chart).toBeVisible()
    // The off-screen data table is a SIBLING of the role=img node: children of
    // a role=img are presentational and would be dropped from the a11y tree.
    const dataTable = page.getByRole('table', { name: /Cost over time — full data/ })
    await expect(dataTable).toBeAttached()
    expect(await chart.evaluate((el, sel) => el.querySelector(sel) === null, 'table')).toBe(true)
  })

  test('scopes every breakdown header and names the action column', async ({ page }) => {
    await gotoSubView(page, 'Cost Dashboard')
    const table = page.getByRole('table', { name: /Cost and token breakdown/ })
    await expect(table).toBeVisible()
    const unscoped = await table.evaluate(t =>
      [...t.querySelectorAll('th')].filter(th => th.getAttribute('scope') !== 'col').length)
    expect(unscoped).toBe(0)
    await expect(table.getByRole('columnheader', { name: 'Live status' })).toBeAttached()
  })

  test('names the date-range inputs', async ({ page }) => {
    await gotoSubView(page, 'Cost Dashboard')
    const named = await page.evaluate(() =>
      [...document.querySelectorAll('input[type="date"]')].map(i => {
        const l = i.id && document.querySelector(`label[for="${CSS.escape(i.id)}"]`)
        return l ? l.textContent.trim() : null
      }))
    expect(named).toEqual(['From', 'To'])
  })
})

test.describe('error rates', () => {
  test('captions both failure-rate tables', async ({ page }) => {
    await gotoSubView(page, 'Error Rates')
    await expect(page.getByRole('table', { name: 'Failure rates by agent' })).toBeVisible()
    await expect(page.getByRole('table', { name: 'Failure rates by tool' })).toBeVisible()
  })
})

test.describe('request race protection', () => {
  // The defect this guards (fixed in M6.5): switching selection left the
  // previous request in flight, and whichever resolved LAST won — repainting
  // the view with data the picker no longer names.
  test('a superseded summary response does not overwrite the current one', async ({ page }) => {
    let held
    const heldRequest = new Promise(resolve => { held = resolve })

    // Delay the FIRST task's summary until after the second is displayed.
    await page.route('**/tasks/*/summary', async (route) => {
      const url = route.request().url()
      if (url.includes('11111111')) {
        await heldRequest
        return route.continue()
      }
      return route.continue()
    })

    await gotoSubView(page, 'Summary')
    const picker = page.getByRole('combobox', { name: 'Task' })
    const options = await picker.locator('option').evaluateAll(els =>
      els.map(e => e.value).filter(Boolean))

    await picker.selectOption(options[0])           // slow one, held in flight
    await picker.selectOption(options[1])           // fast one, renders

    // The stub derives duration from the task id, so the two are tellable
    // apart: task 1 renders 10.5s, task 2 renders 20.5s. Asserting a
    // task-SPECIFIC value is what makes this test able to fail — comparing
    // generic page text cannot detect an overwrite by identical data.
    await expect(page.getByText('20.5s')).toBeVisible()

    held()                                          // the stale answer arrives
    await expect(page.getByText('10.5s')).toHaveCount(0)
    await expect(page.getByText('20.5s')).toBeVisible()
    expect(await picker.inputValue()).toBe(options[1])
  })
})

test.describe('compare view', () => {
  test('stacks its panels rather than overflowing on narrow viewports', async ({ page, viewport }) => {
    await gotoSubView(page, 'Compare')
    const pickers = page.getByRole('combobox')
    await expect(pickers.first()).toBeVisible()

    const wraps = await page.evaluate(() => {
      const el = document.querySelector('#main div[style*="flex-wrap"], #main div[style*="flexWrap"]')
      return el ? getComputedStyle(el).flexWrap : null
    })
    if (viewport.width < 768) expect(wraps).toBe('wrap')
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBe(0)
  })
})

test.describe('offline determinism', () => {
  test('the suite never reaches a non-stub origin', async ({ page }) => {
    const external = []
    page.on('request', r => {
      const u = r.url()
      if (!u.startsWith('http://localhost:') && !u.startsWith('data:') && !u.startsWith('blob:')) external.push(u)
    })
    for (const sub of ['Timeline', 'Summary', 'Cost Dashboard', 'Error Rates', 'Compare']) {
      await gotoSubView(page, sub)
    }
    expect(external, `unexpected external requests: ${external.join(', ')}`).toEqual([])
    expect(STUB_URL).toContain('localhost')
  })
})
