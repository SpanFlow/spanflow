/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  readonly VITE_DATAFLOW_API_BASE_URL?: string
  readonly VITE_UFMAPPING_API_BASE_URL?: string
  readonly VITE_SOLUTION_MEMORY_API_BASE_URL?: string
  readonly VITE_PRODUCT_NAME?: string
  readonly VITE_PRODUCT_SHORT?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
