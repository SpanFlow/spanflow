import { create } from 'zustand'
import { api } from '../lib/api'
import { describeApiError } from '../lib/apiError'
import type { ActivityVisibility, MenuPartKey, OrgSwitcherLayout, UserPreferences } from '../types/api'
import type { GroupStyle } from '../components/canvas/groups/nodeGroups'

interface PreferencesState {
  /** Simple mode (E52-T4), "Easy": a project's rail shows only Start here. */
  simpleMode: boolean
  /** E58: where a platform admin's own activity shows. Always 'everyone' for anyone else. */
  activityVisibility: ActivityVisibility
  /** E58: this account may choose activityVisibility (it is a platform admin). */
  canSetActivityVisibility: boolean
  /** E56-T5: a platform admin keeps their own page views out of the activity log. Always false for anyone else. */
  hidePageOpens: boolean
  /** E58: the account has a password to change. */
  hasPassword: boolean
  /** E61-T6: the look new node groups start with, on every canvas; empty is the built-in default. */
  groupStyle: Partial<GroupStyle>
  /** E66: this person's own org/project switcher layout; null follows their organization. */
  orgSwitcher: OrgSwitcherLayout | null
  /** E66: the left menu parts this person chose; null is the default for their role. */
  menuParts: MenuPartKey[] | null
  loaded: boolean
  /** The last switch that could not be saved; the switch is put back. */
  error: string | null
  fetch: () => Promise<void>
  setSimpleMode: (on: boolean) => Promise<void>
  /** E58: returns the error, or null once saved. */
  setActivityVisibility: (visibility: ActivityVisibility) => Promise<string | null>
  /** E56-T5: returns the error, or null once saved. */
  setHidePageOpens: (hide: boolean) => Promise<string | null>
  /** E61-T6: null goes back to the built-in default. Returns the error, or null once saved. */
  setGroupStyle: (style: Partial<GroupStyle> | null) => Promise<string | null>
  /** E66: null goes back to following the organization. Returns the error, or null once saved. */
  setOrgSwitcher: (layout: OrgSwitcherLayout | null) => Promise<string | null>
  /** E66: null goes back to the default for their role. Returns the error, or null once saved. */
  setMenuParts: (parts: MenuPartKey[] | null) => Promise<string | null>
}

function parseMenuParts(value: string | null | undefined): MenuPartKey[] | null {
  if (!value) return null
  const parts = value.split(',').filter((p): p is MenuPartKey => p === 'project' || p === 'org' || p === 'platform')
  return parts.length ? parts : null
}

/**
 * Easy / Manage reaches every studio open in this browser at once (E53-T3): the split view's pane and
 * other tabs are separate apps that read the preference when they loaded, and a switch that changed only
 * the side it was clicked on would leave the two halves of one screen disagreeing. The storage event fires
 * in every other window of this origin, never in the one that wrote it.
 */
const MODE_KEY = 'studio.simpleMode'

function announce(on: boolean) {
  try {
    window.localStorage.setItem(MODE_KEY, JSON.stringify({ on, at: Date.now() }))
  } catch {
    // storage unavailable: the other windows pick the change up on their next load
  }
}

/**
 * The signed-in person's own interface choices, kept on their account so they follow them to any device.
 * Read once per session; a failure leaves the defaults (everything shown), which is always safe — simple
 * mode only hides menu items, it never stands between someone and something they are allowed to do.
 */
export const usePreferencesStore = create<PreferencesState>((set, get) => ({
  simpleMode: false,
  activityVisibility: 'everyone',
  canSetActivityVisibility: false,
  hidePageOpens: false,
  hasPassword: true,
  groupStyle: {},
  orgSwitcher: null,
  menuParts: null,
  loaded: false,
  error: null,

  fetch: async () => {
    if (get().loaded) return
    try {
      const { data } = await api.get<Partial<UserPreferences>>('/me/preferences')
      set({
        simpleMode: data?.simpleMode === true,
        activityVisibility: data?.activityVisibility ?? 'everyone',
        canSetActivityVisibility: data?.canSetActivityVisibility === true,
        hidePageOpens: data?.hidePageOpens === true,
        hasPassword: data?.hasPassword !== false,
        groupStyle: data?.groupStyle ?? {},
        orgSwitcher: data?.orgSwitcher ?? null,
        menuParts: parseMenuParts(data?.menuParts),
        loaded: true,
      })
    } catch {
      set({ loaded: true })
    }
  },

  setSimpleMode: async (on) => {
    const before = get().simpleMode
    set({ simpleMode: on, error: null })
    try {
      await api.put('/me/preferences', { simpleMode: on })
      announce(on)
    } catch {
      set({ simpleMode: before, error: 'That setting could not be saved. Try again in a moment.' })
    }
  },

  setGroupStyle: async (style) => {
    const before = get().groupStyle
    const next = style ?? {}
    set({ groupStyle: next })
    try {
      // An empty object is the server's "back to the built-in default"; null would mean "leave it".
      await api.put('/me/preferences', { groupStyle: next })
      return null
    } catch (err) {
      set({ groupStyle: before })
      return describeApiError(err, 'save your default look for groups', 'That setting could not be saved. Try again in a moment.')
    }
  },

  setOrgSwitcher: async (layout) => {
    const before = get().orgSwitcher
    set({ orgSwitcher: layout })
    try {
      // An empty string is the server's "follow the organization again"; null would mean "leave it".
      await api.put('/me/preferences', { orgSwitcher: layout ?? '' })
      return null
    } catch (err) {
      set({ orgSwitcher: before })
      return describeApiError(err, 'change how you switch organizations', 'That setting could not be saved. Try again in a moment.')
    }
  },

  setMenuParts: async (parts) => {
    const before = get().menuParts
    set({ menuParts: parts })
    try {
      // An empty string is the server's "back to the default"; null would mean "leave it".
      await api.put('/me/preferences', { menuParts: parts ? parts.join(',') : '' })
      return null
    } catch (err) {
      set({ menuParts: before })
      return describeApiError(err, 'change your left menu', 'That setting could not be saved. Try again in a moment.')
    }
  },

  setActivityVisibility: async (visibility) => {
    const before = get().activityVisibility
    set({ activityVisibility: visibility })
    try {
      // Only the field being changed: a save leaves what it does not name as it is (E58).
      await api.put('/me/preferences', { activityVisibility: visibility })
      return null
    } catch (err) {
      set({ activityVisibility: before })
      return describeApiError(err, 'change who sees your activity', 'That setting could not be saved. Try again in a moment.')
    }
  },

  setHidePageOpens: async (hide) => {
    const before = get().hidePageOpens
    set({ hidePageOpens: hide })
    try {
      await api.put('/me/preferences', { hidePageOpens: hide })
      return null
    } catch (err) {
      set({ hidePageOpens: before })
      return describeApiError(err, 'change whether your page views are logged', 'That setting could not be saved. Try again in a moment.')
    }
  },
}))

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (e) => {
    if (e.key !== MODE_KEY || !e.newValue) return
    try {
      const { on } = JSON.parse(e.newValue) as { on?: unknown }
      if (typeof on === 'boolean') usePreferencesStore.setState({ simpleMode: on, error: null })
    } catch {
      // not ours
    }
  })
}
