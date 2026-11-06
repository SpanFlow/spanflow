import { useEffect } from 'react'
import { create } from 'zustand'
import { probeAllServices } from '../lib/serviceHealth'

/**
 * E66: which services are not answering, for the top bar's "Service problem" signal. Filled by a quiet
 * health check (on load, every minute, and when the tab comes back into view) and, at once, by any request
 * that found a service unreachable — so the bar explains a red error on the page before anyone goes looking.
 * Shown only while something is wrong; a healthy studio has no status control in the bar at all.
 */
interface ServiceStatusState {
  /** Labels of the services that are down or degraded right now. */
  down: string[]
  check: () => Promise<void>
  reportUnreachable: (label: string) => void
}

let inFlight: Promise<void> | null = null

export const useServiceStatusStore = create<ServiceStatusState>((set, get) => ({
  down: [],
  check: async () => {
    inFlight ??= probeAllServices()
      .then((probes) => set({ down: probes.filter((p) => p.state !== 'ok').map((p) => p.service.label) }))
      .catch(() => {})
      .finally(() => {
        inFlight = null
      })
    await inFlight
  },
  reportUnreachable: (label) => {
    if (!get().down.includes(label)) set({ down: [...get().down, label] })
  },
}))

const EVERY_MS = 60_000

/** Mounted once, in the shell. */
export function useServiceWatch() {
  const check = useServiceStatusStore((s) => s.check)
  useEffect(() => {
    void check()
    const timer = window.setInterval(() => document.visibilityState === 'visible' && void check(), EVERY_MS)
    const onVisible = () => document.visibilityState === 'visible' && void check()
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [check])
}
