import { test, expect } from '@playwright/test'
import { openApp, resetStub, armStreamDrop } from '../fixtures/app.js'

// The Playground's live behaviour, driven by a REAL SSE connection from the
// stub — including a real dropped TCP connection, which is the one thing the
// mocked-EventSource vitest suite cannot reproduce.

async function startRun(page) {
  await page.getByPlaceholder(/Research LangGraph/i).fill('Workload check')
  await page.getByPlaceholder(/Describe what you need/i).fill('Flag load-management risks')
  await page.getByRole('button', { name: /Run Agents/i }).click()
}

const liveStatus = (page) => page.locator('[data-testid="playground-live-status"]')

test.describe('streaming announcements', () => {
  test.beforeEach(async ({ page, request }) => {
    await resetStub(request)
    await openApp(page)
  })

  test('announces phase transitions through to completion', async ({ page }) => {
    await startRun(page)
    // One polite region carries the whole narrative; the assertions follow the
    // real event sequence the stub emits.
    await expect(liveStatus(page)).toHaveText(/Task started/i)
    await expect(liveStatus(page)).toHaveText(/Research agent started/i)
    await expect(liveStatus(page)).toHaveText(/Task completed/i)
  })

  test('renders streamed agent and tool activity', async ({ page }) => {
    await startRun(page)
    await expect(liveStatus(page)).toHaveText(/Task completed/i)
    await page.getByRole('radio', { name: 'List' }).click()
    await expect(page.getByRole('heading', { name: 'Research' })).toBeVisible()
  })
})

test.describe('dropped stream recovery', () => {
  test.beforeEach(async ({ page, request }) => {
    await resetStub(request)
    await openApp(page)
  })

  test('surfaces a real dropped connection instead of hanging', async ({ page, request }) => {
    // Before the M6.5 fix this left the run visibly in-flight forever: the
    // socket closed, no terminal event could arrive, and nothing told the user.
    await armStreamDrop(request)
    await startRun(page)

    const alert = page.getByRole('alert')
    await expect(alert).toBeVisible()
    await expect(alert).toContainText(/disconnected/i)
    await expect(liveStatus(page)).toHaveText(/disconnected/i)
    await expect(page.getByRole('button', { name: /Reconnect/i })).toBeVisible()

    // No longer presented as running.
    await expect(page.getByRole('button', { name: /Run Agents/i })).toBeVisible()
  })

  test('reconnects with a fresh ticket and resumes to completion', async ({ page, request }) => {
    const ticketRequests = []
    page.on('request', r => { if (r.url().includes('stream-ticket')) ticketRequests.push(r.url()) })

    await armStreamDrop(request)
    await startRun(page)
    await expect(page.getByRole('button', { name: /Reconnect/i })).toBeVisible()
    const ticketsBefore = ticketRequests.length

    await page.getByRole('button', { name: /Reconnect/i }).click()

    // A NEW ticket is minted: the previous one is single-use and already spent,
    // which is exactly why the browser's native EventSource retry cannot help.
    await expect(liveStatus(page)).toHaveText(/Task completed/i)
    expect(ticketRequests.length).toBe(ticketsBefore + 1)
    await expect(page.getByRole('alert')).toHaveCount(0)
    await expect(page.getByRole('button', { name: /Reconnect/i })).toHaveCount(0)
  })

  test('does not report a disconnection when the stream ends normally', async ({ page }) => {
    // Servers routinely close right after the terminal event. Without the
    // settled-guard, every successful run would end with a false failure.
    await startRun(page)
    await expect(liveStatus(page)).toHaveText(/Task completed/i)
    await page.waitForTimeout(400)
    await expect(page.getByRole('button', { name: /Reconnect/i })).toHaveCount(0)
    await expect(page.getByRole('alert')).toHaveCount(0)
  })
})

test.describe('execution graph', () => {
  test.beforeEach(async ({ page, request }) => {
    await resetStub(request)
    await openApp(page)
  })

  test('sizes the graph to the viewport without overflowing the page', async ({ page }) => {
    await startRun(page)
    await expect(liveStatus(page)).toHaveText(/Task completed/i)

    const graph = page.getByRole('img', { name: /execution graph/i })
      .or(page.locator('.react-flow'))
    await expect(graph.first()).toBeVisible()

    const box = await graph.first().boundingBox()
    const vw = page.viewportSize().width
    expect(box.width).toBeLessThanOrEqual(vw)
    expect(await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBe(0)
  })

  test('offers the List view as a fallback', async ({ page }) => {
    await startRun(page)
    await expect(liveStatus(page)).toHaveText(/Task completed/i)
    await page.getByRole('radio', { name: 'List' }).click()
    await expect(page.getByRole('heading', { name: 'Agent Executions' })).toBeVisible()
    await page.getByRole('radio', { name: 'Graph' }).click()
    await expect(page.locator('.react-flow').first()).toBeVisible()
  })
})
