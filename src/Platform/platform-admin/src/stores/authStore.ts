import { create, type StoreApi, type UseBoundStore } from 'zustand'
import axios from 'axios'
import type { LoginResponse } from '../types/api'
import { signInUnreachable } from '../lib/signInUnreachable'

const STORAGE_KEY = 'platform-admin.auth'

interface StoredAuth {
  token: string
  /**
   * The long-lived half of the session. The access token above dies after an hour and cannot be
   * renewed on its own; this is what buys the next one, so an afternoon's work doesn't end at the
   * login screen. Rotated server-side on every use — only the newest one works.
   */
  refreshToken: string
  /** When `token` expires, as an ISO string. Kept so a refresh can be spent before a request fails. */
  expiresAt: string
  userId: string
  email: string
  fullName: string
  isPlatformAdmin: boolean
  /**
   * The password in use was set by an admin (E23-T23). Persisted with the rest of the session so a
   * reload cannot walk past the change screen — though the real gate is the server, which refuses
   * every endpoint but the change itself while the token carries the matching claim.
   */
  mustChangePassword: boolean
  /** E57: read-only until the email is verified. The server enforces it; this drives the banner. */
  emailUnverified: boolean
}

function loadStoredAuth(): StoredAuth | null {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as StoredAuth
  } catch {
    return null
  }
}

interface AuthState {
  token: string | null
  refreshToken: string | null
  expiresAt: string | null
  userId: string | null
  email: string | null
  fullName: string | null
  isPlatformAdmin: boolean
  mustChangePassword: boolean
  emailUnverified: boolean
  loading: boolean
  error: string | null
  /**
   * Set when a session ended by itself rather than by someone clicking Log out, so the login screen
   * can say why instead of looking like a random bounce.
   */
  sessionEndedReason: string | null
  login: (email: string, password: string) => Promise<boolean>
  register: (email: string, password: string, fullName: string) => Promise<boolean>
  /**
   * Finishes a social sign-in (E30-T3): posts what the provider sent back, plus the PKCE verifier this
   * tab kept, and takes the same LoginResponse a password sign-in gets.
   */
  completeExternalSignIn: (
    provider: string,
    callback: { code: string; state: string; codeVerifier: string | null },
  ) => Promise<boolean>
  /**
   * Takes a session the API just returned (E57: sign-in or reset by email code, verifying the email) the
   * same way a password sign-in is taken — persisted, and shared with the other tabs.
   */
  adoptSession: (response: LoginResponse) => void
  /** Replaces the signed-in account's own password; the response carries the unblocked token. */
  changePassword: (currentPassword: string, newPassword: string) => Promise<boolean>
  /**
   * Trades the refresh token for a fresh access token and returns it, or null when the session is
   * genuinely over (in which case the session has already been cleared). Concurrent callers share one
   * in-flight request — a page that fires six requests at the hour mark must not spend six refresh
   * tokens, five of which would look like a replay attack to the server.
   */
  refreshSession: () => Promise<string | null>
  /** Clears the session and explains why. Used by the API clients when a refresh can't save it. */
  endSession: (reason: string) => void
  logout: () => void
}

const initial = loadStoredAuth()

function apiBaseUrl() {
  return import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5041'
}

function persist(response: LoginResponse) {
  const stored: StoredAuth = {
    token: response.token,
    refreshToken: response.refreshToken ?? '',
    expiresAt: response.expiresAt,
    userId: response.userId,
    email: response.email,
    fullName: response.fullName,
    isPlatformAdmin: response.isPlatformAdmin,
    mustChangePassword: response.mustChangePassword ?? false,
    emailUnverified: response.emailUnverified ?? false,
  }
  localStorage.setItem(STORAGE_KEY, JSON.stringify(stored))
  return stored
}

function clearStored() {
  localStorage.removeItem(STORAGE_KEY)
}

const signedOut = {
  token: null,
  refreshToken: null,
  expiresAt: null,
  userId: null,
  email: null,
  fullName: null,
  isPlatformAdmin: false,
  mustChangePassword: false,
  emailUnverified: false,
} as const

/**
 * The one in-flight refresh, shared by every caller. Module-level rather than store state because it
 * is a promise, not something a component renders.
 */
let inFlightRefresh: Promise<string | null> | null = null

