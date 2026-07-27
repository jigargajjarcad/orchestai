// Vitest global setup — registers jest-dom matchers and cleans up the DOM
// between tests. Component-level test harness for P2-M3.
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => {
  cleanup()
})
