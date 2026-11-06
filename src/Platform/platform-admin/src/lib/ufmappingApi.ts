import axios from 'axios'
import { useAuthStore } from '../stores/authStore'
import { attachSessionRefresh } from './authRefresh'

/**
 * Client for the UFMapping Designer API (a separate service on port 5215). It's a
 * sibling of `api.ts`/`dataflowApi.ts`: same Platform JWT attached, same
 * renew-on-401 behavior. UFMapping accepts the Platform token because it's a
 * Platform-connected app (JWT resource server).
 */
export const ufmappingApi = axios.create({
  baseURL: import.meta.env.VITE_UFMAPPING_API_BASE_URL ?? 'http://localhost:5215',
})

ufmappingApi.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

attachSessionRefresh(ufmappingApi)
