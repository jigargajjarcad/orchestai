// frontend/src/playground/usePlaygroundExecutionGraph.js
//
// P2-M5 — React hook wrapping the pure playgroundSpanModel reducer. Consumes the
// same normalized SSE events the Playground card feed already handles (additive:
// the card reducer is untouched) and exposes a live span array for the graph.

import { useReducer, useCallback, useMemo } from 'react'
import { emptyGraphState, applyEvent, toSpanArray } from './playgroundSpanModel'

const RESET = { event: '__reset__' }

function reducer(state, action) {
  if (action === RESET || action?.event === '__reset__') return emptyGraphState()
  return applyEvent(state, action)
}

/**
 * @returns {{
 *   liveSpans: Array,             // TimelineSpanDto-shaped spans for computeGraphLayout
 *   dispatchGraphEvent: (event: {event:string, payload?:object, timestamp?:string}) => void,
 *   reset: () => void,
 * }}
 */
export function usePlaygroundExecutionGraph() {
  const [state, dispatch] = useReducer(reducer, undefined, emptyGraphState)

  const dispatchGraphEvent = useCallback((event) => dispatch(event), [])
  const reset = useCallback(() => dispatch(RESET), [])
  const liveSpans = useMemo(() => toSpanArray(state), [state])

  return { liveSpans, dispatchGraphEvent, reset }
}
