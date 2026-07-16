import { beforeEach, describe, expect, it } from "vitest";
import { renderHook } from "@testing-library/react";
import { sessionStore } from "@/lib/auth/session";
import type { ProfileStatus, SessionUser } from "@/lib/auth/session";
import { NAV_ENTRIES, useNavEntries } from "./navConfig";

/**
 * Navigation-registration pattern (M1). `useNavEntries` is the single role filter
 * over `NAV_ENTRIES`: it hides `requiresAdmin` entries unless the profile has
 * RESOLVED to `role === "ADMIN"` — mirroring `AdminRoute`'s fail-safe (pending /
 * error / unknown role → admin hidden, never fabricate a role). Reads only the
 * Zustand session store, so these are pure store-driven hook tests (no network).
 */

function setSession(
  user: SessionUser | null,
  profileStatus: ProfileStatus = "resolved",
) {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "acc",
    accessTokenExpiresAt: future,
    refreshToken: "ref",
    refreshTokenExpiresAt: future,
    user,
    profileStatus,
  });
}

const ADMIN_ENTRY = NAV_ENTRIES.find((e) => e.to === "/admin");
const NON_ADMIN_COUNT = NAV_ENTRIES.filter((e) => !e.requiresAdmin).length;

beforeEach(() => {
  sessionStore.setState({
    status: "idle",
    accessToken: null,
    accessTokenExpiresAt: null,
    refreshToken: null,
    refreshTokenExpiresAt: null,
    user: null,
    profileStatus: "idle",
  });
});

describe("navConfig", () => {
  it("NavEntries_Registry_CoversEveryRoadmapAreaPlusGatedAdmin", () => {
    // The registry is the single source of truth: one entry per roadmap area,
    // admin carrying `requiresAdmin`. Guards the "one-line to register" contract.
    const paths = NAV_ENTRIES.map((e) => e.to);
    expect(paths).toEqual([
      "/dashboard",
      "/members",
      "/categories",
      "/tags",
      "/expenses",
      "/events",
      "/stats",
      "/wallet",
      "/admin",
    ]);
    expect(ADMIN_ENTRY?.requiresAdmin).toBe(true);
    // Only the admin entry is gated.
    expect(NAV_ENTRIES.filter((e) => e.requiresAdmin)).toHaveLength(1);
  });
});

describe("useNavEntries", () => {
  it("UseNavEntries_UserResolved_ReturnsEveryAreaWithAdminHidden", () => {
    setSession({ username: "demo", role: "USER" }, "resolved");
    const { result } = renderHook(() => useNavEntries());

    expect(result.current).toHaveLength(NON_ADMIN_COUNT);
    expect(result.current.some((e) => e.to === "/admin")).toBe(false);
    // Every non-admin roadmap area is present.
    expect(result.current.map((e) => e.to)).toEqual([
      "/dashboard",
      "/members",
      "/categories",
      "/tags",
      "/expenses",
      "/events",
      "/stats",
      "/wallet",
    ]);
  });

  it("UseNavEntries_AdminResolved_IncludesAdminEntry", () => {
    setSession({ username: "root", role: "ADMIN" }, "resolved");
    const { result } = renderHook(() => useNavEntries());

    expect(result.current).toHaveLength(NAV_ENTRIES.length);
    expect(result.current.some((e) => e.to === "/admin")).toBe(true);
  });

  it("UseNavEntries_AdminRolePending_HidesAdminFailSafe", () => {
    // Fail-safe: even an ADMIN role is hidden until the profile RESOLVES.
    setSession({ username: "root", role: "ADMIN" }, "pending");
    const { result } = renderHook(() => useNavEntries());

    expect(result.current.some((e) => e.to === "/admin")).toBe(false);
    expect(result.current).toHaveLength(NON_ADMIN_COUNT);
  });

  it("UseNavEntries_ProfileError_HidesAdminFailSafe", () => {
    // Degraded (non-401) profile fetch: no user, error state → admin hidden.
    setSession(null, "error");
    const { result } = renderHook(() => useNavEntries());

    expect(result.current.some((e) => e.to === "/admin")).toBe(false);
    expect(result.current).toHaveLength(NON_ADMIN_COUNT);
  });

  it("UseNavEntries_UnknownRoleResolved_HidesAdminFailSafe", () => {
    // Resolved but with no/unknown role → never treated as ADMIN.
    setSession({ username: "demo" }, "resolved");
    const { result } = renderHook(() => useNavEntries());

    expect(result.current.some((e) => e.to === "/admin")).toBe(false);
    expect(result.current).toHaveLength(NON_ADMIN_COUNT);
  });
});
