import { useEffect, useState } from 'react'

/**
 * Fullscreen toggle for an editor panel: while maximized the panel renders as a
 * fixed overlay covering the viewport; Escape (or the same button) restores it.
 */
export function useMaximized() {
  const [maximized, setMaximized] = useState(false)

  useEffect(() => {
    if (!maximized) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setMaximized(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [maximized])

  return { maximized, toggle: () => setMaximized((v) => !v) }
}
