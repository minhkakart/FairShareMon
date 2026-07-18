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
  /**
   * Origin of the external VietQR bank directory (scheme + host, no trailing
   * slash). The bank picker fetches `${VITE_VIETQR_BASE_URL}/api/vietqr/banks`
   * and renders logos from `.../api/vietqr/images/{imageId}`. Optional — falls
   * back to the public default `https://vietqr.vn` when unset.
   */
  readonly VITE_VIETQR_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
