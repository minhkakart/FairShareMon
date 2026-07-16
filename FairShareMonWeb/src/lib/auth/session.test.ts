import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { http } from "msw";
import { HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { getSession, sessionStore } from "./session";
import {
  clearRefreshToken,
  loadRefreshToken,
  saveRefreshToken,
} from "./storage";
import { refreshOnce } from "@/lib/api/refresh";
import { registerSessionExpiredHandler } from "@/lib/api/runtime";
import type { TokenPairResponse } from "@/lib/api/types/auth";

/**
 * Verifies the session store's OQ3 storage split (access in memory, refresh in
 * localStorage), boot rehydration via /auth/refresh, and logout teardown.
 */

const REFRESH_KEY = "fsm.refreshToken";

function tokenPair(over: Partial<TokenPairResponse> = {}): TokenPairResponse {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  return {
    accessToken: "access-token",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-token",
    refreshTokenExpiresAt: future,
    ...over,
  };
}

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}

beforeEach(() => {
  window.localStorage.clear();
  sessionStore.setState({
    status: "idle",
    accessToken: null,
    accessTokenExpiresAt: null,
    refreshToken: null,
    refreshTokenExpiresAt: null,
    user: null,
  });
  registerSessionExpiredHandler(null);
});

afterEach(() => {
  registerSessionExpiredHandler(null);
});

describe("refresh-token storage", () => {
  it("Storage_SaveLoadClear_RoundTripsRefreshToken", () => {
    expect(loadRefreshToken()).toBeNull();
    saveRefreshToken("rt-123");
    expect(window.localStorage.getItem(REFRESH_KEY)).toBe("rt-123");
    expect(loadRefreshToken()).toBe("rt-123");
    clearRefreshToken();
    expect(loadRefreshToken()).toBeNull();
  });
});

describe("session store", () => {
  it("Session_SetSession_KeepsAccessInMemoryAndPersistsRefreshOnly", () => {
    getSession().setSession(
      tokenPair({ accessToken: "acc-1", refreshToken: "ref-1" }),
      { username: "demo" },
    );

    const state = getSession();
    expect(state.status).toBe("authenticated");
    expect(state.accessToken).toBe("acc-1");
    expect(state.user).toEqual({ username: "demo" });
    // Refresh token (only) is mirrored into localStorage; access token is not.
    expect(window.localStorage.getItem(REFRESH_KEY)).toBe("ref-1");
    expect(window.localStorage.getItem("fsm.accessToken")).toBeNull();
    const stored = JSON.stringify(window.localStorage);
    expect(stored).not.toContain("acc-1");
  });

  it("Session_ClearSession_WipesTokensStorageAndUser", () => {
    getSession().setSession(tokenPair({ refreshToken: "ref-1" }), {
      username: "demo",
    });
    getSession().clearSession();

    const state = getSession();
    expect(state.status).toBe("unauthenticated");
    expect(state.accessToken).toBeNull();
    expect(state.refreshToken).toBeNull();
    expect(state.user).toBeNull();
    expect(window.localStorage.getItem(REFRESH_KEY)).toBeNull();
  });

  it("Session_MarkUnauthenticated_LeavesTokensButEndsIdle", () => {
    getSession().markUnauthenticated();
    expect(getSession().status).toBe("unauthenticated");
  });
});

describe("boot rehydration via /auth/refresh", () => {
  it("Session_RehydrateWithValidRefreshToken_BecomesAuthenticated", async () => {
    // Simulate a page reload: only the refresh token survived in the store.
    sessionStore.setState({ refreshToken: "surviving-refresh" });

    server.use(
      http.post("*/api/v1/auth/refresh", () =>
        HttpResponse.json<Envelope>({
          data: tokenPair({
            accessToken: "rehydrated",
            refreshToken: "rotated",
          }),
          isSuccess: true,
          error: null,
        }),
      ),
    );

    await refreshOnce();

    expect(getSession().status).toBe("authenticated");
    expect(getSession().accessToken).toBe("rehydrated");
    expect(window.localStorage.getItem(REFRESH_KEY)).toBe("rotated");
  });

  it("Session_RehydrateWithNoRefreshToken_ClearsAndSignalsRedirect", async () => {
    const onExpired = vi.fn();
    registerSessionExpiredHandler(onExpired);

    await expect(refreshOnce()).rejects.toBeDefined();
    expect(getSession().status).toBe("unauthenticated");
    expect(onExpired).toHaveBeenCalledTimes(1);
  });
});
