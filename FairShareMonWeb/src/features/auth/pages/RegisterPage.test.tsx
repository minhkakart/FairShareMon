import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { RegisterPage } from "./RegisterPage";
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

function renderRegister() {
  return renderWithProviders(
    <Routes>
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/login" element={<h1>Đăng nhập</h1>} />
    </Routes>,
    { initialPath: "/register" },
  );
}

beforeEach(() => {
  window.localStorage.clear();
  sessionStore.setState({
    status: "unauthenticated",
    accessToken: null,
    accessTokenExpiresAt: null,
    refreshToken: null,
    refreshTokenExpiresAt: null,
    user: null,
  });
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("RegisterPage", () => {
  it("RegisterPage_UsernameTooShort_BlocksSubmitWithClientError", async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      http.post("*/api/v1/auth/register", () => {
        called = true;
        return ok({});
      }),
    );

    renderRegister();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "ab");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123");
    await user.click(screen.getByRole("button", { name: "Đăng ký" }));

    expect(
      await screen.findByText("Tên đăng nhập phải từ 3 đến 32 ký tự."),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it("RegisterPage_PasswordOver72Bytes_BlocksSubmitWithClientError", async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      http.post("*/api/v1/auth/register", () => {
        called = true;
        return ok({});
      }),
    );

    renderRegister();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "validuser");
    await user.type(screen.getByLabelText(/Mật khẩu/), "a".repeat(73));
    await user.click(screen.getByRole("button", { name: "Đăng ký" }));

    expect(
      await screen.findByText("Mật khẩu quá dài (tối đa 72 byte)."),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it("RegisterPage_UsernameTaken2000_RendersServerMessage", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("*/api/v1/auth/register", () =>
        fail(ErrorCodes.UsernameTaken, "Tên đăng nhập đã tồn tại.", 400),
      ),
    );

    renderRegister();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "takenuser");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123");
    await user.click(screen.getByRole("button", { name: "Đăng ký" }));

    expect(
      await screen.findByText("Tên đăng nhập đã tồn tại."),
    ).toBeInTheDocument();
  });

  it("RegisterPage_Success_RoutesToLoginWithNoAutoLogin", async () => {
    const user = userEvent.setup();
    let registerBody: { username: string; password: string } | null = null;
    server.use(
      http.post("*/api/v1/auth/register", async ({ request }) => {
        registerBody = (await request.json()) as {
          username: string;
          password: string;
        };
        return ok({
          uuid: "u1",
          username: "newuser",
          tier: "FREE",
          createdAt: new Date().toISOString(),
        });
      }),
    );

    renderRegister();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "NewUser");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123");
    await user.click(screen.getByRole("button", { name: "Đăng ký" }));

    // Landed on the login route (heading), NOT auto-authenticated.
    expect(
      await screen.findByRole("heading", { name: "Đăng nhập" }),
    ).toBeInTheDocument();
    expect(getSession().status).toBe("unauthenticated");
    expect(getSession().accessToken).toBeNull();
    expect(registerBody).toEqual({
      username: "newuser",
      password: "password123",
    });
  });
});
