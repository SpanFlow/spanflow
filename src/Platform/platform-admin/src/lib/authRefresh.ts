import type { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '../stores/authStore'

/** Marks a request we have already retried once, so a persistent 401 can't loop. */
type RetriedConfig = InternalAxiosRequestConfig & { _authRetried?: boolean }

/**
 * Turns a 401 into a renewed session instead of a sign-out.
 *
 * The access token lives an hour; before this existed, the first request after that hour cleared
 * localStorage and bounced the user to the login screen — mid-form, mid-pipeline, whatever they were
 * doing. Now the 401 is answered by spending the refresh token and **replaying the original request**,
 * so the hour boundary is invisible: the click that would have signed them out just works.
 *
 * The session still ends when it should — a refresh refused by the server (expired, revoked, account
 * disabled) clears the state and leaves the user a reason on the login screen.
 *
 * Shared by all three clients (Platform, DataFlow, UFMapping) because they all carry the same Platform
 * JWT: renewing it for one renews it for the others, and a per-client copy of this logic would mean
 * three chances to get the retry loop wrong.
 */
export function attachSessionRefresh(client: AxiosInstance) {
  client.interceptors.response.use(
    (response) => response,
    async (error: AxiosError) => {
      if (error.response?.status !== 401) return Promise.reject(error)

      const config = error.config as RetriedConfig | undefined

      // No request to replay, or a replay that came back 401 anyway: the token isn't the problem.
      if (!config || config._authRetried) {
        useAuthStore.getState().endSession('Your session expired. Sign in to pick up where you left off.')
        return Promise.reject(error)
      }

      const token = await useAuthStore.getState().refreshSession()
      if (!token) return Promise.reject(error)

      config._authRetried = true
      config.headers.Authorization = `Bearer ${token}`
      return client(config)
    },
  )
}
