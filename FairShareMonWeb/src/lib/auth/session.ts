import { createStore } from "zustand/vanilla";
import { useStore } from "zustand";
import type { TokenPairResponse } from "@/lib/api/types/auth";
import {
  clearRefreshToken,
  loadRefreshToken,
  saveRefreshToken,
} from "./storage";

/**
 * What the client knows about the signed-in user. Login returns only a token
 * pair, so immediately after login only `username` — captured optimistically
 * from the form — is guaranteed. The full profile (`uuid`/`tier`/`role`/
 * `createdAt`) is reconciled by `GET /auth/me` (see `useCurrentUserQuery`), which
 * runs after both login and boot-refresh rehydrate. `role` (`USER` | `ADMIN`)
 * drives the admin guard; it is absent until `/auth/me` resolves, so the guard
 * waits on `profileStatus` rather than reading a half-populated user.
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

/**
 * Lifecycle of the `/auth/me` profile fetch, tracked separately from the token
 * `status` so an authenticated user can render immediately while the profile is
 * still resolving (OQ2a), and so guards have a definite "settled" signal (OQ5a).
 * `idle` — no fetch yet (unauthenticated / just cleared); `pending` — authenticated,
 * profile in flight; `resolved` — profile populated; `error` — a non-401 failure
 * left the session authenticated but degraded (`user` stays absent), settling
 * guards into a fail-safe deny rather than an infinite splash (OQ3a).
 */
export type ProfileStatus = "idle" | "pending" | "resolved" | "error";

export interface SessionState {
  status: SessionStatus;
  /** In-memory only — never persisted (OQ3a). */
  accessToken: string | null;
  accessTokenExpiresAt: string | null;
  /** Mirrored into localStorage so the session survives reload. */
  refreshToken: string | null;
  refreshTokenExpiresAt: string | null;
  user: SessionUser | null;
  profileStatus: ProfileStatus;

  /**
   * Store a fresh token pair (login / refresh); persists the refresh token and
   * marks the profile `pending` (a `/auth/me` fetch follows the authenticated
   * transition).
   */
  setSession: (tokens: TokenPairResponse, user?: SessionUser | null) => void;
  /**
   * Attach/replace the resolved current-user payload (from `/auth/me`) without
   * touching tokens; settles `profileStatus` to `resolved`.
   */
  setUser: (user: SessionUser | null) => void;
  /**
   * A non-401 `/auth/me` failure: stay authenticated but degraded — settle
   * `profileStatus` to `error` (guards fail-safe deny), leaving `user` as-is.
   */
  markProfileUnavailable: () => void;
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
  profileStatus: "idle",

  setSession: (tokens, user) => {
    saveRefreshToken(tokens.refreshToken);
    set((state) => ({
      status: "authenticated",
      accessToken: tokens.accessToken,
      accessTokenExpiresAt: tokens.accessTokenExpiresAt,
      refreshToken: tokens.refreshToken,
      refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
      user: user ?? state.user,
      profileStatus: "pending",
    }));
  },

  setUser: (user) => set({ user, profileStatus: "resolved" }),

  markProfileUnavailable: () => set({ profileStatus: "error" }),

  clearSession: () => {
    clearRefreshToken();
    set({
      status: "unauthenticated",
      accessToken: null,
      accessTokenExpiresAt: null,
      refreshToken: null,
      refreshTokenExpiresAt: null,
      user: null,
      profileStatus: "idle",
    });
  },

  markUnauthenticated: () => set({ status: "unauthenticated", profileStatus: "idle" }),
}));

/** Non-React accessors for the API client. */
export function getSession(): SessionState {
  return sessionStore.getState();
}

/** React binding — select a slice of the session. */
export function useSession<T>(selector: (state: SessionState) => T): T {
  return useStore(sessionStore, selector);
}
