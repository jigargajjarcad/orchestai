import { expect } from '@playwright/test'

// Shared navigation and measurement helpers for the Playwright suite.
//
// The measurement helpers are the important part: each one encodes a lesson the
// P2 milestones learned the hard way, and the comments say which, so a future
// reader does not "simplify" a check back into uselessness.

export const STUB_URL = 'http://localhost:5188'
const API_KEY = 'orch_live_stub_key_for_offline_verification'

/** Clear any armed stub behaviour. Call at the start of a test that cares. */
export async function resetStub(request) {
  await request.post(`${STUB_URL}/__control/reset`)
}

/** Arm the stub so the NEXT SSE connection dies mid-run (one-shot). */
export async function armStreamDrop(request) {
  await request.post(`${STUB_URL}/__control/drop-next-stream`)
}

/** Load the app and get past the API-key gate. */
export async function openApp(page) {
  await page.goto('/')
  const key = page.locator('input[placeholder="orch_live_..."]')
  if (await key.count()) {
    await key.fill(API_KEY)
    await page.getByRole('button', { name: /continue/i }).first().click()
  }
  await expect(page.locator('#main')).toBeVisible()
}

/**
 * Navigate to a top-level view, opening the mobile drawer first when the
 * sidebar is collapsed behind it (phone/tablet).
 */
export async function gotoView(page, name) {
  const direct = page.getByRole('button', { name, exact: true })
  if (await direct.count() && await direct.first().isVisible()) {
    await direct.first().click()
  } else {
    await page.getByRole('button', { name: /open navigation|menu/i }).first().click()
    await page.getByRole('button', { name, exact: true }).first().click()
    const close = page.getByRole('button', { name: /close navigation/i })
    if (await close.count() && await close.first().isVisible()) await close.first().click()
  }
  await expect(page.getByRole('heading', { level: 1, name })).toBeVisible()
}

/** Switch an Observability or Evals sub-view. */
export async function gotoSubView(page, name) {
  await page.getByRole('button', { name, exact: true }).first().click()
}

/**
 * Horizontal overflow of the document.
 *
 * documentElement.scrollWidth is the honest signal: it counts content that
 * escapes ancestor clipping. An absolutely-positioned visually-hidden node did
 * exactly that in M6.3 and made the page scroll 86px sideways while every
 * element-level check passed. Note that scrollTo() is NOT a valid probe — it
 * scrolls overflow:hidden containers too, which this app uses on the shell.
 */
export async function pageOverflow(page) {
  return page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth)
}

/**
 * Elements extending past the viewport that are NOT inside a scroll container.
 * Content wider than the viewport is fine when it scrolls in its own box —
 * that is the intended pattern for wide tables and the cost chart.
 */
export async function unscrollableOverflow(page) {
  return page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out = []
    for (const el of document.querySelectorAll('main *')) {
      const r = el.getBoundingClientRect()
      if (r.width === 0 || r.height === 0 || r.right <= vw + 1) continue
      let scrollable = false
      for (let n = el; n && n !== document.body; n = n.parentElement) {
        const ox = getComputedStyle(n).overflowX
        if (ox === 'auto' || ox === 'scroll') { scrollable = true; break }
      }
      if (!scrollable) {
        out.push({ tag: el.tagName, right: Math.round(r.right), text: (el.textContent || '').trim().slice(0, 40) })
      }
    }
    return out.slice(0, 5)
  })
}

/**
 * Form controls with no accessible name.
 */
export async function unnamedControls(page) {
  return page.evaluate(() => {
    const out = []
    for (const el of document.querySelectorAll('main select, main input, main textarea')) {
      const named = (el.id && document.querySelector(`label[for="${CSS.escape(el.id)}"]`)) ||
        el.getAttribute('aria-label') || el.getAttribute('aria-labelledby') || el.closest('label')
      if (!named) out.push({ tag: el.tagName, type: el.type })
    }
    return out
  })
}

/**
 * Focusable controls that show no visible focus indicator.
 * Disabled controls are excluded: being unfocusable is correct for them, not a
 * defect (M6.3 initially reported them as failures).
 */
export async function focusablesWithoutRing(page) {
  return page.evaluate(() => {
    const main = document.getElementById('main')
    if (!main) return []
    const sel = 'a[href],button,select,input,textarea,[tabindex]:not([tabindex="-1"])'
    const focusables = [...main.querySelectorAll(sel)]
      .filter(el => el.offsetParent !== null && !el.disabled)
    const out = []
    for (const el of focusables.slice(0, 30)) {
      el.focus()
      if (document.activeElement !== el) { out.push({ tag: el.tagName, why: 'not focusable' }); continue }
      const cs = getComputedStyle(el)
      const inlineNone = el.style.outline === 'none' || el.style.outlineStyle === 'none'
      const ring = (!inlineNone && cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0) ||
        (cs.boxShadow && cs.boxShadow !== 'none') || el.classList.contains('focus-ring')
      if (!ring) out.push({ tag: el.tagName, text: (el.textContent || '').trim().slice(0, 24) })
    }
    return out
  })
}

/**
 * Visually-hidden nodes that could widen the page.
 *
 * `srOnly` is position:absolute, so with no positioned ancestor its containing
 * block is the viewport — it then escapes every overflow clip and adds its own
 * width to the document. Verified in M6.3: an off-screen table header sat at
 * x=461 in a 375px viewport. The fix is a positioned host, so this asserts the
 * host exists rather than that the node is on-screen.
 */
export async function unanchoredHiddenNodes(page) {
  return page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out = []
    for (const el of document.querySelectorAll('main *')) {
      if (getComputedStyle(el).position !== 'absolute') continue
      const r = el.getBoundingClientRect()
      let positionedHost = false
      for (let n = el.parentElement; n && n !== document.body; n = n.parentElement) {
        if (getComputedStyle(n).position !== 'static') { positionedHost = true; break }
      }
      if (!positionedHost && r.right > vw) {
        out.push({ tag: el.tagName, right: Math.round(r.right), text: (el.textContent || '').trim().slice(0, 40) })
      }
    }
    return out
  })
}
