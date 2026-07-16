import { http, HttpResponse } from "msw";
import type {
  ChangePasswordRequest,
  LoginRequest,
  RegisterRequest,
  RefreshRequest,
} from "@/lib/api/types/auth";

/**
 * Envelope-shaped auth handlers. Used to run the app against mocks when the
 * backend/DB is unreachable (VITE_ENABLE_MOCKS) AND by the Vitest harness. They
 * exercise the REAL client (envelope unwrap, refresh, error codes) at the
 * network boundary — the `*` origin prefix matches both same-origin (browser)
 * and jsdom (localhost). A tiny in-memory store makes the flow demonstrable.
 */

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

const users = new Map<string, string>([["demo", "password123"]]);
const validRefreshTokens = new Set<string>();
let lastLoggedInUser: string | null = null;

function rand(): string {
  return Math.random().toString(36).slice(2);
}

function issueTokens(username: string) {
  const now = Date.now();
  const refreshToken = `refresh-${username}-${now}-${rand()}`;
  validRefreshTokens.add(refreshToken);
  return {
    accessToken: `access-${username}-${now}-${rand()}`,
    accessTokenExpiresAt: new Date(now + 30 * 60_000).toISOString(),
    refreshToken,
    refreshTokenExpiresAt: new Date(now + 30 * 86_400_000).toISOString(),
  };
}

export const handlers = [
  http.post("*/api/v1/auth/register", async ({ request }) => {
    const body = (await request.json()) as RegisterRequest;
    if (users.has(body.username)) {
      return fail(2000, "Tên đăng nhập đã tồn tại.", 400);
    }
    users.set(body.username, body.password);
    return ok({
      uuid: `uuid-${rand()}`,
      username: body.username,
      tier: "FREE",
      createdAt: new Date().toISOString(),
    });
  }),

  http.post("*/api/v1/auth/login", async ({ request }) => {
    const body = (await request.json()) as LoginRequest;
    if (users.get(body.username) === body.password) {
      lastLoggedInUser = body.username;
      return ok(issueTokens(body.username));
    }
    return fail(2001, "Tên đăng nhập hoặc mật khẩu không đúng.", 401);
  }),

  http.post("*/api/v1/auth/refresh", async ({ request }) => {
    const body = (await request.json()) as RefreshRequest;
    if (!validRefreshTokens.has(body.refreshToken)) {
      // Reuse/expired: 2002 (terminal — client hard-clears the session).
      return fail(2002, "Mã gia hạn phiên không hợp lệ hoặc đã hết hạn.", 401);
    }
    validRefreshTokens.delete(body.refreshToken); // full pair rotation
    const username = body.refreshToken.split("-")[1] ?? "demo";
    return ok(issueTokens(username));
  }),

  http.post("*/api/v1/auth/logout", () => ok({ message: "Đã đăng xuất." })),

  http.post("*/api/v1/auth/change-password", async ({ request }) => {
    const body = (await request.json()) as ChangePasswordRequest;
    const username = lastLoggedInUser ?? "demo";
    if (users.get(username) !== body.currentPassword) {
      return fail(2003, "Mật khẩu hiện tại không đúng.", 400);
    }
    users.set(username, body.newPassword);
    validRefreshTokens.clear(); // change-password revokes ALL tokens
    return ok({ message: "Đổi mật khẩu thành công." });
  }),
];
