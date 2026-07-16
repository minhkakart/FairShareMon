import { beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Route, Routes } from "react-router-dom";
import { renderWithProviders } from "@/test/utils";
import { ProtectedRoute } from "./ProtectedRoute";
import { PublicOnlyRoute } from "./PublicOnlyRoute";
import { AdminRoute } from "./AdminRoute";
import { NotFound } from "./NotFound";
import { getSession, sessionStore } from "@/lib/auth/session";
import type { SessionUser } from "@/lib/auth/session";

function setStatus(
  status: "idle" | "authenticated" | "unauthenticated",
  user: SessionUser | null = null,
  profileStatus: "idle" | "pending" | "resolved" | "error" = "idle",
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

beforeEach(() => {
  window.localStorage.clear();
  setStatus("idle");
});

describe("ProtectedRoute", () => {
  function renderProtected(initialPath: string) {
    return renderWithProviders(
      <Routes>
        <Route element={<ProtectedRoute />}>
          <Route path="/app" element={<div>Protected Child</div>} />
        </Route>
        <Route path="/login" element={<div>Login Screen</div>} />
      </Routes>,
      { initialPath },
    );
  }

  it("ProtectedRoute_SessionIdle_ShowsBootSplash", () => {
    setStatus("idle");
    renderProtected("/app");
    // Boot splash shows a live status spinner (label appears in the SR text + a
    // visible paragraph) and holds back protected content.
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(
      screen.getAllByText("Đang khôi phục phiên đăng nhập…").length,
    ).toBeGreaterThan(0);
    expect(screen.queryByText("Protected Child")).not.toBeInTheDocument();
  });

  it("ProtectedRoute_Unauthenticated_RedirectsToLogin", () => {
    setStatus("unauthenticated");
    renderProtected("/app");
    expect(screen.getByText("Login Screen")).toBeInTheDocument();
    expect(screen.queryByText("Protected Child")).not.toBeInTheDocument();
  });

  it("ProtectedRoute_Authenticated_RendersChild", () => {
    setStatus("authenticated", { username: "demo" });
    renderProtected("/app");
    expect(screen.getByText("Protected Child")).toBeInTheDocument();
  });
});

describe("PublicOnlyRoute", () => {
  function renderPublic(initialPath: string) {
    return renderWithProviders(
      <Routes>
        <Route element={<PublicOnlyRoute />}>
          <Route path="/login" element={<div>Login Screen</div>} />
        </Route>
        <Route path="/dashboard" element={<div>Dashboard Screen</div>} />
      </Routes>,
      { initialPath },
    );
  }

  it("PublicOnlyRoute_Authenticated_BouncesToDashboard", () => {
    setStatus("authenticated", { username: "demo" });
    renderPublic("/login");
    expect(screen.getByText("Dashboard Screen")).toBeInTheDocument();
    expect(screen.queryByText("Login Screen")).not.toBeInTheDocument();
  });

  it("PublicOnlyRoute_Unauthenticated_RendersPublicChild", () => {
    setStatus("unauthenticated");
    renderPublic("/login");
    expect(screen.getByText("Login Screen")).toBeInTheDocument();
  });
});

describe("AdminRoute", () => {
  function renderAdmin() {
    return renderWithProviders(
      <Routes>
        <Route element={<AdminRoute />}>
          <Route path="/admin" element={<div>Admin Panel</div>} />
        </Route>
      </Routes>,
      { initialPath: "/admin" },
    );
  }

  it("AdminRoute_NoRoleResolved_DeniesWithForbidden", () => {
    // Profile settled with no role (fail-safe) → denied, never ADMIN.
    setStatus("authenticated", { username: "demo" }, "resolved");
    renderAdmin();
    expect(screen.getByText("Không có quyền truy cập")).toBeInTheDocument();
    expect(screen.queryByText("Admin Panel")).not.toBeInTheDocument();
  });

  it("AdminRoute_NonAdminRoleResolved_DeniesWithForbidden", () => {
    setStatus("authenticated", { username: "demo", role: "USER" }, "resolved");
    renderAdmin();
    expect(screen.getByText("Không có quyền truy cập")).toBeInTheDocument();
    expect(screen.queryByText("Admin Panel")).not.toBeInTheDocument();
  });

  it("AdminRoute_AdminRoleResolved_AdmitsOutlet", () => {
    setStatus("authenticated", { username: "root", role: "ADMIN" }, "resolved");
    renderAdmin();
    expect(screen.getByText("Admin Panel")).toBeInTheDocument();
    expect(getSession().user?.role).toBe("ADMIN");
  });
});

describe("NotFound", () => {
  it("NotFound_Rendered_ShowsTitleBodyAndBackLink", () => {
    renderWithProviders(<NotFound />, { initialPath: "/nope" });
    expect(
      screen.getByRole("heading", { name: "Không tìm thấy" }),
    ).toBeInTheDocument();
    // Ownership-404 copy: never confirms existence.
    expect(
      screen.getByText(/không tồn tại hoặc bạn không có quyền xem/),
    ).toBeInTheDocument();
    // The back-home affordance is a router Link rendered via `Button asChild`,
    // so it is a single <a> (role="link"), not a nested button.
    expect(
      screen.getByRole("link", { name: "Về trang chính" }),
    ).toBeInTheDocument();
  });
});
