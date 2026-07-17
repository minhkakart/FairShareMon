import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { AdminUserActions } from "./components/users/AdminUserActions";
import type { AdminUserDetailResponse } from "./api/types";

/**
 * AdminUserActions — the client-side self/other-admin guard (R-Guards14xxx). Proves
 * the destructive actions (disable, revoke-tokens, reset-password, demote) are
 * DISABLED with an explanatory tooltip when the target is self (14001) or another
 * ADMIN (14002), enabled for a non-admin target; tier grant/revoke + promote stay
 * enabled; and clicking an enabled action opens its dialog. No network — the guard
 * is computed from the session `me.uuid` + the target row.
 */

function user(overrides: Partial<AdminUserDetailResponse>): AdminUserDetailResponse {
  return {
    uuid: "u-target",
    username: "bob",
    tier: "PREMIUM",
    role: "USER",
    status: "ACTIVE",
    createdAt: "2026-03-01T09:00:00.000Z",
    grants: [],
    ...overrides,
  };
}

function seedMe(uuid: string) {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-admin-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-admin-t",
    refreshTokenExpiresAt: future,
    user: { username: "admin", role: "ADMIN", uuid },
    profileStatus: "resolved",
  });
}

const GUARDED = [
  "Khóa tài khoản",
  "Đăng xuất mọi thiết bị",
  "Đặt lại mật khẩu",
];

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});
afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("AdminUserActions — guards", () => {
  it("AdminUserActions_NonAdminTargetNotSelf_EnablesAllActions", () => {
    seedMe("uuid-admin");
    renderWithProviders(<AdminUserActions user={user({ uuid: "u-target" })} />);

    for (const label of GUARDED) {
      expect(screen.getByRole("button", { name: label })).toBeEnabled();
    }
    expect(screen.getByRole("button", { name: "Cấp Premium" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Thu hồi Premium" })).toBeEnabled();
    // A USER target → the role action is "promote" (routine, always enabled).
    expect(
      screen.getByRole("button", { name: "Thăng làm Quản trị viên" }),
    ).toBeEnabled();
  });

  it("AdminUserActions_SelfTarget_DisablesGuardedActionsWithSelfTooltip", () => {
    seedMe("u-self");
    renderWithProviders(<AdminUserActions user={user({ uuid: "u-self" })} />);

    for (const label of GUARDED) {
      const btn = screen.getByRole("button", { name: label });
      expect(btn).toBeDisabled();
      // The wrapping span carries the explanatory tooltip.
      expect(btn.closest("span")).toHaveAttribute(
        "title",
        "Không thể thực hiện hành động này với tài khoản của chính bạn.",
      );
    }
    // Tier grant stays enabled even on self.
    expect(screen.getByRole("button", { name: "Cấp Premium" })).toBeEnabled();
  });

  it("AdminUserActions_AdminTargetNotSelf_DisablesGuardedIncludingDemoteWithAdminTooltip", () => {
    seedMe("uuid-admin");
    renderWithProviders(
      <AdminUserActions user={user({ uuid: "u-other-admin", role: "ADMIN" })} />,
    );

    for (const label of GUARDED) {
      const btn = screen.getByRole("button", { name: label });
      expect(btn).toBeDisabled();
      expect(btn.closest("span")).toHaveAttribute(
        "title",
        "Không thể thực hiện hành động này với một Quản trị viên khác.",
      );
    }
    // An ADMIN target shows demote (guarded/disabled), never promote.
    const demote = screen.getByRole("button", { name: "Hạ về Người dùng" });
    expect(demote).toBeDisabled();
    expect(
      screen.queryByRole("button", { name: "Thăng làm Quản trị viên" }),
    ).not.toBeInTheDocument();
    // Tier grant still enabled.
    expect(screen.getByRole("button", { name: "Cấp Premium" })).toBeEnabled();
  });

  it("AdminUserActions_ClickEnabledDisable_OpensTheDisableDialog", async () => {
    seedMe("uuid-admin");
    renderWithProviders(<AdminUserActions user={user({ uuid: "u-target" })} />);

    await userEvent.click(screen.getByRole("button", { name: "Khóa tài khoản" }));
    // The disable confirm dialog opens with its own title.
    expect(
      await screen.findByText("Khóa tài khoản này?"),
    ).toBeInTheDocument();
  });
});
