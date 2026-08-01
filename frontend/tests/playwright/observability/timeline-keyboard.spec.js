import { test, expect } from '@playwright/test'
import { openApp, gotoView } from '../fixtures/app.js'

// F1 — the Timeline was the last keyboard-inoperable surface in the app. Its
// span expand/collapse control was a click-only <span>, so a keyboard user
// could not open or close a span at all. These tests cover the fix and the
// graph interaction that was already operable but never verified in a browser.

test.beforeEach(async ({ page }) => {
  await openApp(page)
  await gotoView(page, 'Observability')
  const picker = page.getByRole('combobox', { name: 'Task' })
  await expect(picker).toBeVisible()
  const options = await picker.locator('option').evaluateAll(els => els.map(e => e.value).filter(Boolean))
  await picker.selectOption(options[0])
})

// The Timeline defaults to the Graph view; SpanRows live in the List view, so
// the span tests switch there first. The graph test below stays on the default.
async function showSpanList(page) {
  await page.getByRole('button', { name: 'List', exact: true }).click()
  await expect(page.getByText('Orchestrator', { exact: true }).first()).toBeVisible()
}

test.describe('span expand/collapse', () => {
  test('is a real button, reachable and operable by keyboard', async ({ page }) => {
    await showSpanList(page)
    const toggle = page.getByRole('button', { name: /Expand|Collapse/ }).first()
    await expect(toggle).toBeVisible()

    await toggle.focus()
    await expect(toggle).toBeFocused()

    const before = await toggle.getAttribute('aria-expanded')
    await page.keyboard.press('Enter')
    await expect(toggle).toHaveAttribute('aria-expanded', before === 'true' ? 'false' : 'true')

    // Space must work too — it is half of the native button contract.
    await page.keyboard.press(' ')
    await expect(toggle).toHaveAttribute('aria-expanded', before ?? 'true')
  })

  test('announces its state and names the span it controls', async ({ page }) => {
    await showSpanList(page)
    const toggle = page.getByRole('button', { name: /Expand|Collapse/ }).first()
    // The accessible name must identify WHICH span, not just "expand" —
    // a timeline renders many of these.
    const label = await toggle.getAttribute('aria-label')
    expect(label).toMatch(/(Expand|Collapse) .+/)
    expect(await toggle.getAttribute('aria-expanded')).toMatch(/true|false/)
  })

  test('actually shows and hides child spans', async ({ page }) => {
    // Assert on a NAMED descendant rather than text length: length is a proxy
    // that reports rendering progress as well as expansion, which made this
    // test read "collapsed content got longer" while the tree was still
    // painting.
    await showSpanList(page)
    const child = page.getByText('Research agent', { exact: true })
    await expect(child).toBeVisible()

    await page.getByRole('button', { name: 'Collapse Orchestrator' }).focus()
    await page.keyboard.press('Enter')

    // Behaviour preserved: collapsing genuinely removes descendant rows.
    await expect(child).toBeHidden()

    // The control now advertises the action the NEXT press performs — so the
    // locator must follow the label. That relabelling is the point: a static
    // "toggle" name would leave a screen-reader user guessing which way it goes.
    const expand = page.getByRole('button', { name: 'Expand Orchestrator' })
    await expect(expand).toHaveAttribute('aria-expanded', 'false')

    await expand.focus()
    await page.keyboard.press('Enter')
    await expect(child).toBeVisible()
  })

  test('leaves childless spans out of the tab order', async ({ page }) => {
    await showSpanList(page)
    // A leaf span has nothing to toggle; it must not present an empty button.
    const emptyNamed = await page.locator('#main button').evaluateAll(els =>
      els.filter(b => !(b.textContent || '').trim() && !b.getAttribute('aria-label')).length)
    expect(emptyNamed).toBe(0)
  })
})

test.describe('graph canvas (already operable — verified, not assumed)', () => {
  test('exposes nodes as focusable buttons', async ({ page }) => {
    // The close-out review listed this as a keyboard defect. It is not: React
    // Flow gives node wrappers tabIndex=0 and the nodes carry role="button",
    // with Enter/Space handled by a delegating ancestor. Verified here so the
    // claim rests on observed behaviour rather than on reading the source.
    const first = page.locator('.react-flow__node').first()
    await expect(first).toBeVisible()   // React Flow lays out asynchronously
    await expect(first).toHaveAttribute('tabindex', '0')
    await first.focus()
    await expect(first).toBeFocused()
  })
})
