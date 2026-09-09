/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_URL?: string
  readonly VITE_API_MOCKING?: 'enabled' | 'disabled'
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
