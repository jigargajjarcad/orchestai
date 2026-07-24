import { useCallback, useEffect, useState } from 'react'

// Top-level view routing only. This intentionally knows nothing about
// page-local sub-tabs (e.g. ObservabilityPage's Timeline/Summary/Cost
// Dashboard/Error Rates/Compare, EvalsPage's Suites/Run/Results/Post-Hoc) —
// those remain local useState in their own components, untouched by this hook.

const DEFAULT_VIEW = 'playground'

const PATH_TO_VIEW = {
  '/': 'playground',
  '/memories': 'memories',
  '/observability': 'observability',
  '/evals': 'evals',
}

const VIEW_TO_PATH = {
  playground: '/',
  memories: '/memories',
  observability: '/observability',
  evals: '/evals',
}

function viewFromLocation() {
  return PATH_TO_VIEW[window.location.pathname] ?? DEFAULT_VIEW
}

/**
 * Small custom client-side router for the four top-level app views.
 * No routing library — uses native history.pushState/popstate directly.
 *
 * Usage: const { view, navigate } = useRouting()
 *   - view: 'playground' | 'memories' | 'observability' | 'evals'
 *   - navigate(nextView): pushes the matching URL and updates `view`
 */
export function useRouting() {
  const [view, setView] = useState(viewFromLocation)

  useEffect(() => {
    const onPopState = () => setView(viewFromLocation())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const navigate = useCallback((nextView) => {
    const path = VIEW_TO_PATH[nextView] ?? VIEW_TO_PATH[DEFAULT_VIEW]
    if (window.location.pathname !== path) {
      window.history.pushState(null, '', path)
    }
    setView(nextView)
  }, [])

  return { view, navigate }
}
