import axios from 'axios'
import { useAuthStore } from '../stores/authStore'
import { attachSessionRefresh } from './authRefresh'

/**
 * Client for the DataFlow API (a separate service on port 5320). It's a sibling of
 * `api.ts`: same Platform JWT attached, same renew-on-401 behavior. DataFlow accepts
 * the Platform token because it's a Platform-connected app (JWT resource server) — which
 * is also why renewing the token at Platform is enough for this client too.
 */
export const dataflowApi = axios.create({
  baseURL: import.meta.env.VITE_DATAFLOW_API_BASE_URL ?? 'http://localhost:5320',
})

// Tenancy (E16-T1): DataFlow scopes every query to the project the admin is browsing.
// ProjectDetailPage sets this; requests carry it as X-Project-Id.
let currentProjectId: string | null = null

export function setDataflowProject(projectId: string | null) {
  currentProjectId = projectId
}

/** The project the admin is browsing — for a Platform call about the same project (E31-T11). */
export function getDataflowProject(): string | null {
  return currentProjectId
}

dataflowApi.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  // A call about another project (the org's API keys page listing each project's apps, E56-T10) names it itself.
  if (currentProjectId && !config.headers['X-Project-Id']) {
    config.headers['X-Project-Id'] = currentProjectId
  }
  return config
})

attachSessionRefresh(dataflowApi)
