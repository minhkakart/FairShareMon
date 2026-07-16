import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { ChangePasswordPage } from "./ChangePasswordPage";
import { getSession, sessionStore } from "@/lib/auth/session";
import { ErrorCodes } from "@/lib/api/errors";

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}
function fail(code: number, message: string, status: number) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message } },
    { status },
  );
}

function seedAuthenticated() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "acc",
    accessTokenExpiresAt: future,
    refreshToken: "ref",
    refreshTokenExpiresAt: future,
    user: { username: "demo" },
  });
}

function renderChangePassword() {
  return renderWithProviders(
    <Routes>
      <Route
        path="/settings/change-password"
        element={<ChangePasswordPage />}
      />
      <Route path="/login" element={<h1>Đăng nhập</h1>} />
    </Routes>,
    { initialPath: "/settings/change-password" },
  );
}

beforeEach(() => {
  window.localStorage.clear();
  seedAuthenticated();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("ChangePasswordPage", () => {
  it("ChangePasswordPage_CurrentPasswordWrong2003_RendersServerMessage", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("*/api/v1/auth/change-password", () =>
        fail(
          ErrorCodes.CurrentPasswordIncorrect,
          "Mật khẩu hiện tại không đúng.",
          400,
        ),
      ),
    );

    renderChangePassword();
    await user.type(screen.getByLabelText(/Mật khẩu hiện tại/), "wrongold1");
    await user.type(screen.getByLabelText(/Mật khẩu mới/), "newpass123");
    await user.click(screen.getByRole("button", { name: "Đổi mật khẩu" }));

    expect(
      await screen.findByText("Mật khẩu hiện tại không đúng."),
    ).toBeInTheDocument();
    // Session untouched on failure.
    expect(getSession().status).toBe("authenticated");
  });

  it("ChangePasswordPage_Success_ClearsSessionAndShowsReloginNotice", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("*/api/v1/auth/change-password", () =>
        ok({ message: "Đổi mật khẩu thành công." }),
      ),
    );

    renderChangePassword();
    await user.type(screen.getByLabelText(/Mật khẩu hiện tại/), "oldpass123");
    await user.type(screen.getByLabelText(/Mật khẩu mới/), "newpass123");
    await user.click(screen.getByRole("button", { name: "Đổi mật khẩu" }));

    // Redirected to login (all sessions revoked → forced re-login).
    expect(
      await screen.findByRole("heading", { name: "Đăng nhập" }),
    ).toBeInTheDocument();
    expect(getSession().status).toBe("unauthenticated");
    expect(getSession().accessToken).toBeNull();
    // The re-login notice toast is shown.
    expect(
      await screen.findByText(/Vui lòng đăng nhập lại trên tất cả thiết bị/),
    ).toBeInTheDocument();
  });

  it("ChangePasswordPage_ShortNewPassword_BlocksSubmitWithClientError", async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      http.post("*/api/v1/auth/change-password", () => {
        called = true;
        return ok({ message: "ok" });
      }),
    );

    renderChangePassword();
    await user.type(screen.getByLabelText(/Mật khẩu hiện tại/), "oldpass123");
    await user.type(screen.getByLabelText(/Mật khẩu mới/), "short");
    await user.click(screen.getByRole("button", { name: "Đổi mật khẩu" }));

    expect(
      await screen.findByText("Mật khẩu phải có ít nhất 8 ký tự."),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });
});
