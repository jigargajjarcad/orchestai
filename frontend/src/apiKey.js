// frontend/src/apiKey.js
//
// In-memory only — never persisted to localStorage/sessionStorage, never baked into the build,
// never sent to any URL as a query parameter, never logged. This is a temporary
// development/testing auth flow, not a production design: any JavaScript-accessible value, even
// one held only in memory, is still readable by an XSS vulnerability during an active session.
// A production design needs a backend-for-frontend session, short-lived tokens, or httpOnly
// cookies — not a long-lived machine API key living in browser JS at all. See ADR-014
// confirmation #10.
//
// One deliberate exception: the SSE stream (App.jsx's `new EventSource(...)`) does NOT go through
// authenticatedFetch and does NOT carry this Authorization header at all — EventSource is a
// native browser API that cannot set custom request headers, a hard platform limitation, not an
// oversight here. Instead, App.jsx first calls POST {id}/stream-ticket through authenticatedFetch
// (a plain fetch(), so the Bearer header applies normally) to mint a short-lived, single-use,
// task-bound ticket, then opens the EventSource with that ticket as a query parameter. The ticket
// is intentionally NOT this long-lived API key — it expires in 60 seconds, works for exactly one
// stream connection, and is scoped to exactly one task, so the "never sent as a query parameter"
// rule above still holds for the actual API key. See ITaskStreamTicketIssuer, Task 1 (Phase 1
// architecture/product validation).

let currentApiKey = null

export function setApiKey(key) {
  currentApiKey = key || null
}

export function getApiKey() {
  return currentApiKey
}

export function hasApiKey() {
  return currentApiKey !== null && currentApiKey !== ''
}

export function clearApiKey() {
  currentApiKey = null
}

// Drop-in replacement for fetch() that injects the Authorization header when a key is set.
// Never appends the key to the URL/query string.
export async function authenticatedFetch(url, options = {}) {
  const headers = { ...(options.headers || {}) }
  if (currentApiKey) {
    headers['Authorization'] = `Bearer ${currentApiKey}`
  }
  return fetch(url, { ...options, headers })
}
