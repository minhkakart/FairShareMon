import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test/utils";
import { DashboardPage } from "./pages/DashboardPage";
import { sessionStore } from "@/lib/auth/session";
import type { ProfileStatus, SessionUser } from "@/lib/auth/session";
import i18n from "@/i18n";
import { setActiveLocale } from "@/lib/api/runtime";

/**
 * Minimal home (M1). A welcome greeting + role-filtered quick-link cards from
 * `useNavEntries` (the dashboard tile excluded, admin tile admin-only). No charts
 * and no data fetching beyond the session `user` — `onUnhandledRequest: "error"`
 * in setup.ts means any stray request would fail the test.
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

beforeEach(async () => {
  window.localStorage.clear();
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
});

afterEach(async () => {
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
  window.localStorage.clear();
});

describe("DashboardPage home", () => {
  it("Dashboard_UserResolved_ShowsWelcomeGreetingWithUsername", () => {
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    expect(
      screen.getByRole("heading", { level: 1, name: "Chào demo" }),
    ).toBeInTheDocument();
  });

  it("Dashboard_NoUsername_ShowsGenericGreeting", () => {
    setSession(null, "pending");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    expect(
      screen.getByRole("heading", { level: 1, name: "Chào mừng bạn" }),
    ).toBeInTheDocument();
  });

  it("Dashboard_UserResolved_RendersQuickLinkPerVisibleAreaExcludingDashboardAndAdmin", () => {
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // One card (h2) per visible non-dashboard area.
    for (const label of [
      "Thành viên",
      "Danh mục",
      "Nhãn",
      "Chi tiêu",
      "Đợt",
      "Thống kê",
      "Ví",
    ]) {
      expect(
        screen.getByRole("heading", { level: 2, name: label }),
      ).toBeInTheDocument();
    }
    // The dashboard tile itself is excluded (you're already home).
    expect(
      screen.queryByRole("heading", { level: 2, name: "Tổng quan" }),
    ).not.toBeInTheDocument();
    // Admin tile is hidden for a USER.
    expect(
      screen.queryByRole("heading", { level: 2, name: "Quản trị" }),
    ).not.toBeInTheDocument();

    // Each card links to its area (accessible name = area label).
    expect(screen.getByRole("link", { name: "Thành viên" })).toHaveAttribute(
      "href",
      "/members",
    );
    // No charts in the M1 home.
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("Dashboard_AdminResolved_ShowsAdminQuickLinkCard", () => {
    setSession({ username: "root", role: "ADMIN" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    expect(
      screen.getByRole("heading", { level: 2, name: "Quản trị" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Quản trị" })).toHaveAttribute(
      "href",
      "/admin",
    );
  });

  it("Dashboard_EnUsLocale_RendersEnglishGreetingAndCards", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // LocaleProvider syncs i18n on mount; en-US copy resolves.
    expect(
      await screen.findByRole("heading", { level: 1, name: "Welcome, demo" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Members" }),
    ).toBeInTheDocument();
  });
});
