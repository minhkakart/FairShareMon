/**
 * Persistence for the refresh token only (OQ3a): the refresh token lives in
 * `localStorage` so the session survives reload; the access token is kept in
 * memory only and never persisted. Reuse-detection + rotation on the backend
 * limit the blast radius of an XSS-readable refresh token.
 */
const REFRESH_TOKEN_KEY = "fsm.refreshToken";

export function loadRefreshToken(): string | null {
  try {
    return window.localStorage.getItem(REFRESH_TOKEN_KEY);
  } catch {
    return null;
  }
}

export function saveRefreshToken(token: string): void {
  try {
    window.localStorage.setItem(REFRESH_TOKEN_KEY, token);
  } catch {
    // Storage unavailable (private mode / disabled) — session just won't persist.
  }
}

export function clearRefreshToken(): void {
  try {
    window.localStorage.removeItem(REFRESH_TOKEN_KEY);
  } catch {
    // no-op
  }
}
