import axios from 'axios'
import { useAuthStore } from '../stores/authStore'
import { attachSessionRefresh } from './authRefresh'

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5041',
})

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// An expired access token is renewed and the request replayed, instead of the session being thrown
// away. See authRefresh.ts for why that is the whole point.
attachSessionRefresh(api)
