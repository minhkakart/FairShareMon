import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { sessionStore } from "@/lib/auth/session";
import { isApiError } from "@/lib/api/errors";
import { shareApi } from "./api/shareApi";

/**
 * shareApi over the REAL centralized client + MSW at the network boundary. The
 * two PUBLIC endpoints must be anonymous (no `Authorization` header even while a
 * session is present) and must NOT trip the client's `401 → refresh → retry` loop
 * (they pass `skipAuthRefresh`). The authed owner endpoints hit the right
 * verb+path and unwrap the `ApiResult<T>` envelope's `data`.
 */

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

function seedAuthedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-sharer-tok",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-sharer-tok",
    refreshTokenExpiresAt: future,
    user: { username: "sharer", tier: "PREMIUM", role: "USER" },
    profileStatus: "resolved",
  });
}

beforeEach(() => {
  seedAuthedSession();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("shareApi public endpoints (anonymous)", () => {
  it("ShareApi_GetPublicShare_SendsNoAuthorizationHeader", async () => {
    let sawAuth: string | null = "unset";
    server.use(
      http.get("*/api/v1/public/shares/:token", ({ request }) => {
        sawAuth = request.headers.get("Authorization");
        return ok({
          eventName: "Đợt X",
          closedAt: null,
          rows: [],
          expenses: [],
          totalOutstanding: 0,
          owingMemberCount: 0,
          settledMemberCount: 0,
          hasQr: false,
        });
      }),
    );

    const data = await shareApi.getPublicShare("tok-1");

    // A session IS active, yet the anonymous request carries no bearer token.
    expect(sawAuth).toBeNull();
    expect(data.eventName).toBe("Đợt X");
  });

  it("ShareApi_GetPublicShareMemberQrs_SendsNoAuthorizationHeader_UnwrapsData", async () => {
    let sawAuth: string | null = "unset";
    server.use(
      http.get("*/api/v1/public/shares/:token/qr/members", ({ request }) => {
        sawAuth = request.headers.get("Authorization");
        return ok([
          { memberUuid: "m-1", memberName: "An", amount: 1000, image: "data:image/png;base64,AA" },
        ]);
      }),
    );

    const qrs = await shareApi.getPublicShareMemberQrs("tok-1");

    expect(sawAuth).toBeNull();
    expect(qrs).toHaveLength(1);
    expect(qrs[0].memberUuid).toBe("m-1");
  });

  it("ShareApi_PublicShare401_DoesNotRefresh_ThrowsApiError", async () => {
    // skipAuthRefresh must keep a public 401 from triggering the refresh flow —
    // an anonymous visitor has no session to refresh.
    let refreshCalls = 0;
    server.use(
      http.post("*/api/v1/auth/refresh", () => {
        refreshCalls += 1;
        return ok({});
      }),
      http.get("*/api/v1/public/shares/:token", () =>
        fail(16000, "Liên kết không tồn tại hoặc đã hết hạn.", 404),
      ),
    );

    await expect(shareApi.getPublicShare("gone")).rejects.toSatisfy(
      (err: unknown) => isApiError(err) && err.code === 16000,
    );
    expect(refreshCalls).toBe(0);
  });
});

describe("shareApi authed owner endpoints", () => {
  it("ShareApi_GetActiveLink_GetsEventSharePath_UnwrapsNullWhenNotShared", async () => {
    const seen: { method: string; path: string } = { method: "", path: "" };
    server.use(
      http.get("*/api/v1/events/:uuid/share", ({ request }) => {
        seen.method = request.method;
        seen.path = new URL(request.url).pathname;
        return ok(null);
      }),
    );

    const data = await shareApi.getActiveLink("ev-9");

    expect(seen.method).toBe("GET");
    expect(seen.path).toBe("/api/v1/events/ev-9/share");
    // OQ1 — `data:null` ("not shared yet") is unwrapped as null, never an error.
    expect(data).toBeNull();
  });

  it("ShareApi_CreateLink_PostsBodyAndPath_UnwrapsData", async () => {
    const seen: { method: string; path: string; body: unknown } = {
      method: "",
      path: "",
      body: null,
    };
    server.use(
      http.post("*/api/v1/events/:uuid/share", async ({ request }) => {
        seen.method = request.method;
        seen.path = new URL(request.url).pathname;
        seen.body = await request.json();
        return ok({
          token: "abc",
          expiresAt: "2026-07-25T10:00:00.000Z",
          createdAt: "2026-07-24T10:00:00.000Z",
          hasQr: true,
          bankName: "Vietcombank",
          accountNumber: "0071001234567",
          accountHolderName: "NGUYEN VAN MINH",
        });
      }),
    );

    const data = await shareApi.createLink("ev-9", {
      bankAccountUuid: "ba-1",
      regenerate: true,
    });

    expect(seen.method).toBe("POST");
    expect(seen.path).toBe("/api/v1/events/ev-9/share");
    expect(seen.body).toEqual({ bankAccountUuid: "ba-1", regenerate: true });
    expect(data.token).toBe("abc");
    expect(data.hasQr).toBe(true);
  });

  it("ShareApi_RevokeLink_DeletesEventSharePath_UnwrapsData", async () => {
    const seen: { method: string; path: string } = { method: "", path: "" };
    server.use(
      http.delete("*/api/v1/events/:uuid/share", ({ request }) => {
        seen.method = request.method;
        seen.path = new URL(request.url).pathname;
        return ok({ message: "Đã thu hồi liên kết chia sẻ." });
      }),
    );

    const data = await shareApi.revokeLink("ev-9");

    expect(seen.method).toBe("DELETE");
    expect(seen.path).toBe("/api/v1/events/ev-9/share");
    expect(data.message).toContain("thu hồi");
  });
});
