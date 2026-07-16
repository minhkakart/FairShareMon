/**
 * Typed accessor over `import.meta.env`. The single source of the API base URL.
 *
 * Dev: the client calls same-origin `/api`; the Vite dev proxy forwards it to
 * VITE_API_BASE_URL (the backend). Prod: VITE_API_BASE_URL is the API origin
 * the client hits directly, and we append the versioned `/api` path here.
 */

function resolveApiBaseUrl(): string {
  // Dev (and test) always go through the same-origin proxy path.
  if (import.meta.env.DEV) {
    return "/api";
  }

  const origin = (import.meta.env.VITE_API_BASE_URL ?? "").trim();
  if (!origin) {
    // Fail loud in a prod build with no API origin configured — never guess.
    throw new Error(
      "VITE_API_BASE_URL is not set. Configure the production API origin at build/deploy time.",
    );
  }
  return `${origin.replace(/\/+$/, "")}/api`;
}

export const env = {
  /** Base URL every API request is built on, ending at `/api` (no version). */
  apiBaseUrl: resolveApiBaseUrl(),
  /** Serve the app against MSW browser mocks instead of the real backend. */
  enableMocks: import.meta.env.VITE_ENABLE_MOCKS === "true",
  isDev: import.meta.env.DEV,
} as const;
