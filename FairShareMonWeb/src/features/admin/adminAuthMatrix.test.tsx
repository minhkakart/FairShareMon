import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Route, Routes } from "react-router-dom";
import { renderWithProviders } from "@/test/utils";
import { ProtectedRoute } from "@/routes/ProtectedRoute";
import { AdminRoute } from "@/routes/AdminRoute";
import { sessionStore } from "@/lib/auth/session";
import type { SessionUser } from "@/lib/auth/session";

/**
 * Admin auth matrix (headline) — the full `/admin` gate as wired in the real
 * router: `ProtectedRoute` (session) wrapping `AdminRoute` (role from `/auth/me`).
 * Proves: anonymous → login redirect (no admin content); a non-admin USER — INCL.
 * a Premium USER — → Forbidden; an ADMIN → the area renders; and the gate fails
 * SAFE (deny, never leak) while the profile is still pending and on a settled
 * error / unknown-role state. Session is driven directly (the `/auth/me` sync is
 * covered elsewhere); deterministic, no network.
 */

function setSession(
  status: "idle" | "authenticated" | "unauthenticated",
  user: SessionUser | null,
  profileStatus: "idle" | "pending" | "resolved" | "error",
) {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status,
    accessToken: status === "authenticated" ? "acc" : null,
    accessTokenExpiresAt: status === "authenticated" ? future : null,
    refreshToken: null,
    refreshTokenExpiresAt: null,
    user,
    profileStatus,
  });
}

function renderAdminGate() {
  return renderWithProviders(
    <Routes>
      <Route element={<ProtectedRoute />}>
        <Route element={<AdminRoute />}>
          <Route path="/admin" element={<div>Admin Console</div>} />
        </Route>
      </Route>
      <Route path="/login" element={<div>Login Screen</div>} />
    </Routes>,
    { initialPath: "/admin" },
  );
}

beforeEach(() => {
  window.localStorage.clear();
});
afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("Admin auth matrix", () => {
  it("AdminGate_Anonymous_RedirectsToLoginWithoutAdminContent", () => {
    setSession("unauthenticated", null, "idle");
    renderAdminGate();
    expect(screen.getByText("Login Screen")).toBeInTheDocument();
    expect(screen.queryByText("Admin Console")).not.toBeInTheDocument();
  });

  it("AdminGate_NonAdminUser_ShowsForbidden", () => {
    setSession("authenticated", { username: "demo", role: "USER" }, "resolved");
    renderAdminGate();
    expect(screen.getByText("Không có quyền truy cập")).toBeInTheDocument();
    expect(screen.queryByText("Admin Console")).not.toBeInTheDocument();
  });

  it("AdminGate_PremiumUserButNotAdmin_ShowsForbidden", () => {
    // Premium tier does not confer admin — role is the only gate.
    setSession(
      "authenticated",
      { username: "vip", role: "USER", tier: "PREMIUM" },
      "resolved",
    );
    renderAdminGate();
    expect(screen.getByText("Không có quyền truy cập")).toBeInTheDocument();
    expect(screen.queryByText("Admin Console")).not.toBeInTheDocument();
  });

  it("AdminGate_Admin_RendersTheArea", () => {
    setSession(
      "authenticated",
      { username: "root", role: "ADMIN", uuid: "uuid-admin" },
      "resolved",
    );
    renderAdminGate();
    expect(screen.getByText("Admin Console")).toBeInTheDocument();
  });

  it("AdminGate_ProfilePending_HoldsOnSplashNotForbiddenNorContent", () => {
    // Fail-safe: while the profile resolves the guard shows the boot splash — it
    // never flashes Forbidden at an admin, and never leaks the area.
    setSession("authenticated", { username: "root" }, "pending");
    renderAdminGate();
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.queryByText("Admin Console")).not.toBeInTheDocument();
    expect(screen.queryByText("Không có quyền truy cập")).not.toBeInTheDocument();
  });

  it("AdminGate_ProfileErrorNoRole_FailsSafeToForbidden", () => {
    setSession("authenticated", null, "error");
    renderAdminGate();
    expect(screen.getByText("Không có quyền truy cập")).toBeInTheDocument();
    expect(screen.queryByText("Admin Console")).not.toBeInTheDocument();
  });

  it("AdminGate_UnknownRoleResolved_NeverAdmitted", () => {
    setSession(
      "authenticated",
      { username: "weird", role: "SUPERUSER" },
      "resolved",
    );
    renderAdminGate();
    expect(screen.getByText("Không có quyền truy cập")).toBeInTheDocument();
  });
});
