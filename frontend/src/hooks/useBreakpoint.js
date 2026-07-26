import { breakpoints } from '../theme/tokens'
import { useViewportWidth } from './useViewportWidth'

// Classifies window width into the app's four mobile-first breakpoint
// buckets (see `breakpoints` in theme/tokens.js for the threshold values and
// the rationale behind them). Built directly on top of `useViewportWidth` —
// that hook owns the actual resize-listener/`window.innerWidth` tracking;
// this one only adds the width->name classification and the boolean/
// `atLeast` convenience API on top. No new resize logic here.

/**
 * Classifies a raw width (px) into a breakpoint name, mobile-first.
 * Exported standalone (not just used internally) so the width->name mapping
 * can be exercised without mounting a component/React tree.
 *
 * Usage: nameForWidth(1024) -> 'desktop'
 *   - width: a plain number, typically `window.innerWidth` or a test value
 *   - returns: 'mobile' | 'tablet' | 'desktop' | 'wide'
 */
export function nameForWidth(width) {
  if (width >= breakpoints.wide) return 'wide'
  if (width >= breakpoints.desktop) return 'desktop'
  if (width >= breakpoints.tablet) return 'tablet'
  return 'mobile'
}

/**
 * Live breakpoint classification, updated on resize.
 *
 * Usage: const { width, name, isMobile, isTablet, isDesktop, isWide, atLeast } = useBreakpoint()
 *   - width: current window.innerWidth in px (from useViewportWidth)
 *   - name: 'mobile' | 'tablet' | 'desktop' | 'wide' (mobile-first, per
 *     `breakpoints` in theme/tokens.js)
 *   - isMobile/isTablet/isDesktop/isWide: booleans matching `name` exactly
 *     (exactly one is true at a time)
 *   - atLeast(name): true when the current width meets or exceeds the named
 *     threshold (e.g. atLeast('desktop') is true for both 'desktop' and
 *     'wide') — 'mobile' is the implicit floor and always returns true.
 */
export function useBreakpoint() {
  const width = useViewportWidth()
  const name = nameForWidth(width)

  const atLeast = (thresholdName) => {
    if (thresholdName === 'mobile') return true
    return width >= breakpoints[thresholdName]
  }

  return {
    width,
    name,
    isMobile: name === 'mobile',
    isTablet: name === 'tablet',
    isDesktop: name === 'desktop',
    isWide: name === 'wide',
    atLeast,
  }
}
