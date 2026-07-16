import { createStore } from "zustand/vanilla";
import { useStore } from "zustand";
import type { TokenPairResponse } from "@/lib/api/types/auth";
import {
  clearRefreshToken,
  loadRefreshToken,
  saveRefreshToken,
} from "./storage";

/**
 * What the client actually knows about the signed-in user. Login returns only
 * a token pair (no user payload, and there is no `/auth/me` endpoint yet), so
 * only `username` — captured from the login form — is guaranteed. The rest are
 * populated when a real payload provides them. `role` is the ADMIN-guard seam:
 * the backend `UserResponse` does not expose it today (see planning doc), so it
 * is always undefined and the admin route fails safe.
 */
export interface SessionUser {
  username: string;
  uuid?: string;
  tier?: string;
  role?: string;
  createdAt?: string;
}

/**
 * `idle`         — boot, session not yet rehydrated (show a boot splash).
 * `authenticated`— a valid access token is held.
 * `unauthenticated` — no session (rehydrate failed / logged out).
 */
export type SessionStatus = "idle" | "authenticated" | "unauthenticated";

export interface SessionState {
  status: SessionStatus;
  /** In-memory only — never persisted (OQ3a). */
  accessToken: string | null;
  accessTokenExpiresAt: string | null;
  /** Mirrored into localStorage so the session survives reload. */
  refreshToken: string | null;
  refreshTokenExpiresAt: string | null;
  user: SessionUser | null;

  /** Store a fresh token pair (login / refresh); persists the refresh token. */
  setSession: (tokens: TokenPairResponse, user?: SessionUser | null) => void;
  /** Attach/replace the current-user payload without touching tokens. */
  setUser: (user: SessionUser | null) => void;
  /** Terminal: wipe tokens + user, clear storage, mark unauthenticated. */
  clearSession: () => void;
  /** Boot finished with no valid session. */
  markUnauthenticated: () => void;
}

/**
 * Vanilla Zustand store so the API client (a non-React module) can read the
 * access token and react to session changes WITHOUT React hooks (OQ4a). The
 * refresh token is seeded from localStorage on boot; status stays `idle` until
 * the bootstrap refresh resolves.
 */
export const sessionStore = createStore<SessionState>((set) => ({
  status: "idle",
  accessToken: null,
  accessTokenExpiresAt: null,
  refreshToken: loadRefreshToken(),
  refreshTokenExpiresAt: null,
  user: null,

  setSession: (tokens, user) => {
    saveRefreshToken(tokens.refreshToken);
    set((state) => ({
      status: "authenticated",
      accessToken: tokens.accessToken,
      accessTokenExpiresAt: tokens.accessTokenExpiresAt,
      refreshToken: tokens.refreshToken,
      refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
      user: user ?? state.user,
    }));
  },

  setUser: (user) => set({ user }),

  clearSession: () => {
    clearRefreshToken();
    set({
      status: "unauthenticated",
      accessToken: null,
      accessTokenExpiresAt: null,
      refreshToken: null,
      refreshTokenExpiresAt: null,
      user: null,
    });
  },

  markUnauthenticated: () => set({ status: "unauthenticated" }),
}));

/** Non-React accessors for the API client. */
export function getSession(): SessionState {
  return sessionStore.getState();
}

/** React binding — select a slice of the session. */
export function useSession<T>(selector: (state: SessionState) => T): T {
  return useStore(sessionStore, selector);
}
