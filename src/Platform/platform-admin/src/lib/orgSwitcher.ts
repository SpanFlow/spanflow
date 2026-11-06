import { useEffect } from 'react'
import { create } from 'zustand'
import { matchPath, useLocation } from 'react-router-dom'
import { api } from './api'
import { usePreferencesStore } from '../stores/preferencesStore'
import type { OrgAccountSettingsView, OrgSwitcherLayout } from '../types/api'

/**
 * E66: the studio shell's "where am I" — which organization and project the page is in, the last place this
 * browser worked in (so sign-in lands there instead of on a list of one), and which switcher layout applies.
 */

export const DEFAULT_LAYOUT: OrgSwitcherLayout = 'both'

export const LAYOUT_LABELS: Record<OrgSwitcherLayout, { label: string; help: string }> = {
  both: {
    label: 'Org rail + project picker',
    help: 'A column of organization icons on the left, and one project picker in the top bar that searches every organization.',
  },
  picker: {
    label: 'One picker',
    help: 'One button in the top bar: organizations on the left, that organization’s projects on the right, searchable.',
  },
  rail: {
    label: 'Org rail only',
    help: 'A column of organization icons on the left; the project is chosen at the top of the project’s menu.',
  },
}

export const isLayout = (v: unknown): v is OrgSwitcherLayout => v === 'both' || v === 'picker' || v === 'rail'

/** The org and project the current page belongs to, read from the address. */
export function placeFromPath(pathname: string): { orgId: string | null; projectId: string | null } {
  const project = matchPath('/orgs/:orgId/projects/:projectId/*', pathname)
  if (project) return { orgId: project.params.orgId ?? null, projectId: project.params.projectId ?? null }
  const org = matchPath('/orgs/:orgId/*', pathname)
  return { orgId: org?.params.orgId ?? null, projectId: null }
}

export function useCurrentPlace() {
  return placeFromPath(useLocation().pathname)
}

// ---- last place (per browser) ----------------------------------------------------------------------------

const PLACE_KEY = 'studio.lastPlace'

interface StoredPlace {
  orgId?: string
  projectId?: string
  /** The last project opened in each org, so switching org returns to where you were in it. */
  byOrg?: Record<string, string>
}

function readPlace(): StoredPlace {
  try {
    const raw = JSON.parse(window.localStorage.getItem(PLACE_KEY) ?? '{}') as StoredPlace
    return raw && typeof raw === 'object' ? raw : {}
  } catch {
    return {}
  }
}

/** Remembered on every project page; storage that is unavailable just means no memory. */
export function rememberPlace(orgId: string, projectId: string | null) {
  try {
    const place = readPlace()
    const byOrg = { ...(place.byOrg ?? {}) }
    if (projectId) byOrg[orgId] = projectId
    window.localStorage.setItem(PLACE_KEY, JSON.stringify({ orgId, projectId: projectId ?? byOrg[orgId], byOrg }))
  } catch {
    // private window or blocked storage
  }
}

export function lastPlace(): { orgId: string | null; projectId: string | null } {
  const place = readPlace()
  return { orgId: place.orgId ?? null, projectId: place.projectId ?? null }
}

export function lastProjectIn(orgId: string): string | null {
  return readPlace().byOrg?.[orgId] ?? null
}

/** Where switching to an org lands: the project last used there, else the org itself. */
export function hrefForOrg(orgId: string): string {
  const projectId = lastProjectIn(orgId)
  return projectId ? `/orgs/${orgId}/projects/${projectId}` : `/orgs/${orgId}`
}

/** Remembers the place of whatever page is open — mounted once, in the shell. */
export function useRememberPlace() {
  const { orgId, projectId } = useCurrentPlace()
  useEffect(() => {
    if (orgId) rememberPlace(orgId, projectId)
  }, [orgId, projectId])
}

// ---- which layout ----------------------------------------------------------------------------------------

interface OrgLayoutState {
  /** Each org's effective layout (its own choice, else the platform's), once read. */
  byOrg: Record<string, OrgSwitcherLayout>
  fetch: (orgId: string) => Promise<void>
  /** After an org admin changes it, so the shell follows at once. */
  set: (orgId: string, layout: OrgSwitcherLayout) => void
}

const inFlight = new Set<string>()

export const useOrgLayoutStore = create<OrgLayoutState>((set, get) => ({
  byOrg: {},
  fetch: async (orgId) => {
    if (get().byOrg[orgId] || inFlight.has(orgId)) return
    inFlight.add(orgId)
    try {
      const { data } = await api.get<OrgAccountSettingsView>(`/orgs/${orgId}/account-settings`)
      const layout = isLayout(data?.effective?.orgSwitcher) ? data.effective.orgSwitcher : DEFAULT_LAYOUT
      set({ byOrg: { ...get().byOrg, [orgId]: layout } })
    } catch {
      // Unreadable: the built-in default stands. Layout is menu shape only, never access.
    } finally {
      inFlight.delete(orgId)
    }
  },
  set: (orgId, layout) => set({ byOrg: { ...get().byOrg, [orgId]: layout } }),
}))

/** The person's own choice, else the current org's, else the platform default. */
export function useOrgSwitcherLayout(orgId: string | null): OrgSwitcherLayout {
  const own = usePreferencesStore((s) => s.orgSwitcher)
  const fromOrg = useOrgLayoutStore((s) => (orgId ? s.byOrg[orgId] : undefined))
  const fetch = useOrgLayoutStore((s) => s.fetch)
  useEffect(() => {
    if (orgId && !own) void fetch(orgId)
  }, [orgId, own, fetch])
  return own ?? fromOrg ?? DEFAULT_LAYOUT
}

// ---- what the shell has open -----------------------------------------------------------------------------

export type ShellPanel = 'picker' | 'palette' | 'shortcuts' | 'admin' | 'account' | 'projects' | 'status' | null

interface ShellState {
  open: ShellPanel
  /** Text the palette opens with (the `O` key in a layout without a picker opens it on "switch"). */
  paletteQuery: string
  show: (panel: Exclude<ShellPanel, null>, paletteQuery?: string) => void
  toggle: (panel: Exclude<ShellPanel, null>) => void
  close: () => void
}

/** One thing open at a time: opening the palette closes the account menu, and so on. */
export const useShellStore = create<ShellState>((set, get) => ({
  open: null,
  paletteQuery: '',
  show: (panel, paletteQuery = '') => set({ open: panel, paletteQuery }),
  toggle: (panel) => set({ open: get().open === panel ? null : panel, paletteQuery: '' }),
  close: () => set({ open: null, paletteQuery: '' }),
}))
