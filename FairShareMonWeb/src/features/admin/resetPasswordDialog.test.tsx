import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { resetAdminStore } from "@/test/msw/handlers";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ResetPasswordDialog } from "./components/users/ResetPasswordDialog";

/**
 * ResetPasswordDialog (OQ3a — the highest-severity action). Proves: a strong temp
 * password is generated client-side (meets the charset/length rules) and can be
 * regenerated; submit reveals it EXACTLY once; copy writes it to the clipboard with
 * a live-region confirm; the secret is held in component state ONLY — never in the
 * TanStack cache or localStorage — and is cleared when the dialog closes; and a
 * `14002` guard rejection surfaces inline (no reveal).
 */

const STRONG = {
  upper: /[A-HJ-NP-Z]/,
  lower: /[a-hj-km-np-z]/,
  digit: /[2-9]/,
  symbol: /[!@#$%^&*?]/,
};

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

function Harness() {
  const [open, setOpen] = useState(true);
  return (
    <ResetPasswordDialog
      user={{ uuid: "uuid-le-b", username: "le.thi.b" }}
      open={open}
      onOpenChange={setOpen}
    />
  );
}

function generatedField(): HTMLInputElement {
  return screen.getByRole("textbox", {
    name: "Mật khẩu tạm (tạo tự động)",
  }) as HTMLInputElement;
}

/** Every value ever cached in the singleton client, serialized. */
function cacheDump(): string {
  return JSON.stringify(
    queryClient.getQueryCache().getAll().map((q) => q.state.data),
  );
}

/** Serialize all of localStorage without spreading the Storage instance. */
function storageDump(): string {
  let out = "";
  for (let i = 0; i < window.localStorage.length; i += 1) {
    const key = window.localStorage.key(i) ?? "";
    out += `${key}=${window.localStorage.getItem(key)};`;
  }
  return out;
}

let writeText: ReturnType<typeof vi.fn>;

beforeEach(async () => {
  window.localStorage.clear();
  resetAdminStore();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedAdmin();
  writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, "clipboard", {
    value: { writeText },
    configurable: true,
  });
});
afterEach(() => {
  sessionStore.getState().clearSession();
  vi.restoreAllMocks();
});

describe("ResetPasswordDialog", () => {
  it("ResetPasswordDialog_GeneratedPassword_MeetsRulesAndRegenerates", async () => {
    renderWithProviders(<Harness />, { queryClient });

    const first = generatedField().value;
    expect(first.length).toBeGreaterThanOrEqual(12);
    expect(first.length).toBeLessThanOrEqual(16);
    expect(STRONG.upper.test(first)).toBe(true);
    expect(STRONG.lower.test(first)).toBe(true);
    expect(STRONG.digit.test(first)).toBe(true);
    expect(STRONG.symbol.test(first)).toBe(true);

    await userEvent.click(screen.getByRole("button", { name: "Tạo lại" }));
    // Regenerate replaces the value (overwhelmingly different).
    expect(generatedField().value).not.toBe(first);
  });

  it("ResetPasswordDialog_Submit_RevealsOnceThenCopyConfirms", async () => {
    renderWithProviders(<Harness />, { queryClient });
    const generated = generatedField().value;

    await userEvent.click(
      screen.getByRole("button", { name: "Đặt lại mật khẩu" }),
    );

    // One-time reveal panel shows the exact temp password the server echoed.
    expect(
      await screen.findByText("Mật khẩu tạm — chỉ hiển thị một lần"),
    ).toBeInTheDocument();
    expect(screen.getByText(generated)).toBeInTheDocument();

    // Copy → clipboard write + live-region confirm + button flips to "Đã sao chép".
    await userEvent.click(screen.getByRole("button", { name: "Sao chép" }));
    expect(writeText).toHaveBeenCalledWith(generated);
    expect(
      await screen.findByText("Đã sao chép mật khẩu tạm vào bộ nhớ tạm."),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Đã sao chép" }),
    ).toBeInTheDocument();
  });

  it("ResetPasswordDialog_Secret_NeverInCacheOrStorageAndClearedOnClose", async () => {
    renderWithProviders(<Harness />, { queryClient });
    const generated = generatedField().value;

    await userEvent.click(
      screen.getByRole("button", { name: "Đặt lại mật khẩu" }),
    );
    expect(await screen.findByText(generated)).toBeInTheDocument();

    // The reset-password response is NEVER cached, and nothing is persisted.
    expect(cacheDump()).not.toContain(generated);
    expect(window.localStorage.getItem("newPassword")).toBeNull();
    expect(storageDump()).not.toContain(generated);

    // Close destroys the reveal — the secret is gone from the DOM.
    await userEvent.click(
      screen.getByRole("button", { name: "Tôi đã sao chép — Đóng" }),
    );
    await waitFor(() =>
      expect(screen.queryByText(generated)).not.toBeInTheDocument(),
    );
    expect(
      screen.queryByText("Mật khẩu tạm — chỉ hiển thị một lần"),
    ).not.toBeInTheDocument();
    // Still absent from cache after close.
    expect(cacheDump()).not.toContain(generated);
  });

  it("ResetPasswordDialog_Guard14002_SurfacesInlineNoReveal", async () => {
    server.use(
      http.post("*/api/v1/admin/users/:uuid/reset-password", () =>
        HttpResponse.json(
          {
            data: null,
            isSuccess: false,
            error: {
              code: 14002,
              message: "Không thể thực hiện hành động này với một quản trị viên khác.",
            },
          },
          { status: 400 },
        ),
      ),
    );
    renderWithProviders(<Harness />, { queryClient });
    const generated = generatedField().value;

    await userEvent.click(
      screen.getByRole("button", { name: "Đặt lại mật khẩu" }),
    );

    // The guard message shows inline; the one-time reveal never opens.
    expect(
      await screen.findByText(
        "Không thể thực hiện hành động này với một quản trị viên khác.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByText("Mật khẩu tạm — chỉ hiển thị một lần"),
    ).not.toBeInTheDocument();
    // The revealed secret code is never shown on the guard path.
    expect(cacheDump()).not.toContain(generated);
  });
});
