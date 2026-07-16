import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { renderWithProviders } from "@/test/utils";
import { SettingsPage } from "./pages/SettingsPage";
import { ProfileCard } from "./components/ProfileCard";
import { TierStatusPanel } from "./components/TierStatusPanel";
import { getSession, sessionStore } from "@/lib/auth/session";
import type { ProfileStatus, SessionUser } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { formatDate } from "@/i18n/format";
import i18n from "@/i18n";
import { setActiveLocale } from "@/lib/api/runtime";

/**
 * Settings surface (M1). All components read only the already-loaded session
 * `user` — no network — so these are component/interaction tests against the
 * session + i18n + theme providers. TZ pinned (Asia/Ho_Chi_Minh, setup.ts),
 * locale pinned per-test.
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

const FREE_USER: SessionUser = {
  username: "demo",
  uuid: "uuid-demo",
  tier: "FREE",
  role: "USER",
  createdAt: "2026-01-01T00:00:00+00:00",
};
const PREMIUM_USER: SessionUser = {
  username: "rich",
  uuid: "uuid-rich",
  tier: "PREMIUM",
  role: "USER",
  createdAt: "2026-01-01T00:00:00+00:00",
};

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  document.documentElement.removeAttribute("data-theme");
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
  setSession(FREE_USER, "resolved");
});

afterEach(async () => {
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
  window.localStorage.clear();
  document.documentElement.removeAttribute("data-theme");
});

describe("SettingsPage profile", () => {
  it("SettingsPage_FreeUserResolved_RendersUsernameRoleMemberSinceAndFreeBadge", () => {
    renderWithProviders(<SettingsPage />, { initialPath: "/settings" });

    expect(
      screen.getByRole("heading", { level: 1, name: "Cài đặt" }),
    ).toBeInTheDocument();
    expect(screen.getByText("demo")).toBeInTheDocument();
    // Role badge (User) + Free tier badge.
    expect(screen.getByText("Người dùng")).toBeInTheDocument();
    expect(screen.getByText("Miễn phí")).toBeInTheDocument();
    // Member-since via formatDate under the pinned timezone.
    expect(
      screen.getByText(formatDate("2026-01-01T00:00:00+00:00")),
    ).toBeInTheDocument();
  });

  it("SettingsPage_AdminUser_RendersAdministratorRoleLabel", () => {
    setSession(
      { ...FREE_USER, username: "root", role: "ADMIN" },
      "resolved",
    );
    renderWithProviders(<SettingsPage />, { initialPath: "/settings" });
    expect(screen.getByText("Quản trị viên")).toBeInTheDocument();
  });

  it("ProfileCard_MemberSince_RendersInActiveTimezoneCrossingYearBoundary", () => {
    // 2025-12-31 20:00 UTC is 2026-01-01 03:00 in Asia/Ho_Chi_Minh (+07): the
    // displayed date must reflect the viewer timezone, not raw UTC.
    setSession({ ...FREE_USER, createdAt: "2025-12-31T20:00:00+00:00" });
    renderWithProviders(<ProfileCard />, { initialPath: "/settings" });

    const shown = formatDate("2025-12-31T20:00:00+00:00");
    expect(shown).toContain("2026");
    expect(shown).not.toContain("2025");
    expect(screen.getByText(shown)).toBeInTheDocument();
  });

  it("ProfileCard_ProfilePending_ShowsSkeletonsForUnknownFields", () => {
    // Boot-rehydrate: authenticated, profile still pending (no user yet).
    setSession(null, "pending");
    const { container } = renderWithProviders(<ProfileCard />, {
      initialPath: "/settings",
    });
    // Skeletons are aria-hidden inline-width spans; the real values are absent.
    expect(
      container.querySelectorAll('span[aria-hidden="true"][style*="width"]')
        .length,
    ).toBeGreaterThan(0);
    expect(screen.queryByText("demo")).not.toBeInTheDocument();
  });

  it("ProfileCard_ProfileError_ShowsDegradedNotice", () => {
    setSession(null, "error");
    renderWithProviders(<ProfileCard />, { initialPath: "/settings" });
    expect(
      screen.getByText(/Không tải được hồ sơ/),
    ).toBeInTheDocument();
  });
});

describe("SettingsPage tier status", () => {
  it("TierStatusPanel_FreeUser_ShowsInformationalUpgradePromptWithNoAction", () => {
    setSession(FREE_USER, "resolved");
    renderWithProviders(<TierStatusPanel />, { initialPath: "/settings" });

    // Informational upgrade explanation + perks line.
    expect(screen.getByText("Nâng lên Premium")).toBeInTheDocument();
    expect(screen.getByText(/Premium bổ sung/)).toBeInTheDocument();
    // No navigating action — it explains that Premium is a manual grant.
    const panel = screen.getByRole("status");
    expect(within(panel).queryByRole("button")).not.toBeInTheDocument();
    expect(within(panel).queryByRole("link")).not.toBeInTheDocument();
    // The Premium confirmation copy is absent for a Free user.
    expect(
      screen.queryByText("Bạn đang dùng Premium"),
    ).not.toBeInTheDocument();
  });

  it("TierStatusPanel_PremiumUser_ShowsActiveConfirmationAndNoUpgradePrompt", () => {
    setSession(PREMIUM_USER, "resolved");
    renderWithProviders(<TierStatusPanel />, { initialPath: "/settings" });

    expect(screen.getByText("Bạn đang dùng Premium")).toBeInTheDocument();
    expect(screen.queryByText("Nâng lên Premium")).not.toBeInTheDocument();
  });

  it("TierBadge_LowercaseTier_NormalizesToPremium", () => {
    // Case-insensitive normalization: casing drift never mislabels a user.
    setSession({ ...PREMIUM_USER, tier: "premium" }, "resolved");
    renderWithProviders(<ProfileCard />, { initialPath: "/settings" });
    expect(screen.getByText("Premium")).toBeInTheDocument();
    expect(screen.queryByText("Miễn phí")).not.toBeInTheDocument();
  });

  it("TierBadge_AbsentTier_FallsBackToFree", () => {
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<ProfileCard />, { initialPath: "/settings" });
    expect(screen.getByText("Miễn phí")).toBeInTheDocument();
  });

  it("TierStatusPanel_UnknownTier_TreatedAsFreeFailSafe", () => {
    setSession({ username: "demo", tier: "GOLD" }, "resolved");
    renderWithProviders(<TierStatusPanel />, { initialPath: "/settings" });
    // Unknown tier is non-privileged → the Free informational prompt.
    expect(screen.getByText("Nâng lên Premium")).toBeInTheDocument();
  });
});

describe("SettingsPage security", () => {
  it("SecurityCard_ChangePasswordLink_TargetsChangePasswordRoute", () => {
    renderWithProviders(<SettingsPage />, { initialPath: "/settings" });
    const link = screen.getByRole("link", { name: "Đổi mật khẩu" });
    expect(link).toHaveAttribute("href", "/settings/change-password");
  });
});

describe("SettingsPage preferences", () => {
  it("PreferencesCard_SelectDarkTheme_StampsDataThemeAttribute", async () => {
    const user = userEvent.setup();
    renderWithProviders(<SettingsPage />, { initialPath: "/settings" });

    await user.click(screen.getByRole("radio", { name: "Tối" }));
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("PreferencesCard_SwitchLanguageToEnUs_SwitchesCopy", async () => {
    const user = userEvent.setup();
    renderWithProviders(<SettingsPage />, { initialPath: "/settings" });

    expect(
      screen.getByRole("heading", { level: 1, name: "Cài đặt" }),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "English" }));

    await waitFor(() =>
      expect(
        screen.getByRole("heading", { level: 1, name: "Settings" }),
      ).toBeInTheDocument(),
    );
    // Settings-namespace + tier copy resolve in en-US too.
    expect(screen.getByText("Profile")).toBeInTheDocument();
    expect(screen.getByText("Free")).toBeInTheDocument();
  });
});

describe("SettingsPage i18n parity", () => {
  it("SettingsPage_EnUsDefault_RendersEnglishCopy", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    renderWithProviders(<SettingsPage />, { initialPath: "/settings" });

    expect(
      await screen.findByRole("heading", { level: 1, name: "Settings" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Upgrade to Premium")).toBeInTheDocument();
  });
});

describe("SettingsPage logout", () => {
  it("SettingsPage_Logout_ClearsSessionAndNavigatesToLogin", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <Routes>
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="/login" element={<div>Login Screen</div>} />
      </Routes>,
      { initialPath: "/settings" },
    );

    await user.click(screen.getByRole("button", { name: "Đăng xuất" }));

    expect(await screen.findByText("Login Screen")).toBeInTheDocument();
    expect(getSession().status).toBe("unauthenticated");
    expect(getSession().accessToken).toBeNull();
  });
});