// The annotation is load-bearing, not decoration: refreshSession() calls useAuthStore.getState()
// inside this very initializer, so without it TypeScript is asked to infer the store's type from an
// expression that mentions the store (TS7022), and the whole auth surface degrades to `any`.
export const useAuthStore: UseBoundStore<StoreApi<AuthState>> = create<AuthState>((set) => ({
  token: initial?.token ?? null,
  refreshToken: initial?.refreshToken ?? null,
  expiresAt: initial?.expiresAt ?? null,
  userId: initial?.userId ?? null,
  email: initial?.email ?? null,
  fullName: initial?.fullName ?? null,
  isPlatformAdmin: initial?.isPlatformAdmin ?? false,
  mustChangePassword: initial?.mustChangePassword ?? false,
  emailUnverified: initial?.emailUnverified ?? false,
  loading: false,
  error: null,
  sessionEndedReason: null,

  login: async (email, password) => {
    set({ loading: true, error: null })
    try {
      const response = await axios.post<LoginResponse>(`${apiBaseUrl()}/auth/login`, { email, password })
      const stored = persist(response.data)
      set({ ...stored, loading: false, sessionEndedReason: null })
      return true
    } catch (err) {
      // The API explains why (invalid_credentials / account_locked / account_disabled);
      // fall back to a network hint when the request never reached the server.
      const axiosErr = err as { response?: { data?: { message?: string } }; request?: unknown }
      const message =
        axiosErr.response?.data?.message ??
        (axiosErr.request && !axiosErr.response
          ? signInUnreachable()
          : 'Invalid email or password.')
      set({ loading: false, error: message })
      return false
    }
  },

  register: async (email, password, fullName) => {
    set({ loading: true, error: null })
    try {
      const response = await axios.post<LoginResponse>(`${apiBaseUrl()}/auth/register`, {
        email,
        password,
        fullName,
      })
      const stored = persist(response.data)
      set({ ...stored, loading: false, sessionEndedReason: null })
      return true
    } catch (err) {
      // The API returns the actual reason(s): Identity validation errors (password too short,
      // no digit, duplicate email, …) come back as { errors: string[] }; a plain refusal as
      // { message }. Surfacing them lets a password-policy failure read as one instead of every
      // failure claiming the email is taken. Fall back to a network hint when nothing arrived.
      const axiosErr = err as {
        response?: { data?: { errors?: string[]; message?: string } }
        request?: unknown
      }
      const data = axiosErr.response?.data
      const message =
        (data?.errors && data.errors.length > 0 ? data.errors.join(' ') : undefined) ??
        data?.message ??
        (axiosErr.request && !axiosErr.response
          ? signInUnreachable()
          : 'Could not register: the email may already be taken.')
      set({ loading: false, error: message })
      return false
    }
  },

  completeExternalSignIn: async (provider, callback) => {
    set({ loading: true, error: null })
    try {
      const response = await axios.post<LoginResponse>(
        `${apiBaseUrl()}/auth/${encodeURIComponent(provider)}/callback`,
        callback,
      )
      const stored = persist(response.data)
      set({ ...stored, loading: false, sessionEndedReason: null })
      return true
    } catch (err) {
      // The API names the refusal ({reason, message}): unverified email, no account and sign-up off,
      // account disabled, a stale or foreign state… The sentence is written for this screen.
      const axiosErr = err as { response?: { data?: { message?: string } }; request?: unknown }
      const message =
        axiosErr.response?.data?.message ??
        (axiosErr.request && !axiosErr.response
          ? signInUnreachable()
          : 'Sign-in was refused.')
      set({ loading: false, error: message })
      return false
    }
  },

  adoptSession: (response) => {
    const stored = persist(response)
    set({ ...stored, loading: false, error: null, sessionEndedReason: null })
  },

  changePassword: async (currentPassword, newPassword) => {
    set({ loading: true, error: null })
    try {
      const token = useAuthStore.getState().token
      const response = await axios.post<LoginResponse>(
        `${apiBaseUrl()}/auth/change-password`,
        { currentPassword, newPassword },
        { headers: { Authorization: `Bearer ${token}` } },
      )
      // The response's token is the point: the one we were holding is refused everywhere else.
      const stored = persist(response.data)
      set({ ...stored, loading: false, sessionEndedReason: null })
      return true
    } catch (err) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      set({ loading: false, error: axiosErr.response?.data?.error ?? 'Could not change the password.' })
      return false
    }
  },

  refreshSession: () => {
    if (inFlightRefresh) return inFlightRefresh

    // Read through localStorage, not just this tab's memory. Tabs share the stored session but not a
    // JavaScript context, so the tab next door may have rotated the token seconds ago — spending our
    // stale copy would look like a replay and take the session down for both of us.
    const refreshToken = loadStoredAuth()?.refreshToken || useAuthStore.getState().refreshToken
    if (!refreshToken) {
      useAuthStore.getState().endSession('Your session has ended. Please sign in again.')
      return Promise.resolve(null)
    }

    const pending = axios
      .post<LoginResponse>(`${apiBaseUrl()}/auth/refresh`, { refreshToken })
      .then((response) => {
        const stored = persist(response.data)
        set({ ...stored, sessionEndedReason: null })
        return stored.token
      })
      .catch((err) => {
        // A 4xx means the session is really over. A network failure is NOT: the API being briefly
        // unreachable must not throw away a refresh token that still works, or a restart of the API
        // would sign everyone out — the exact bug this whole mechanism exists to fix.
        const status = (err as { response?: { status?: number } }).response?.status
        if (status !== undefined && status >= 400 && status < 500) {
          useAuthStore.getState().endSession('Your session expired. Sign in to pick up where you left off.')
        }
        return null
      })
      .finally(() => {
        inFlightRefresh = null
      })

    inFlightRefresh = pending
    return pending
  },

  endSession: (reason) => {
    clearStored()
    set({ ...signedOut, sessionEndedReason: reason })
  },

  logout: () => {
    // Told to the server so the refresh token can't renew anything afterwards, but not waited on:
    // signing out must not depend on the network, and the local state is what the UI reads.
    const refreshToken = loadStoredAuth()?.refreshToken || useAuthStore.getState().refreshToken
    if (refreshToken) {
      void axios.post(`${apiBaseUrl()}/auth/logout`, { refreshToken }).catch(() => {})
    }
    clearStored()
    set({ ...signedOut, sessionEndedReason: null })
  },
}))

/**
 * Keeps sibling tabs in step. `storage` fires only in the *other* tabs, so this is how a tab learns
 * that the one next door renewed the session (adopt the new tokens rather than 401 on the old ones)
 * or signed out (follow it, instead of sitting on a session the server has already dropped).
 */
if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key !== STORAGE_KEY) return

    const stored = loadStoredAuth()
    if (stored) {
      useAuthStore.setState({ ...stored })
    } else if (useAuthStore.getState().token) {
      useAuthStore.setState({ ...signedOut })
    }
  })
}
