import { mappingName, productName, studioName } from './brand'
/**
 * The three services this admin UI talks to, and where each one lives.
 *
 * This exists so a failure can name a service instead of "the API". Every panel's error message used
 * to guess — hard-coded "is the DataFlow API running?" strings, several of them on panels that call
 * Platform — and a reader who trusts the message restarts the wrong process. The base URLs are the
 * same ones the axios clients are built from, so whatever a message names is what the request
 * actually went to, including a VITE_ override that moved it somewhere unexpected.
 */

export type ServiceId = 'platform' | 'dataflow' | 'ufmapping' | 'solutionmemory'

/** The three services every install runs, and the health dialog probes. */
type CoreServiceId = Exclude<ServiceId, 'solutionmemory'>

export interface ServiceDescriptor {
  id: ServiceId
  /** How the service is named to the reader — matches the names used in the run docs. */
  label: string
  baseUrl: string
  /** Where its diagnostic log file is written, for the "and then what?" half of an error message. */
  logHint: string
}

const trim = (url: string) => url.replace(/\/+$/, '')

export const SERVICES: Record<CoreServiceId, ServiceDescriptor> = {
  platform: {
    id: 'platform',
    label: studioName,
    baseUrl: trim(import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5041'),
    logHint: 'Platform.Api/logs/platform-*.log',
  },
  dataflow: {
    id: 'dataflow',
    label: productName,
    baseUrl: trim(import.meta.env.VITE_DATAFLOW_API_BASE_URL ?? 'http://localhost:5320'),
    logHint: 'DataFlow.Api/logs/dataflow-*.log',
  },
  ufmapping: {
    id: 'ufmapping',
    label: mappingName,
    // Defaults must match the axios clients exactly (api.ts, dataflowApi.ts, ufmappingApi.ts) — a
    // status screen that probes a different port than the failing request is worse than none.
    baseUrl: trim(import.meta.env.VITE_UFMAPPING_API_BASE_URL ?? 'http://localhost:5215'),
    logHint: 'UFMapping.Designer.Api/logs/ufmapping-*.log',
  },
}

export const ALL_SERVICES: ServiceDescriptor[] = Object.values(SERVICES)

/**
 * Solution Memory (E59), behind the Concepts section (E59-T16). Optional: an install may not run it, so it
 * is not in {@link ALL_SERVICES} (the health dialog would report it down to everyone who never asked for
 * it), but a failed request to it is still named correctly.
 */
export const SOLUTION_MEMORY: ServiceDescriptor = {
  id: 'solutionmemory',
  label: 'Solution Memory',
  // Must match solutionMemoryApi.ts's default.
  baseUrl: trim(import.meta.env.VITE_SOLUTION_MEMORY_API_BASE_URL ?? 'http://localhost:5450'),
  logHint: "Solution Memory's console output",
}

/**
 * Which service a failed request was aimed at, worked out from the axios config rather than from the
 * panel's own belief about who it calls — those two disagree often enough that trusting the panel is
 * how the wrong service ends up named in the error.
 */
export function serviceForUrl(baseUrl: string | undefined): ServiceDescriptor | null {
  if (!baseUrl) return null
  const normalized = trim(baseUrl)
  return [...ALL_SERVICES, SOLUTION_MEMORY].find((service) => service.baseUrl === normalized) ?? null
}
