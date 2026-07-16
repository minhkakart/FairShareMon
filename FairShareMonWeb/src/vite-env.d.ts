/// <reference types="vite/client" />

interface ImportMetaEnv {
  /**
   * Dev: the Vite `/api` proxy TARGET (client still calls same-origin `/api`).
   * Prod: the API origin (scheme + host, no trailing slash) the client calls
   * directly. Empty in dev builds.
   */
  readonly VITE_API_BASE_URL: string;
  /** "true" serves the app against MSW browser mocks instead of the backend. */
  readonly VITE_ENABLE_MOCKS?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
