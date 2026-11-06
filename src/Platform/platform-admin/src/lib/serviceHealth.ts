import { ALL_SERVICES, type ServiceDescriptor } from './services'

/**
 * Asking all three services what's wrong with them, from the browser.
 *
 * The log file answers this for whoever can get a shell on the host. For everyone else — which for
 * the admin UI is everyone — the readiness endpoint is the only channel, and it already reports
 * per-dependency verdicts. This turns those into one screen, so "something failed" becomes "the engine
 * is up but three updates are unapplied" without leaving the browser.
 *
 * The probe is deliberately a **bare fetch**, not the axios clients: those attach an Authorization
 * header, and a request with credentials is a stricter CORS ask than one without. Diagnosing a
 * possible CORS failure with a request that is itself more likely to be blocked would be a trap.
 */

export type ProbeState = 'ok' | 'degraded' | 'unreachable'

export interface ServiceProbe {
  service: ServiceDescriptor
  state: ProbeState
  /** One sentence for the reader: what is wrong and what to do about it. */
  summary: string
  /** Per-dependency verdicts from /readyz, when the service answered. `label` is what the reader sees. */
  checks: { name: string; label: string; status: string; description?: string }[]
}

/**
 * The services name their checks for operators (`runQueue`, `migrations`); the screen names them for
 * whoever is reading it. The raw name stays on hover and in the descriptions, for the log search.
 */
const CHECK_LABELS: Record<string, string> = {
  database: 'Data store',
  migrations: 'Updates',
  configuration: 'Settings',
  runQueue: 'Job runner',
}

export const checkLabel = (name: string) => CHECK_LABELS[name] ?? name

interface ReadyResponse {
  status?: string
  checks?: Record<string, { status?: string; description?: string }>
}

/** Long enough for a cold service to answer, short enough that three of them don't stall the dialog. */
const TIMEOUT_MS = 5000

export async function probeService(service: ServiceDescriptor): Promise<ServiceProbe> {
  try {
    const response = await fetch(`${service.baseUrl}/readyz`, {
      signal: AbortSignal.timeout(TIMEOUT_MS),
    })

    // A non-JSON answer means something is listening on that port, but it isn't this service — a
    // misdirected base URL, or a dev proxy in the way. Worth saying, because every subsequent call
    // will fail in a way that looks like the service being broken rather than absent.
    const body = (await response.json().catch(() => null)) as ReadyResponse | null
    if (!body?.status) {
      return {
        service,
        state: 'unreachable',
        summary:
          `Something answered at ${service.baseUrl} but it isn't ${service.label}: check the ` +
          `VITE_ base URL for this service, and what else is bound to that port.`,
        checks: [],
      }
    }

    const checks = Object.entries(body.checks ?? {}).map(([name, check]) => ({
      name,
      label: checkLabel(name),
      status: check.status ?? 'Unknown',
      description: check.description,
    }))
    const failed = checks.filter((c) => c.status !== 'Healthy')

    return {
      service,
      state: failed.length === 0 ? 'ok' : 'degraded',
      summary: failed.length === 0
        ? 'Running. Every check passed.'
        : `Running, but ${failed.map((c) => c.label).join(' and ')} ${failed.length === 1 ? 'needs' : 'need'} attention.`,
      checks,
    }
  } catch {
    // Reached for both "nothing is listening" and "the origin was refused", which the browser does
    // not let us tell apart — so say both, and name the check that does.
    return {
      service,
      state: 'unreachable',
      summary:
        `No answer from ${service.baseUrl}. Either it isn't running, or it is running and refused ` +
        `this page's origin (${window.location.origin}) at CORS. Opening ${service.baseUrl}/readyz ` +
        `directly in a tab separates the two: an answer there means CORS, no answer means it's down ` +
        `and its startup log (${service.logHint}) records which check failed.`,
      checks: [],
    }
  }
}

export function probeAllServices(): Promise<ServiceProbe[]> {
  return Promise.all(ALL_SERVICES.map(probeService))
}
