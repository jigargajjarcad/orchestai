// F1.1 — browser regressions for the Critical/High findings of the human
// accessibility pass. See docs/f1-1-human-accessibility-report.md.
//
// These are the checks jsdom cannot make honestly: real composited contrast,
// real focus movement under a real Tab keypress, and the React Flow canvas.
// Each test names the measurement that failed before the fix, so a regression
// reports a number rather than "expected true".
import { test, expect } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { openApp, gotoView, gotoSubView } from '../fixtures/app.js'

const AXE = readFileSync('node_modules/axe-core/axe.min.js', 'utf8')

/** Every colour-contrast violation axe finds on the page as it stands. */
async function contrastViolations(page) {
  await page.addScriptTag({ content: AXE })
  return page.evaluate(async () => {
    const res = await window.axe.run(document, {
      runOnly: ['color-contrast'], resultTypes: ['violations'],
    })
    return res.violations.flatMap(v => v.nodes.map(n => {
      const d = (n.any[0] && n.any[0].data) || {}
      return { target: n.target.join(' '), fg: d.fgColor, bg: d.bgColor, ratio: d.contrastRatio }
    }))
  })
}

test.describe('F1.1 — contrast on POPULATED screens', () => {
  // F1 audited eleven surfaces and reported clean, but every audit ran against
  // an empty state. A StatusBadge only exists once a task is selected; a
  // StatusPill only exists inside a graph node or an open detail panel. That
  // is the gap these tests close: they select data first, then measure.
  test('Observability Summary — Completed and Failed badges', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Observability')
    await gotoSubView(page, 'Summary')
    for (const [index, status] of [[1, 'Completed'], [3, 'Failed']]) {
      await page.locator('select').first().selectOption({ index })
      await page.waitForTimeout(700)
      // Assert the badge is actually rendered BEFORE measuring it. Without
      // this, a fixture change that stops rendering the badge turns the
      // assertion below into a no-op that still passes — which is precisely
      // how F1's audit came to certify screens it was not measuring.
      await expect(page.getByText(status, { exact: true }).first()).toBeVisible()
      // Before F1.1: Completed 3.78:1, Failed 2.92:1 against 4.5:1.
      expect(await contrastViolations(page)).toEqual([])
    }
  })

  test('Observability graph + detail panel — StatusPill and the error label', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Observability')
    await page.locator('select').first().selectOption({ index: 1 })
    await page.waitForTimeout(900)
    const nodes = page.locator('.react-flow__node')
    await nodes.nth((await nodes.count()) - 1).click()
    await expect(page.locator('[role=dialog]')).toBeVisible()
    // The StatusPill and the error label are the elements under test here.
    // "Failed" appears twice in the dialog — the pill and the Status stat —
    // and both carry the status text colour, so `.first()` rather than a
    // narrower locator that would break if either moved.
    await expect(page.locator('[role=dialog]').getByText('Failed', { exact: true }).first()).toBeVisible()
    await expect(page.locator('[role=dialog]').getByText(/^Error/).first()).toBeVisible()
    // Before F1.1: pill 2.89:1, "Error · category" 3.18:1, attribution 1.23:1.
    expect(await contrastViolations(page)).toEqual([])
  })

  test('Observability Compare — both panes', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Observability')
    await gotoSubView(page, 'Compare')
    await page.locator('select').nth(0).selectOption({ index: 1 })
    await page.locator('select').nth(1).selectOption({ index: 2 })
    await page.waitForTimeout(900)
    await expect(page.getByText('Completed', { exact: true }).first()).toBeVisible()
    expect(await contrastViolations(page)).toEqual([])
  })

  test('Playground — Running badge mid-run, then rendered agent output', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Playground')
    await page.getByLabel('Task Title').fill('contrast regression')
    await page.getByLabel('Prompt').fill('x')
    await page.getByRole('button', { name: /run agents/i }).click()
    await page.waitForTimeout(500)
    await expect(page.getByText('Running', { exact: true }).first()).toBeVisible()
    // Before F1.1: Running badge 2.62:1 in the TopBar, 2.59:1 on the agent card.
    expect(await contrastViolations(page)).toEqual([])

    await page.waitForTimeout(1800)
    await page.getByRole('radio', { name: 'List' }).click()
    await page.waitForTimeout(500)
    // Before F1.1: inline `code` in the final result rendered #08060d on
    // #313244 — 1.60:1 — because index.css's bare `code` rule inherits colours
    // that flip with prefers-color-scheme while this app is hard-coded dark.
    // Chromium defaults to the light scheme, which is what catches it here.
    await expect(page.locator('#main code').first()).toBeVisible()
    expect(await contrastViolations(page)).toEqual([])
  })

  test('inline code stays legible in BOTH colour schemes', async ({ page }) => {
    // The bug was scheme-dependent, so pinning only one scheme would let it
    // come back for half the users.
    for (const colorScheme of ['light', 'dark']) {
      await page.emulateMedia({ colorScheme })
      await openApp(page)
      await gotoView(page, 'Playground')
      await page.getByLabel('Task Title').fill('scheme probe')
      await page.getByLabel('Prompt').fill('x')
      await page.getByRole('button', { name: /run agents/i }).click()
      await page.waitForTimeout(2300)
      await page.getByRole('radio', { name: 'List' }).click()
      await page.waitForTimeout(400)
      const code = page.locator('#main code').first()
      await expect(code).toBeVisible()
      expect(await contrastViolations(page), `colour-scheme: ${colorScheme}`).toEqual([])
    }
    await page.emulateMedia({ colorScheme: null })
  })
})

