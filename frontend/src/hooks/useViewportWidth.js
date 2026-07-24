import { useEffect, useState } from 'react'

// Tracks window.innerWidth via a resize listener. No CSS media queries, no
// framework — layout components read this value and branch their inline
// `style` objects directly (see ObservabilityPage's stat grids and App.jsx's
// Playground grid/header for the reference usage).

/**
 * Live window.innerWidth, updated on resize.
 *
 * Usage: const width = useViewportWidth()
 *   - width: current window.innerWidth in px (re-renders the caller on resize)
 */
export function useViewportWidth() {
  const [width, setWidth] = useState(() => window.innerWidth)

  useEffect(() => {
    const onResize = () => setWidth(window.innerWidth)
    window.addEventListener('resize', onResize)
    return () => window.removeEventListener('resize', onResize)
  }, [])

  return width
}
