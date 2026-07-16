import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { api, request } from "./client";
import { ApiError, ErrorCodes, isApiError } from "./errors";
import { registerSessionExpiredHandler, setActiveLocale } from "./runtime";
import { getSession, sessionStore } from "@/lib/auth/session";
import type { TokenPairResponse } from "./types/auth";

/**
 * Exercises the REAL centralized API client against MSW at the network boundary
 * (never mocks the client). Covers envelope unwrap, header injection, typed
 * ApiError, the 401 → refresh-once → retry flow (incl. concurrent de-dup),
 * refresh-failure teardown, and the blob path.
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

function tokenPair(over: Partial<TokenPairResponse> = {}): TokenPairResponse {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  return {
    accessToken: "access-token",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-token",
    refreshTokenExpiresAt: future,
    ...over,
  };
}

beforeEach(() => {
  // Deterministic baseline: clean session + pinned locale + no redirect handler.
  sessionStore.setState({
    status: "idle",
    accessToken: null,
    accessTokenExpiresAt: null,
    refreshToken: null,
    refreshTokenExpiresAt: null,
    user: null,
  });
  setActiveLocale("vi-VN");
  registerSessionExpiredHandler(null);
  window.localStorage.clear();
});

afterEach(() => {
  registerSessionExpiredHandler(null);
});

describe("ApiClient envelope handling", () => {
  it("ApiClient_SuccessEnvelope_UnwrapsData", async () => {
    server.use(
      http.post("*/api/v1/echo", async ({ request: req }) => {
        const body = await req.json();
        return ok({ received: body });
      }),
    );

    const result = await api.post<{ received: unknown }>("/v1/echo", { x: 1 });
    expect(result).toEqual({ received: { x: 1 } });
  });

  it("ApiClient_FailureEnvelope_ThrowsTypedApiErrorWithCodeAndCamelCaseFields", async () => {
    server.use(
      http.post("*/api/v1/create", () =>
        fail(ErrorCodes.ValidationFailed, "Dữ liệu không hợp lệ.", 400, {
          userName: ["Tên đăng nhập không hợp lệ."],
        }),
      ),
    );

    const error = await api.post("/v1/create", {}).catch((e: unknown) => e);
    expect(isApiError(error)).toBe(true);
    const apiError = error as ApiError;
    expect(apiError.code).toBe(ErrorCodes.ValidationFailed);
    expect(apiError.httpStatus).toBe(400);
    expect(apiError.message).toBe("Dữ liệu không hợp lệ.");
    expect(apiError.fields).toEqual({
      userName: ["Tên đăng nhập không hợp lệ."],
    });
    expect(apiError.isValidation).toBe(true);
  });

  it("ApiClient_UnexpectedResponseShape_ThrowsInternalError", async () => {
    server.use(
      http.get("*/api/v1/weird", () =>
        HttpResponse.json({ not: "an envelope" }),
      ),
    );

    const error = await api.get("/v1/weird").catch((e: unknown) => e);
    expect(isApiError(error)).toBe(true);
    expect((error as ApiError).code).toBe(ErrorCodes.InternalError);
  });

  it("ApiClient_NetworkFailure_ThrowsNetworkApiError", async () => {
    server.use(http.get("*/api/v1/down", () => HttpResponse.error()));

    const error = await api.get("/v1/down").catch((e: unknown) => e);
    expect(isApiError(error)).toBe(true);
    expect((error as ApiError).isNetwork).toBe(true);
    expect((error as ApiError).code).toBe(ErrorCodes.Network);
  });
});

describe("ApiClient header injection", () => {
  it("ApiClient_AuthenticatedRequest_InjectsAuthorizationTimeZoneAndAcceptLanguage", async () => {
    getSession().setSession(tokenPair({ accessToken: "the-access-token" }));
    setActiveLocale("en-US");

    let seen: Record<string, string | null> = {};
    server.use(
      http.get("*/api/v1/whoami", ({ request: req }) => {
        seen = {
          authorization: req.headers.get("Authorization"),
          timeZone: req.headers.get("X-Time-Zone"),
          acceptLanguage: req.headers.get("Accept-Language"),
        };
        return ok({ ok: true });
      }),
    );

    await api.get("/v1/whoami");
    expect(seen.authorization).toBe("Bearer the-access-token");
    // TZ is pinned to Asia/Ho_Chi_Minh in src/test/setup.ts (Node's ICU may
    // report the equivalent alias Asia/Saigon — both are the UTC+7 IANA zone).
    expect(["Asia/Ho_Chi_Minh", "Asia/Saigon"]).toContain(seen.timeZone);
    expect(seen.acceptLanguage).toBe("en-US");
  });

  it("ApiClient_AnonymousRequest_OmitsAuthorizationHeader", async () => {
    getSession().setSession(tokenPair({ accessToken: "should-not-be-sent" }));

    let auth: string | null = "unset";
    server.use(
      http.post("*/api/v1/auth/anon", ({ request: req }) => {
        auth = req.headers.get("Authorization");
        return ok({ ok: true });
      }),
    );

    await request("POST", "/v1/auth/anon", { body: {}, anonymous: true });
    expect(auth).toBeNull();
  });
});