test.describe('F1.1 — keyboard', () => {
  test('graph edges are out of the tab order; nodes keep a visible ring', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Observability')
    await page.locator('select').first().selectOption({ index: 1 })
    await page.waitForTimeout(900)

    // Edges were focusable with `outline: none` — an invisible tab stop
    // announcing a raw span id (WCAG 2.4.7).
    const edges = await page.evaluate(
      `[...document.querySelectorAll('.react-flow__edge')].map(e => ({
         tabindex: e.getAttribute('tabindex'),
         ariaHidden: e.getAttribute('aria-hidden'),
         painted: e.querySelectorAll('path').length,
       }))`)
    expect(edges.length).toBeGreaterThan(0)
    expect(edges.every(e => e.tabindex === null || e.tabindex === '-1')).toBe(true)
    // And out of the accessibility tree entirely: removing them from the tab
    // order alone still left "Edge from s1 to s2" for a virtual-cursor user.
    expect(edges.every(e => e.ariaHidden === 'true')).toBe(true)
    // Still drawn — hiding them from AT must not hide them from everyone.
    expect(edges.every(e => e.painted > 0)).toBe(true)

    // The real Chrome accessibility tree, not just the attributes.
    const axNames = await page.locator('.react-flow__renderer').ariaSnapshot()
    expect(axNames).not.toContain('Edge from')

    // Tab to the first node for real, then read the outline the browser applied.
    await page.evaluate(() => { document.documentElement.tabIndex = -1; document.documentElement.focus() })
    let ring = null
    for (let i = 0; i < 40 && !ring; i++) {
      await page.keyboard.press('Tab')
      ring = await page.evaluate(`(() => {
        const e = document.activeElement
        if (!e.matches || !e.matches('.react-flow__node')) return null
        const cs = getComputedStyle(e)
        return { style: cs.outlineStyle, width: parseFloat(cs.outlineWidth) }
      })()`)
    }
    expect(ring, 'never tabbed to a graph node').not.toBeNull()
    expect(ring.style).not.toBe('none')
    expect(ring.width).toBeGreaterThan(0)
  })

  test('the Playground graph applies the same edge rule', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Playground')
    await page.getByLabel('Task Title').fill('edge focus')
    await page.getByLabel('Prompt').fill('x')
    await page.getByRole('button', { name: /run agents/i }).click()
    await page.waitForTimeout(2300)
    const edges = await page.evaluate(
      `[...document.querySelectorAll('.react-flow__edge')].map(e => ({
         tabindex: e.getAttribute('tabindex'), ariaHidden: e.getAttribute('aria-hidden'),
       }))`)
    expect(edges.length).toBeGreaterThan(0)
    expect(edges.every(e => e.tabindex === null || e.tabindex === '-1')).toBe(true)
    // Both graphs share computeGraphLayout, so both inherit the same fix.
    expect(edges.every(e => e.ariaHidden === 'true')).toBe(true)
  })

  test('starting a run does not drop focus to the document body', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Playground')
    await page.getByLabel('Task Title').fill('focus survival')
    await page.getByLabel('Prompt').fill('x')
    const run = page.getByRole('button', { name: /run agents/i })
    await run.focus()

    await page.keyboard.press('Enter')
    // Before F1.1 this read BODY: the `disabled` attribute took focus with it
    // at the exact moment the user committed to the run (WCAG 2.4.3).
    await page.waitForTimeout(300)
    expect(await page.evaluate(`document.activeElement.tagName`)).toBe('BUTTON')
    expect(await page.evaluate(`document.activeElement.getAttribute('aria-disabled')`)).toBe('true')

    await page.waitForTimeout(2000)
    expect(await page.evaluate(`document.activeElement.tagName`)).toBe('BUTTON')
  })
})

