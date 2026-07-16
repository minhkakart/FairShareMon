import { beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { UserEvent } from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { renderWithProviders } from "@/test/utils";
import { AppShellLayout } from "./AppShellLayout";
import { getSession, sessionStore } from "@/lib/auth/session";
import type { ProfileStatus, SessionUser } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";

/**
 * App shell / navigation (M1). Drives the REAL `AppShellLayout` + `AppShell`
 * primitive against the session store (no `/auth/me` — the layout reads the store
 * directly; `useCurrentUserQuery` is mounted at `ProtectedRoute`, which these
 * tests deliberately omit so no network fires). Copy is the vi-VN default.
 *
 * jsdom note (per the plan): the shell is mobile-first — the inline nav is
 * `display:none` until the 64rem media query, which jsdom does not apply, so the
 * inline nav is inaccessible in tests. The mobile DRAWER is the jsdom-observable
 * navigation surface, so nav registration/active-state/navigation are asserted
 * through it (the CSS breakpoint collapse itself is not jsdom-observable).
 *
 * NOTE on the pending account-label: the shell shows the neutral `common:account`
 * fallback (NOT a Skeleton) while the profile is pending — a locked foundation
 * decision asserted by `currentUserProfile.test.tsx`. These tests honor it.
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

function renderShell(initialPath = "/dashboard") {
  return renderWithProviders(
    <Routes>
      <Route element={<AppShellLayout />}>
        <Route path="/dashboard" element={<div>Dashboard Screen</div>} />
        <Route path="/members" element={<div>Members Screen</div>} />
        <Route path="/settings" element={<div>Settings Screen</div>} />
        <Route path="/admin" element={<div>Admin Screen</div>} />
      </Route>
      <Route path="/login" element={<div>Login Screen</div>} />
    </Routes>,
    { initialPath },
  );
}

/** Open the mobile drawer (the jsdom-observable nav surface) and return it. */
async function openDrawer(user: UserEvent) {
  await user.click(screen.getByRole("button", { name: "Menu" }));
  return screen.findByRole("dialog");
}

beforeEach(() => {
  window.localStorage.clear();
  queryClient.clear();
  setSession({ username: "demo", role: "USER", tier: "FREE" }, "resolved");
});

describe("AppShellLayout nav", () => {
  it("Shell_UserResolved_RendersOneNavItemPerVisibleEntryAdminHidden", async () => {
    const user = userEvent.setup();
    renderShell();
    const drawer = await openDrawer(user);

    // Every non-admin roadmap area renders as a nav link (vi-VN labels).
    for (const label of [
      "Tổng quan",
      "Thành viên",
      "Danh mục",
      "Nhãn",
      "Chi tiêu",
      "Đợt",
      "Thống kê",
      "Ví",
    ]) {
      expect(
        within(drawer).getByRole("link", { name: label }),
      ).toBeInTheDocument();
    }
    // Admin nav is hidden for a USER.
    expect(
      within(drawer).queryByRole("link", { name: "Quản trị" }),
    ).not.toBeInTheDocument();
  });

  it("Shell_ActiveRoute_MarksMatchingEntryAriaCurrentPage", async () => {
    const user = userEvent.setup();
    renderShell("/dashboard");
    const drawer = await openDrawer(user);

    expect(
      within(drawer).getByRole("link", { name: "Tổng quan" }),
    ).toHaveAttribute("aria-current", "page");
    // A non-active entry is not marked current.
    expect(
      within(drawer).getByRole("link", { name: "Thành viên" }),
    ).not.toHaveAttribute("aria-current", "page");
  });

  it("Shell_ClickNavEntry_NavigatesToThatRoute", async () => {
    const user = userEvent.setup();
    renderShell("/dashboard");
    const drawer = await openDrawer(user);

    expect(screen.getByText("Dashboard Screen")).toBeInTheDocument();
    await user.click(within(drawer).getByRole("link", { name: "Thành viên" }));

    expect(await screen.findByText("Members Screen")).toBeInTheDocument();
  });

  it("Shell_AccountButton_LinksToSettings", async () => {
    const user = userEvent.setup();
    renderShell("/dashboard");

    // The account button carries the resolved username and links to /settings.
    const accountLink = screen.getByRole("link", { name: "demo" });
    expect(accountLink).toHaveAttribute("href", "/settings");

    await user.click(accountLink);
    expect(await screen.findByText("Settings Screen")).toBeInTheDocument();
  });

  it("Shell_ProfileResolved_AccountButtonShowsUsername", () => {
    renderShell();
    // Account affordance is a Link (`Button asChild`) → role="link".
    expect(screen.getByRole("link", { name: "demo" })).toBeInTheDocument();
  });

  it("Shell_ProfilePending_AccountButtonShowsNeutralFallbackNotSkeleton", () => {
    // Locked foundation behavior (currentUserProfile.test.tsx): during a pending
    // boot-rehydrate profile (authenticated, user still null until /auth/me) the
    // shell shows the neutral fallback, never a Skeleton.
    setSession(null, "pending");
    renderShell();

    expect(
      screen.getByRole("link", { name: "Tài khoản" }),
    ).toBeInTheDocument();
  });
});

describe("AppShellLayout admin visibility", () => {
  it("Shell_AdminResolved_ShowsAdminNavAndCanReachAdmin", async () => {
    const user = userEvent.setup();
    setSession({ username: "root", role: "ADMIN", tier: "PREMIUM" }, "resolved");
    renderShell("/dashboard");
    const drawer = await openDrawer(user);

    const adminLink = within(drawer).getByRole("link", { name: "Quản trị" });
    expect(adminLink).toBeInTheDocument();

    await user.click(adminLink);
    expect(await screen.findByText("Admin Screen")).toBeInTheDocument();
  });

  it("Shell_UserResolved_DoesNotRenderAdminNav", async () => {
    const user = userEvent.setup();
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderShell("/dashboard");
    const drawer = await openDrawer(user);

    expect(
      within(drawer).queryByRole("link", { name: "Quản trị" }),
    ).not.toBeInTheDocument();
  });
});

describe("AppShellLayout mobile nav (OQ1a)", () => {
  it("Shell_MobileMenuButton_IsPresentAndDrawerClosedByDefault", () => {
    renderShell();
    expect(screen.getByRole("button", { name: "Menu" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("Shell_OpenMobileMenu_RevealsSameNavEntriesInsideDrawer", async () => {
    const user = userEvent.setup();
    renderShell();

    const drawer = await openDrawer(user);
    for (const label of ["Tổng quan", "Thành viên", "Ví"]) {
      expect(
        within(drawer).getByRole("link", { name: label }),
      ).toBeInTheDocument();
    }
  });

  it("Shell_MobileMenuKeyboard_OpensWithEnterClosesWithEscapeAndRestoresFocus", async () => {
    const user = userEvent.setup();
    renderShell();

    const trigger = screen.getByRole("button", { name: "Menu" });
    trigger.focus();
    await user.keyboard("{Enter}");

    expect(await screen.findByRole("dialog")).toBeInTheDocument();

    await user.keyboard("{Escape}");
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    // Radix restores focus to the trigger on close (keyboard a11y).
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("Shell_ActivateNavInDrawer_ClosesDrawerAndNavigates", async () => {
    const user = userEvent.setup();
    renderShell("/dashboard");
    const drawer = await openDrawer(user);

    await user.click(within(drawer).getByRole("link", { name: "Thành viên" }));

    // Navigation happened AND the drawer closed on activation.
    expect(await screen.findByText("Members Screen")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
  });
});

describe("AppShellLayout logout", () => {
  it("Shell_Logout_ClearsSessionAndRedirectsToLogin", async () => {
    const user = userEvent.setup();
    // `useLogoutAction` clears the singleton queryClient + session directly.
    renderShell("/dashboard");

    await user.click(screen.getByRole("button", { name: "Đăng xuất" }));

    expect(await screen.findByText("Login Screen")).toBeInTheDocument();
    expect(getSession().status).toBe("unauthenticated");
    expect(getSession().accessToken).toBeNull();
  });
});
