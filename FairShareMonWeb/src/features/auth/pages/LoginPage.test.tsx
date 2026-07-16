import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { LoginPage } from "./LoginPage";
import { getSession, sessionStore } from "@/lib/auth/session";
import { ErrorCodes } from "@/lib/api/errors";

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: {
    code: number;
    message: string;
    fields?: Record<string, string[]>;
  } | null;
}

function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}
function fail(
  code: number,
  message: string,
  status: number,
  fields?: Record<string, string[]>,
) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message, fields } },
    { status },
  );
}

function tokenPair() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  return {
    accessToken: "acc",
    accessTokenExpiresAt: future,
    refreshToken: "ref",
    refreshTokenExpiresAt: future,
  };
}

function renderLogin() {
  return renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/dashboard" element={<div>Dashboard Screen</div>} />
    </Routes>,
    { initialPath: "/login" },
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

describe("LoginPage", () => {
  it("LoginPage_ValidSubmit_SetsSessionAndNavigatesToDashboard", async () => {
    const user = userEvent.setup();
    let loginBody: { username: string; password: string } | null = null;
    server.use(
      http.post("*/api/v1/auth/login", async ({ request }) => {
        loginBody = (await request.json()) as {
          username: string;
          password: string;
        };
        return ok(tokenPair());
      }),
    );

    renderLogin();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "Demo");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123");
    await user.click(screen.getByRole("button", { name: "Đăng nhập" }));

    expect(await screen.findByText("Dashboard Screen")).toBeInTheDocument();
    expect(getSession().status).toBe("authenticated");
    expect(getSession().user).toEqual({ username: "demo" }); // trimmed + lowercased
    expect(loginBody).toEqual({ username: "demo", password: "password123" });
  });

  it("LoginPage_InvalidCredentials2001_RendersServerMessage", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("*/api/v1/auth/login", () =>
        fail(
          ErrorCodes.InvalidCredentials,
          "Tên đăng nhập hoặc mật khẩu không đúng.",
          401,
        ),
      ),
    );

    renderLogin();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "demo");
    await user.type(screen.getByLabelText(/Mật khẩu/), "wrongpass");
    await user.click(screen.getByRole("button", { name: "Đăng nhập" }));

    expect(
      await screen.findByText("Tên đăng nhập hoặc mật khẩu không đúng."),
    ).toBeInTheDocument();
    expect(getSession().status).toBe("unauthenticated");
    expect(screen.queryByText("Dashboard Screen")).not.toBeInTheDocument();
  });

  it("LoginPage_ValidationFailed1001_MapsFieldErrorsOntoFields", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("*/api/v1/auth/login", () =>
        fail(ErrorCodes.ValidationFailed, "Dữ liệu không hợp lệ.", 400, {
          username: ["Tên đăng nhập không hợp lệ."],
        }),
      ),
    );

    renderLogin();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "demo");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123");
    await user.click(screen.getByRole("button", { name: "Đăng nhập" }));

    expect(
      await screen.findByText("Tên đăng nhập không hợp lệ."),
    ).toBeInTheDocument();
  });

  it("LoginPage_ClientValidation_BlocksEmptySubmitWithoutNetwork", async () => {
    const user = userEvent.setup();
    let called = false;
    server.use(
      http.post("*/api/v1/auth/login", () => {
        called = true;
        return ok(tokenPair());
      }),
    );

    renderLogin();
    await user.click(screen.getByRole("button", { name: "Đăng nhập" }));

    expect(
      await screen.findByText("Vui lòng nhập tên đăng nhập."),
    ).toBeInTheDocument();
    expect(called).toBe(false);
  });

  it("LoginPage_SubmitPending_DisablesSubmitButton", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("*/api/v1/auth/login", async () => {
        await delay(50);
        return ok(tokenPair());
      }),
    );

    renderLogin();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "demo");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123");
    const submit = screen.getByRole("button", { name: "Đăng nhập" });
    await user.click(submit);

    await waitFor(() => expect(submit).toBeDisabled());
    expect(submit).toHaveAttribute("aria-busy", "true");
    // Let it settle to avoid act warnings.
    expect(await screen.findByText("Dashboard Screen")).toBeInTheDocument();
  });

  it("LoginPage_EnterKey_SubmitsForm", async () => {
    const user = userEvent.setup();
    server.use(http.post("*/api/v1/auth/login", () => ok(tokenPair())));

    renderLogin();
    await user.type(screen.getByLabelText(/Tên đăng nhập/), "demo");
    await user.type(screen.getByLabelText(/Mật khẩu/), "password123{Enter}");

    expect(await screen.findByText("Dashboard Screen")).toBeInTheDocument();
  });
});
