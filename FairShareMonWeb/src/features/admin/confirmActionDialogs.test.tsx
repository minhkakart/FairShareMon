import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import type { ReactNode } from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { resetAdminStore } from "@/test/msw/handlers";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { DisableUserDialog } from "./components/users/DisableUserDialog";
import { EnableUserDialog } from "./components/users/EnableUserDialog";
import { RevokeTokensDialog } from "./components/users/RevokeTokensDialog";
import { SetRoleDialog } from "./components/users/SetRoleDialog";

/**
 * The routine + danger confirm dialogs (disable/enable/revoke-tokens/set-role)
 * over the shared ConfirmActionDialog, against the committed admin fixtures.
 * Proves: a confirm succeeds with a success toast, closes, and invalidates the
 * users cache; the destructive dialogs carry a danger consequence callout; and a
 * server guard rejection (14001 self / 14002 other-admin — incl. the last-admin
 * demote path) surfaces INLINE (verbatim message) with the dialog kept open.
 */

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

function Harness({ render }: { render: (open: boolean, setOpen: (o: boolean) => void) => ReactNode }) {
  const [open, setOpen] = useState(true);
  return <>{render(open, setOpen)}</>;
}

function mount(node: (open: boolean, setOpen: (o: boolean) => void) => ReactNode) {
  return renderWithProviders(<Harness render={node} />, { queryClient });
}

beforeEach(async () => {
  window.localStorage.clear();
  resetAdminStore();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedAdmin();
});
afterEach(() => {
  sessionStore.getState().clearSession();
  vi.restoreAllMocks();
});

describe("Disable / Enable / RevokeTokens confirm", () => {
  it("DisableUserDialog_Confirm_TogglesToastClosesAndInvalidates", async () => {
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    mount((open, setOpen) => (
      <DisableUserDialog
        user={{ uuid: "uuid-le-b", username: "le.thi.b" }}
        open={open}
        onOpenChange={setOpen}
      />
    ));

    // Danger consequence callout is shown before confirming.
    expect(screen.getByText("Hậu quả")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Khóa tài khoản" }));

    expect(
      await screen.findByText("Đã khóa tài khoản le.thi.b."),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.queryByRole("button", { name: "Khóa tài khoản" }),
      ).not.toBeInTheDocument(),
    );
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["admin", "users"] });
  });

  it("EnableUserDialog_Confirm_TogglesSuccessToast", async () => {
    mount((open, setOpen) => (
      <EnableUserDialog
        user={{ uuid: "uuid-tran-d", username: "tran.d" }}
        open={open}
        onOpenChange={setOpen}
      />
    ));
    await userEvent.click(screen.getByRole("button", { name: "Mở khóa" }));
    expect(
      await screen.findByText("Đã mở khóa tài khoản tran.d."),
    ).toBeInTheDocument();
  });

  it("RevokeTokensDialog_Confirm_TogglesSuccessToast", async () => {
    mount((open, setOpen) => (
      <RevokeTokensDialog
        user={{ uuid: "uuid-le-b", username: "le.thi.b" }}
        open={open}
        onOpenChange={setOpen}
      />
    ));
    await userEvent.click(screen.getByRole("button", { name: "Thu hồi phiên" }));
    expect(
      await screen.findByText("Đã thu hồi toàn bộ phiên của le.thi.b."),
    ).toBeInTheDocument();
  });
});

describe("14001 / 14002 guards surface inline", () => {
  it("DisableUserDialog_SelfTarget_ShowsInline14001", async () => {
    // Targeting the acting admin (uuid-admin) → the server answers 14001.
    mount((open, setOpen) => (
      <DisableUserDialog
        user={{ uuid: "uuid-admin", username: "admin" }}
        open={open}
        onOpenChange={setOpen}
      />
    ));
    await userEvent.click(screen.getByRole("button", { name: "Khóa tài khoản" }));
    expect(
      await screen.findByText(
        "Không thể thực hiện hành động này với chính bạn.",
      ),
    ).toBeInTheDocument();
    // Dialog stays open on a guard rejection.
    expect(
      screen.getByRole("button", { name: "Khóa tài khoản" }),
    ).toBeInTheDocument();
  });

  it("DisableUserDialog_AdminTarget_ShowsInline14002", async () => {
    mount((open, setOpen) => (
      <DisableUserDialog
        user={{ uuid: "uuid-pham-admin", username: "pham.admin" }}
        open={open}
        onOpenChange={setOpen}
      />
    ));
    await userEvent.click(screen.getByRole("button", { name: "Khóa tài khoản" }));
    expect(
      await screen.findByText(
        "Không thể thực hiện hành động này với một quản trị viên khác.",
      ),
    ).toBeInTheDocument();
  });

  it("SetRoleDialog_DemoteAdminTarget_ShowsInline14002", async () => {
    mount((open, setOpen) => (
      <SetRoleDialog
        user={{ uuid: "uuid-pham-admin", username: "pham.admin" }}
        targetRole="USER"
        open={open}
        onOpenChange={setOpen}
      />
    ));
    await userEvent.click(screen.getByRole("button", { name: "Hạ vai trò" }));
    expect(
      await screen.findByText(
        "Không thể thực hiện hành động này với một quản trị viên khác.",
      ),
    ).toBeInTheDocument();
  });
});

describe("Promote (routine) succeeds", () => {
  it("SetRoleDialog_PromoteUser_TogglesSuccessToast", async () => {
    mount((open, setOpen) => (
      <SetRoleDialog
        user={{ uuid: "uuid-le-b", username: "le.thi.b" }}
        targetRole="ADMIN"
        open={open}
        onOpenChange={setOpen}
      />
    ));
    await userEvent.click(screen.getByRole("button", { name: "Thăng vai trò" }));
    expect(
      await screen.findByText("Đã thăng le.thi.b làm Quản trị viên."),
    ).toBeInTheDocument();
  });
});
