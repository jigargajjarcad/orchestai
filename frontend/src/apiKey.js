// frontend/src/apiKey.js
//
// In-memory only — never persisted to localStorage/sessionStorage, never baked into the build,
// never sent to any URL as a query parameter, never logged. This is a temporary
// development/testing auth flow, not a production design: any JavaScript-accessible value, even
// one held only in memory, is still readable by an XSS vulnerability during an active session.
// A production design needs a backend-for-frontend session, short-lived tokens, or httpOnly
// cookies — not a long-lived machine API key living in browser JS at all. See ADR-014
// confirmation #10.

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