describe("ApiClient 401 → refresh → retry", () => {
  it("ApiClient_401ThenValidRefresh_RetriesWithNewTokenAndSucceeds", async () => {
    getSession().setSession(
      tokenPair({ accessToken: "stale", refreshToken: "valid-refresh" }),
    );

    let refreshBody: { refreshToken: string } | null = null;
    server.use(
      http.post("*/api/v1/auth/refresh", async ({ request: req }) => {
        refreshBody = (await req.json()) as { refreshToken: string };
        return ok(tokenPair({ accessToken: "fresh", refreshToken: "rotated" }));
      }),
      http.get("*/api/v1/protected", ({ request: req }) => {
        const auth = req.headers.get("Authorization");
        if (auth === "Bearer fresh") return ok({ secret: 42 });
        return fail(ErrorCodes.Unauthorized, "Unauthorized", 401);
      }),
    );

    const result = await api.get<{ secret: number }>("/v1/protected");
    expect(result).toEqual({ secret: 42 });
    // Refresh was called with the persisted refresh token.
    expect(refreshBody).toEqual({ refreshToken: "valid-refresh" });
    // Session now holds the rotated pair.
    expect(getSession().accessToken).toBe("fresh");
    expect(getSession().refreshToken).toBe("rotated");
  });

  it("ApiClient_Concurrent401s_ShareOneRefresh", async () => {
    getSession().setSession(
      tokenPair({ accessToken: "stale", refreshToken: "valid-refresh" }),
    );

    let refreshCount = 0;
    server.use(
      http.post("*/api/v1/auth/refresh", async () => {
        refreshCount += 1;
        // Hold the refresh in-flight so all queued 401s reuse the same promise.
        await delay(30);
        return ok(tokenPair({ accessToken: "fresh", refreshToken: "rotated" }));
      }),
      http.get("*/api/v1/protected", ({ request: req }) => {
        const auth = req.headers.get("Authorization");
        if (auth === "Bearer fresh") return ok({ ok: true });
        return fail(ErrorCodes.Unauthorized, "Unauthorized", 401);
      }),
    );

    const results = await Promise.all([
      api.get("/v1/protected"),
      api.get("/v1/protected"),
      api.get("/v1/protected"),
    ]);

    expect(refreshCount).toBe(1);
    expect(results).toEqual([{ ok: true }, { ok: true }, { ok: true }]);
  });

  it("ApiClient_RefreshReuse2002_ClearsSessionAndSignalsRedirect", async () => {
    getSession().setSession(
      tokenPair({ accessToken: "stale", refreshToken: "revoked-refresh" }),
    );
    const onExpired = vi.fn();
    registerSessionExpiredHandler(onExpired);

    server.use(
      http.post("*/api/v1/auth/refresh", () =>
        fail(ErrorCodes.InvalidRefreshToken, "Mã gia hạn không hợp lệ.", 401),
      ),
      http.get("*/api/v1/protected", () =>
        fail(ErrorCodes.Unauthorized, "Unauthorized", 401),
      ),
    );

    const error = await api.get("/v1/protected").catch((e: unknown) => e);
    expect(isApiError(error)).toBe(true);
    // Terminal: session hard-cleared + redirect signalled exactly once.
    expect(getSession().accessToken).toBeNull();
    expect(getSession().refreshToken).toBeNull();
    expect(getSession().status).toBe("unauthenticated");
    expect(onExpired).toHaveBeenCalledTimes(1);
    expect(window.localStorage.getItem("fsm.refreshToken")).toBeNull();
  });

  it("ApiClient_401WithNoRefreshToken_ClearsSessionAndSignalsRedirect", async () => {
    // Authenticated access token but no refresh token to exchange.
    sessionStore.setState({
      status: "authenticated",
      accessToken: "stale",
      accessTokenExpiresAt: new Date(Date.now() + 3_600_000).toISOString(),
      refreshToken: null,
      refreshTokenExpiresAt: null,
      user: { username: "x" },
    });
    const onExpired = vi.fn();
    registerSessionExpiredHandler(onExpired);

    server.use(
      http.get("*/api/v1/protected", () =>
        fail(ErrorCodes.Unauthorized, "Unauthorized", 401),
      ),
    );

    const error = await api.get("/v1/protected").catch((e: unknown) => e);
    expect(isApiError(error)).toBe(true);
    expect((error as ApiError).code).toBe(ErrorCodes.Unauthorized);
    expect(getSession().status).toBe("unauthenticated");
    expect(onExpired).toHaveBeenCalledTimes(1);
  });
});

describe("ApiClient blob path", () => {
  it("ApiClient_BlobResponse_ReturnsBlobWithParsedFilename", async () => {
    getSession().setSession(tokenPair());
    server.use(
      http.get(
        "*/api/v1/export",
        () =>
          new HttpResponse("name,amount\nA,1000", {
            headers: {
              "Content-Type": "text/csv",
              "Content-Disposition": 'attachment; filename="export.csv"',
            },
          }),
      ),
    );

    const result = await api.blob("GET", "/v1/export");
    expect(result.blob).toBeInstanceOf(Blob);
    expect(result.filename).toBe("export.csv");
    expect(result.contentType).toContain("text/csv");
    await expect(result.blob.text()).resolves.toContain("name,amount");
  });

  it("ApiClient_BlobPathError_ThrowsTypedApiError", async () => {
    getSession().setSession(tokenPair());
    server.use(
      http.get("*/api/v1/export", () =>
        fail(ErrorCodes.NotFound, "Không tìm thấy.", 404),
      ),
    );

    const error = await api.blob("GET", "/v1/export").catch((e: unknown) => e);
    expect(isApiError(error)).toBe(true);
    expect((error as ApiError).code).toBe(ErrorCodes.NotFound);
  });
});
