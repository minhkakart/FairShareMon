import { getSession } from "@/lib/auth/session";
import { request } from "./client";
import { ApiError, ErrorCodes } from "./errors";
import { notifySessionExpired } from "./runtime";
import type { TokenPairResponse } from "./types/auth";

/**
 * Single in-flight refresh shared by every concurrent 401 (de-dup): the first
 * caller kicks off `doRefresh`, all others await the same promise, and the slot
 * clears once it settles.
 */
let refreshPromise: Promise<TokenPairResponse> | null = null;

export function refreshOnce(): Promise<TokenPairResponse> {
  refreshPromise ??= doRefresh().finally(() => {
    refreshPromise = null;
  });
  return refreshPromise;
}

/**
 * Rotate the token pair via `POST /auth/refresh`. ANY failure is terminal — the
 * old pair is revoked server-side, and reuse-detection (2002) revokes every
 * session — so we hard-clear the session and signal the login redirect.
 */
async function doRefresh(): Promise<TokenPairResponse> {
  const { refreshToken } = getSession();
  if (!refreshToken) {
    getSession().clearSession();
    notifySessionExpired();
    throw new ApiError(ErrorCodes.InvalidRefreshToken, "No refresh token", 401);
  }

  try {
    const tokens = await request<TokenPairResponse>(
      "POST",
      "/v1/auth/refresh",
      { body: { refreshToken }, anonymous: true, skipAuthRefresh: true },
    );
    getSession().setSession(tokens);
    return tokens;
  } catch (error) {
    getSession().clearSession();
    notifySessionExpired();
    throw error;
  }
}