test.describe('F1.1 — headings to navigate by', () => {
  const outline = (page) => page.evaluate(
    `[...document.querySelectorAll('#main h1,#main h2,#main h3')].map(h => h.tagName + ':' + h.textContent.trim().slice(0,40))`)

  test('every Observability sub-view offers more than the page title', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Observability')
    await page.locator('select').first().selectOption({ index: 1 })
    for (const sub of ['Summary', 'Cost Dashboard', 'Error Rates', 'Compare']) {
      await gotoSubView(page, sub)
      if (sub === 'Summary') await page.locator('select').first().selectOption({ index: 1 })
      if (sub === 'Compare') {
        await page.locator('select').nth(0).selectOption({ index: 1 })
        await page.locator('select').nth(1).selectOption({ index: 2 })
      }
      await page.waitForTimeout(800)
      // Before F1.1 each of these returned []: the section captions were
      // <Label> spans styled as headings but marked up as nothing.
      expect(await outline(page), `${sub} has no section headings`).not.toEqual([])
    }
  })

  test('the new headings add semantics, not type', async ({ page }) => {
    // `font: inherit` is what keeps this a semantics-only change: without it,
    // index.css's bare `h2` rule (24px, IBM Plex Sans, --text-h) would land on
    // every section caption. jsdom cannot check this — it does not implement
    // the `font` shorthand — so the real computed value is asserted here.
    await openApp(page)
    await gotoView(page, 'Evals')
    const h2 = page.getByRole('heading', { level: 2, name: 'Eval Suites' })
    await expect(h2).toBeVisible()
    const type = await h2.evaluate((el) => {
      const cs = getComputedStyle(el)
      return {
        h2Size: cs.fontSize,
        h2Weight: cs.fontWeight,
        inheritedSize: getComputedStyle(el.parentElement).fontSize,
        labelSize: getComputedStyle(el.firstElementChild).fontSize,
        margin: cs.margin,
      }
    })
    // The <h2> box contributes semantics only: it takes the surrounding type
    // rather than index.css's h2 rule (24px / weight 500), and the Label inside
    // still supplies the caption's own 11px — exactly as when it was a <span>.
    expect(type.h2Size).toBe(type.inheritedSize)
    expect(type.h2Size).not.toBe('24px')
    expect(type.h2Weight).not.toBe('500')
    expect(type.labelSize).toBe('11px')
    expect(type.margin).toBe('0px')
  })

  test('every Evals sub-view offers more than the page title', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Evals')
    await page.getByRole('button', { name: /Roster analysis/ }).click()
    for (const sub of ['Suites', 'Run', 'Results']) {
      await gotoSubView(page, sub)
      if (sub === 'Results') await page.selectOption('select[aria-label=Run]', { index: 1 })
      await page.waitForTimeout(800)
      expect(await outline(page), `${sub} has no section headings`).not.toEqual([])
    }
  })
})
