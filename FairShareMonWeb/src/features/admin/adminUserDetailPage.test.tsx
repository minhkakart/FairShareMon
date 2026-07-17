import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Route, Routes } from "react-router-dom";
import { resetAdminStore } from "@/test/msw/handlers";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatMoneyVnd } from "@/i18n/format";
import i18n from "@/i18n";
import { AdminUserDetailPage } from "./pages/AdminUserDetailPage";

/**
 * AdminUserDetailPage integration — metadata + grant history + the action bar,
 * against the committed admin fixtures. Proves: account metadata + tier-grant
 * history render (Money verbatim, references); a `14000` miss → the admin-LOCAL
 * not-found state (not the ledger existence-hiding NotFound); the action bar is
 * present. Metadata + grants only (R10, covered exhaustively in privacy.test).
 */

const vnd = (n: number) => formatMoneyVnd(n).replace(/\s+/g, " ");

function seedAdmin() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-admin-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-admin-t",
    refreshTokenExpiresAt: future,
    user: { username: "admin", role: "ADMIN", uuid: "uuid-admin", tier: "PREMIUM" },
    profileStatus: "resolved",
  });
}

function renderDetail(uuid: string) {
  return renderWithProviders(
    <Routes>
      <Route path="/admin/users/:uuid" element={<AdminUserDetailPage />} />
    </Routes>,
    { initialPath: `/admin/users/${uuid}` },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  resetAdminStore();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedAdmin();
});
afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("AdminUserDetailPage", () => {
  it("AdminUserDetailPage_ExistingUser_RendersMetadataAndGrantHistory", async () => {
    renderDetail("uuid-nguyen-a");

    // Metadata card.
    expect(await screen.findByText("Thông tin tài khoản")).toBeInTheDocument();
    expect(screen.getByText("nguyen.van.a")).toBeInTheDocument();
    expect(screen.getByText("uuid-nguyen-a")).toBeInTheDocument();

    // Grant history table (Money verbatim + references).
    expect(
      screen.getByRole("table", { name: /Lịch sử cấp\/thu hồi Premium của nguyen\.van\.a/ }),
    ).toBeInTheDocument();
    expect(screen.getAllByText(vnd(200000)).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("VCB-20260716-8842")).toBeInTheDocument();

    // The action bar is present.
    expect(screen.getByText("Thao tác quản trị")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Cấp Premium" }),
    ).toBeInTheDocument();
  });

  it("AdminUserDetailPage_UnknownUser14000_ShowsAdminLocalNotFound", async () => {
    renderDetail("uuid-does-not-exist");
    // Admin-local not-found (the admin scope may confirm a user exists) — NOT the
    // global ledger existence-hiding NotFound copy.
    expect(
      await screen.findByText("Không tìm thấy người dùng"),
    ).toBeInTheDocument();
    // Both the top back link and the empty-state action link route back to the list.
    expect(
      screen.getAllByRole("link", { name: "Về danh sách" }).length,
    ).toBeGreaterThanOrEqual(1);
  });

  it("AdminUserDetailPage_GrantHistoryEmpty_ShowsEmptyState", async () => {
    // le.thi.b is a FREE user with no grants.
    renderDetail("uuid-le-b");
    expect(
      await screen.findByText("Chưa có lượt cấp/thu hồi nào."),
    ).toBeInTheDocument();
  });
});
