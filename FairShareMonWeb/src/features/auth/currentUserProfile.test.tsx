import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
} from "vitest";
import { act, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { getSession, sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import {
  currentUserQueryKey,
  invalidateCurrentUser,
  useCurrentUserQuery,
} from "./hooks/useCurrentUserQuery";
import { authApi } from "./api/authApi";
import { ProtectedRoute } from "@/routes/ProtectedRoute";
import { AdminRoute } from "@/routes/AdminRoute";
import { AppShellLayout } from "@/routes/AppShellLayout";
import { LoginPage } from "./pages/LoginPage";
import {
  registerSessionExpiredHandler,
  setActiveLocale,
} from "@/lib/api/runtime";
import i18n from "@/i18n";

/**
 * Wire current-user profile (`GET /auth/me`) + activate the admin guard.
 *
 * Exercises the REAL hook → Zustand store → route guards → shell against MSW at
 * the network boundary (never mocks the client/hook). The store is the canonical
 * `user` read; TanStack Query is the fetch/cache that feeds it (OQ1a).
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}

function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}
function fail(code: number, message: string, status: number) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message } },
    { status },
  );
}

const DEMO_PROFILE = {
  uuid: "uuid-demo",
  username: "demo",
  tier: "FREE",
  role: "USER",
  createdAt: "2026-01-01T00:00:00+00:00",
};
const ADMIN_PROFILE = {
  uuid: "uuid-admin",
  username: "admin",
  tier: "PREMIUM",
  role: "ADMIN",
  createdAt: "2026-01-01T00:00:00+00:00",
};

const FUTURE = () => new Date(Date.now() + 3_600_000).toISOString();

/**
 * Put the store into an authenticated state as if a token was just acquired but
 * `/auth/me` has NOT yet resolved (the boot-rehydrate shape: tokens only, no
 * user). The `access-<username>-t` token drives the default MSW `/auth/me`
 * handler to the seeded profile for that username.
 */
function authedSession(
  username: string,
  {
    user = null,
    profileStatus = "pending" as "idle" | "pending" | "resolved" | "error",
  } = {},
) {
  sessionStore.setState({
    status: "authenticated",
    accessToken: `access-${username}-t`,
    accessTokenExpiresAt: FUTURE(),
    refreshToken: `refresh-${username}-t`,
    refreshTokenExpiresAt: FUTURE(),
    user,
    profileStatus,
  });
}

/** Minimal hook consumer for store/network-boundary assertions. */
function HookProbe() {
  useCurrentUserQuery();
  return null;
}

function renderAdminArea(initialPath = "/admin") {
  return renderWithProviders(
    <Routes>
      <Route element={<ProtectedRoute />}>
        <Route element={<AdminRoute />}>
          <Route path="/admin" element={<div>Admin Panel</div>} />
        </Route>
      </Route>
      <Route path="/login" element={<div>Login Screen</div>} />
    </Routes>,
    { initialPath },
  );
}

function renderShell(initialPath = "/dashboard", opts = {}) {
  return renderWithProviders(
    <Routes>
      <Route element={<ProtectedRoute />}>
        <Route element={<AppShellLayout />}>
          <Route path="/dashboard" element={<div>Dashboard Screen</div>} />
        </Route>
      </Route>
      <Route path="/login" element={<div>Login Screen</div>} />
    </Routes>,
    { initialPath, ...opts },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  sessionStore.setState({
    status: "idle",
    accessToken: null,
    accessTokenExpiresAt: null,
    refreshToken: null,
    refreshTokenExpiresAt: null,
    user: null,
    profileStatus: "idle",
  });
  registerSessionExpiredHandler(null);
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(() => {
  registerSessionExpiredHandler(null);
});

// ─── authApi.me ─────────────────────────────────────────────────────────────
describe("authApi.me", () => {
  it("AuthApiMe_SuccessEnvelope_UnwrapsUserResponseIncludingRole", async () => {
    authedSession("admin");
    const profile = await authApi.me();
    expect(profile).toEqual(ADMIN_PROFILE);
    expect(profile.role).toBe("ADMIN");
  });
});

// ─── useCurrentUserQuery (hook → store sync) ─────────────────────────────────
describe("useCurrentUserQuery", () => {
  it("UseCurrentUserQuery_IdleThenAuthenticated_DisabledUntilAuthenticatedThenFetches", async () => {
    let count = 0;
    server.use(
      http.get("*/api/v1/auth/me", () => {
        count += 1;
        return ok(DEMO_PROFILE);
      }),
    );

    // Boot `idle` → the query is disabled (same condition covers unauthenticated).
    renderWithProviders(<HookProbe />);
    expect(count).toBe(0);

    // Flip to authenticated → the one query fires and syncs the store.
    act(() => authedSession("demo"));
    await waitFor(() => expect(count).toBe(1));
    await waitFor(() =>
      expect(getSession().profileStatus).toBe("resolved"),
    );
  });

  it("UseCurrentUserQuery_Success_SyncsUuidTierRoleIntoStoreAndResolves", async () => {
    authedSession("demo");
    renderWithProviders(<HookProbe />);

    await waitFor(() =>
      expect(getSession().profileStatus).toBe("resolved"),
    );
    expect(getSession().user).toEqual(DEMO_PROFILE);
  });

  it("UseCurrentUserQuery_TwoConsumers_ShareASingleFetch", async () => {
    let count = 0;
    server.use(
      http.get("*/api/v1/auth/me", () => {
        count += 1;
        return ok(DEMO_PROFILE);
      }),
    );
    authedSession("demo");

    renderWithProviders(
      <>
        <HookProbe />
        <HookProbe />
      </>,
    );

    await waitFor(() =>
      expect(getSession().profileStatus).toBe("resolved"),
    );
    // De-duped behind one query key — two consumers, one round-trip.
    expect(count).toBe(1);
  });
});

// ─── Population after login + boot-rehydrate ─────────────────────────────────
describe("profile population", () => {
  it("LoginFlow_SuccessfulLogin_FetchesProfileAndPopulatesRoleAndLabel", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<ProtectedRoute />}>
          <Route element={<AppShellLayout />}>
            <Route path="/dashboard" element={<div>Dashboard Screen</div>} />
          </Route>
        </Route>
      </Routes>,
      { initialPath: "/login" },
    );

    await user.type(screen.getByLabelText(/Tên đăng nhập/), "admin");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123");
    await user.click(screen.getByRole("button", { name: "Đăng nhập" }));

    // Landed in the shell; `/auth/me` reconciled the full profile into the store.
    expect(await screen.findByText("Dashboard Screen")).toBeInTheDocument();
    expect(
      await screen.findByRole("button", { name: "admin" }),
    ).toBeInTheDocument();
    await waitFor(() => expect(getSession().user?.role).toBe("ADMIN"));
    expect(getSession().user?.uuid).toBe("uuid-admin");
    expect(getSession().profileStatus).toBe("resolved");
  });

  it("BootRehydrate_TokensOnlyThenMe_RestoresRealUsernameLabel", async () => {
    // Simulate a reload: boot-refresh returns tokens only (no user) — the store
    // is authenticated with `user = null`, so the shell first shows the neutral
    // account fallback (foundation nit #3), then `/auth/me` restores the name.
    authedSession("demo", { user: null });
    renderShell();

    // Before `/auth/me` resolves: generic fallback label, not the username.
    expect(
      screen.getByRole("button", { name: "Tài khoản" }),
    ).toBeInTheDocument();

    // After `/auth/me`: the real username replaces the fallback.
    expect(
      await screen.findByRole("button", { name: "demo" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Tài khoản" }),
    ).not.toBeInTheDocument();
    expect(getSession().user?.username).toBe("demo");
  });
});

// ─── AdminRoute timing (OQ5a) ────────────────────────────────────────────────
describe("AdminRoute activation + timing", () => {
  it("AdminRoute_AdminDeepLinkWhilePending_ShowsSplashThenAdmitsWithoutForbiddenFlash", async () => {
    server.use(
      http.get("*/api/v1/auth/me", async () => {
        await delay(40);
        return ok(ADMIN_PROFILE);
      }),
    );
    authedSession("admin");
    renderAdminArea("/admin");

    // While the profile is pending: boot splash, never a Forbidden flash.
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(
      screen.queryByText("Không có quyền truy cập"),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Admin Panel")).not.toBeInTheDocument();

    // Settles to admit ADMIN.
    expect(await screen.findByText("Admin Panel")).toBeInTheDocument();
    expect(
      screen.queryByText("Không có quyền truy cập"),
    ).not.toBeInTheDocument();
  });

  it("AdminRoute_UserProfileResolves_DeniesWithForbidden", async () => {
    server.use(
      http.get("*/api/v1/auth/me", async () => {
        await delay(40);
        return ok(DEMO_PROFILE);
      }),
    );
    authedSession("demo");
    renderAdminArea("/admin");

    // Splash while pending, then a deny once the USER profile settles.
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(
      await screen.findByText("Không có quyền truy cập"),
    ).toBeInTheDocument();
    expect(screen.queryByText("Admin Panel")).not.toBeInTheDocument();
  });

  it("AdminRoute_ProfileFetchFails_SettlesToDenyNotInfiniteSplash", async () => {
    // `degraded` has no seeded profile → default handler 500s (non-401).
    authedSession("degraded");
    renderAdminArea("/admin");

    expect(
      await screen.findByText("Không có quyền truy cập"),
    ).toBeInTheDocument();
    // Settled into a fail-safe deny, not stuck on the splash forever.
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(getSession().profileStatus).toBe("error");
  });
});

// ─── Degraded non-401 failure (OQ3a) ─────────────────────────────────────────
describe("degraded profile (non-401)", () => {
  it("Degraded_Non401MeFailure_StaysAuthenticatedUserNullNeutralLabelSurfacesRender", async () => {
    authedSession("degraded");
    renderShell();

    // Non-admin surface renders immediately (paint is not gated on `/auth/me`).
    expect(screen.getByText("Dashboard Screen")).toBeInTheDocument();

    await waitFor(() => expect(getSession().profileStatus).toBe("error"));

    // Session is NOT cleared; stays authenticated with valid tokens.
    expect(getSession().status).toBe("authenticated");
    expect(getSession().accessToken).toBe("access-degraded-t");
    expect(getSession().user).toBeNull();
    // Account label falls back to the neutral generic label.
    expect(
      screen.getByRole("button", { name: "Tài khoản" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Dashboard Screen")).toBeInTheDocument();
  });

  it("Degraded_NeutralAccountLabel_IsI18nDrivenNotHardcoded", async () => {
    // en-US: the fallback label is the localized `common:account`, proving it is
    // not a hardcoded string.
    window.localStorage.setItem("fsm.locale", "en-US");
    authedSession("degraded");
    renderShell();

    expect(
      await screen.findByRole("button", { name: "Account" }),
    ).toBeInTheDocument();
    await waitFor(() => expect(getSession().profileStatus).toBe("error"));
    expect(getSession().status).toBe("authenticated");
  });

  it("Degraded_NetworkMeFailure_RetriesOnceThenSettlesError", async () => {
    let count = 0;
    server.use(
      http.get("*/api/v1/auth/me", () => {
        count += 1;
        return HttpResponse.error();
      }),
    );
    authedSession("demo");
    renderWithProviders(<HookProbe />);

    await waitFor(
      () => expect(getSession().profileStatus).toBe("error"),
      { timeout: 4000 },
    );
    // Genuine network errors auto-retry exactly once (1 + 1 retry = 2 attempts).
    expect(count).toBe(2);
    expect(getSession().status).toBe("authenticated");
  });
});

// ─── 401 path (rides the client refresh flow, not the degraded path) ─────────
describe("401 profile fetch", () => {
  it("Me401ThenValidRefresh_RetriesAndResolvesNotTreatedAsDegraded", async () => {
    sessionStore.setState({
      status: "authenticated",
      accessToken: "stale",
      accessTokenExpiresAt: FUTURE(),
      refreshToken: "valid-refresh",
      refreshTokenExpiresAt: FUTURE(),
      user: null,
      profileStatus: "pending",
    });
    server.use(
      http.post("*/api/v1/auth/refresh", () =>
        ok({
          accessToken: "fresh",
          accessTokenExpiresAt: FUTURE(),
          refreshToken: "rotated",
          refreshTokenExpiresAt: FUTURE(),
        }),
      ),
      http.get("*/api/v1/auth/me", ({ request }) => {
        const auth = request.headers.get("Authorization");
        if (auth === "Bearer fresh") return ok(DEMO_PROFILE);
        return fail(1002, "Phiên đăng nhập không hợp lệ.", 401);
      }),
    );

    renderWithProviders(<HookProbe />);

    await waitFor(() =>
      expect(getSession().profileStatus).toBe("resolved"),
    );
    // The 401 rode the client's refresh→retry: token rotated, profile resolved —
    // NOT the degraded (error) path.
    expect(getSession().accessToken).toBe("fresh");
    expect(getSession().user).toEqual(DEMO_PROFILE);
    expect(getSession().profileStatus).not.toBe("error");
  });

  it("Me401ThenFailedRefresh_ClearsSessionAndRedirects", async () => {
    sessionStore.setState({
      status: "authenticated",
      accessToken: "stale",
      accessTokenExpiresAt: FUTURE(),
      refreshToken: "revoked-refresh",
      refreshTokenExpiresAt: FUTURE(),
      user: null,
      profileStatus: "pending",
    });
    server.use(
      http.post("*/api/v1/auth/refresh", () =>
        fail(2002, "Mã gia hạn phiên không hợp lệ.", 401),
      ),
      http.get("*/api/v1/auth/me", () =>
        fail(1002, "Phiên đăng nhập không hợp lệ.", 401),
      ),
    );

    renderShell();

    // Terminal: hard-cleared + redirected to login (existing refresh flow) —
    // NOT left authenticated-degraded.
    expect(await screen.findByText("Login Screen")).toBeInTheDocument();
    expect(getSession().status).toBe("unauthenticated");
    expect(getSession().accessToken).toBeNull();
    expect(getSession().refreshToken).toBeNull();
  });
});

// ─── Freshness / invalidation (OQ4a) ─────────────────────────────────────────
describe("freshness + invalidation", () => {
  it("Freshness_Remount_DoesNotRefetch", async () => {
    let count = 0;
    server.use(
      http.get("*/api/v1/auth/me", () => {
        count += 1;
        return ok(DEMO_PROFILE);
      }),
    );
    authedSession("demo");

    const { unmount } = renderWithProviders(<HookProbe />, { queryClient });
    await waitFor(() => expect(count).toBe(1));
    unmount();

    // Remount against the SAME client: staleTime Infinity → served from cache.
    renderWithProviders(<HookProbe />, { queryClient });
    await waitFor(() =>
      expect(getSession().profileStatus).toBe("resolved"),
    );
    expect(count).toBe(1);
  });

  it("Invalidation_InvalidateCurrentUser_TriggersRefetch", async () => {
    let count = 0;
    server.use(
      http.get("*/api/v1/auth/me", () => {
        count += 1;
        return ok(DEMO_PROFILE);
      }),
    );
    authedSession("demo");

    renderWithProviders(<HookProbe />, { queryClient });
    await waitFor(() => expect(count).toBe(1));

    // The manual invalidation seam forces a fresh read.
    await act(async () => {
      await invalidateCurrentUser();
    });
    await waitFor(() => expect(count).toBe(2));
  });

  it("Logout_ClearsQueryCacheAndSession", async () => {
    const user = userEvent.setup();
    authedSession("demo");

    renderShell("/dashboard", { queryClient });
    // Profile cached after the first fetch.
    expect(
      await screen.findByRole("button", { name: "demo" }),
    ).toBeInTheDocument();
    expect(queryClient.getQueryData(currentUserQueryKey)).toBeDefined();

    await user.click(screen.getByRole("button", { name: "Đăng xuất" }));

    // Logout drops the cached current-user and clears the session.
    expect(await screen.findByText("Login Screen")).toBeInTheDocument();
    expect(getSession().status).toBe("unauthenticated");
    expect(queryClient.getQueryData(currentUserQueryKey)).toBeUndefined();
  });
});
