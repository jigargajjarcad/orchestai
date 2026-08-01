import { test, expect } from '@playwright/test'
import {
  openApp, gotoView, gotoSubView,
  pageOverflow, unscrollableOverflow, unnamedControls,
  focusablesWithoutRing, unanchoredHiddenNodes,
} from '../fixtures/app.js'

// The product-wide responsive contract, at every width, on every surface.
// This is the suite that replaces the P2 milestones' scratchpad sweeps.

const SURFACES = [
  { view: 'Playground', subViews: [] },
  { view: 'Memories', subViews: [] },
  { view: 'Observability', subViews: ['Timeline', 'Summary', 'Cost Dashboard', 'Error Rates', 'Compare'] },
  { view: 'Evals', subViews: ['Suites', 'Run', 'Results', 'Post-Hoc'] },
]

for (const { view, subViews } of SURFACES) {
  test.describe(view, () => {
    const places = subViews.length ? subViews : [null]

    for (const sub of places) {
      const label = sub ? `${view} › ${sub}` : view

      test(`${label}: no page horizontal overflow`, async ({ page }) => {
        await openApp(page)
        await gotoView(page, view)
        if (sub) await gotoSubView(page, sub)
        expect(await pageOverflow(page)).toBe(0)
      })

      test(`${label}: nothing overflows outside a scroll container`, async ({ page }) => {
        await openApp(page)
        await gotoView(page, view)
        if (sub) await gotoSubView(page, sub)
        expect(await unscrollableOverflow(page)).toEqual([])
      })

      test(`${label}: every control has an accessible name`, async ({ page }) => {
        await openApp(page)
        await gotoView(page, view)
        if (sub) await gotoSubView(page, sub)
        expect(await unnamedControls(page)).toEqual([])
      })

      test(`${label}: every focusable control shows a focus ring`, async ({ page }) => {
        await openApp(page)
        await gotoView(page, view)
        if (sub) await gotoSubView(page, sub)
        expect(await focusablesWithoutRing(page)).toEqual([])
      })

      test(`${label}: no off-screen node can widen the page`, async ({ page }) => {
        await openApp(page)
        await gotoView(page, view)
        if (sub) await gotoSubView(page, sub)
        expect(await unanchoredHiddenNodes(page)).toEqual([])
      })
    }
  })
}

test.describe('wide content containment', () => {
  test('data tables keep a readable width and scroll in place', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Memories')
    // Wait for the fetched data to render — measuring before it exists gives a
    // null table, and (worse) makes the touch-target check below pass vacuously
    // against zero controls.
    await expect(page.getByRole('table', { name: /Stored agent memories/ })).toBeVisible()

    // Compression is the OPPOSITE failure mode to overflow, and the overflow
    // checks are structurally blind to it: a table pinned to a narrow container
    // always "fits" while rendering one character per line. M6.4 shipped that
    // bug and only a screenshot caught it.
    const fit = await page.evaluate(() => {
      const table = document.querySelector('#main table')
      const wrap = table?.parentElement
      return {
        tableWidth: Math.round(table.getBoundingClientRect().width),
        wrapperOverflowX: getComputedStyle(wrap).overflowX,
        scrollsInPlace: wrap.scrollWidth > wrap.clientWidth,
      }
    })
    expect(fit.tableWidth).toBeGreaterThanOrEqual(600)
    expect(fit.wrapperOverflowX).toBe('auto')
  })

  test('the cost chart scrolls in its own container rather than the page', async ({ page }) => {
    await openApp(page)
    await gotoView(page, 'Observability')
    await gotoSubView(page, 'Cost Dashboard')
    await expect(page.getByRole('img', { name: /Cost over time/ })).toBeVisible()

    // M6.3: the bar row cannot shrink below min-content, so without its own
    // scroll container it pushed the whole page sideways.
    const chart = await page.evaluate(() => {
      const img = document.querySelector('[role="img"]')
      const wrap = img.parentElement
      return { overflowX: getComputedStyle(wrap).overflowX, contained: wrap.scrollWidth >= img.scrollWidth }
    })
    expect(chart.overflowX).toBe('auto')
    expect(chart.contained).toBe(true)
    expect(await pageOverflow(page)).toBe(0)
  })
})

// ── Known gaps, preserved rather than dropped ───────────────────────────────
// These verifications were performed during P2 and FAILED. The causes sit in
// frozen files (theme/tokens.js values, mandated by DESIGN.md) or in design
// decisions no milestone was scoped to change. They are recorded as expected
// failures so the checks stay in the repo and stay visible — and so that fixing
// the underlying issue makes the test fail loudly, prompting the flag's removal.
test.describe('known gaps (frozen — see docs/superpowers/verification)', () => {
  test('WCAG AA contrast on label and header text', async ({ page }) => {
    test.fail(true, 'overlay0 3.36-3.84:1 and surface2 2.81:1 vs 4.5:1 — frozen in tokens.js/DESIGN.md')
    await openApp(page)
    await gotoView(page, 'Memories')
    const failures = await page.evaluate(() => {
      const parse = (c) => {
        const m = c.match(/rgba?\(([^)]+)\)/); if (!m) return null
        const p = m[1].split(',').map(s => parseFloat(s.trim()))
        return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 }
      }
      const lum = ({ r, g, b }) => {
        const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4) }
        return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)
      }
      const over = (fg, bg) => ({
        r: fg.r * fg.a + bg.r * (1 - fg.a),
        g: fg.g * fg.a + bg.g * (1 - fg.a),
        b: fg.b * fg.a + bg.b * (1 - fg.a), a: 1,
      })
      const effBg = (el) => {
        let acc = null
        for (let n = el; n; n = n.parentElement) {
          const c = parse(getComputedStyle(n).backgroundColor)
          if (c && c.a > 0) { acc = acc ? over(acc, c) : c; if (acc.a >= 1) return acc }
        }
        return acc ?? { r: 255, g: 255, b: 255, a: 1 }
      }
      const out = []
      for (const el of document.querySelectorAll('main *')) {
        const text = [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).join('')
        if (!text) continue
        const cs = getComputedStyle(el)
        const r = el.getBoundingClientRect()
        if (r.width < 2 || r.height < 2) continue
        const fg = parse(cs.color); if (!fg) continue
        const bg = effBg(el)
        const composited = fg.a < 1 ? over(fg, bg) : fg
        const L1 = lum(composited), L2 = lum(bg)
        const ratio = (Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05)
        const px = parseFloat(cs.fontSize)
        const large = px >= 24 || (parseInt(cs.fontWeight, 10) >= 700 && px >= 18.66)
        if (ratio + 0.01 < (large ? 3 : 4.5)) out.push({ text: text.slice(0, 30), ratio: Math.round(ratio * 100) / 100 })
      }
      return out
    })
    expect(failures).toEqual([])
  })

  test('44px touch targets on primary controls', async ({ page }) => {
    test.fail(true, 'Playground toggles 23px, Memories Delete 22px vs the spec\'s "44px where practical"')
    await openApp(page)
    await gotoView(page, 'Memories')
    await expect(page.getByRole('table', { name: /Stored agent memories/ })).toBeVisible()
    const small = await page.evaluate(() =>
      [...document.querySelectorAll('main button')]
        .filter(el => el.getBoundingClientRect().height > 0)
        .filter(el => el.getBoundingClientRect().height < 44)
        .map(el => ({ text: el.textContent.trim().slice(0, 20), h: Math.round(el.getBoundingClientRect().height) })))
    expect(small).toEqual([])
  })
})
