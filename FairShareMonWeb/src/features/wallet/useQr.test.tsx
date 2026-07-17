import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { useEventQrQuery, useExpenseQrQuery } from "./hooks/useQr";

/**
 * QR blob hooks over MSW. They return a `BlobResult`, are `enabled`-gated (the
 * dialog drives it from open-state + Premium tier; event QR is additionally
 * closed-only), and set `retry: false` because the terminal codes (13003 / 12xxx
 * / ownership 404) are not transient. Object-URL lifecycle is the dialog's job,
 * not the hook's — covered in qrDialog.test.tsx.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function fail(code: number, message: string, status: number) {
  return HttpResponse.json<Envelope>(
    { data: null, isSuccess: false, error: { code, message } },
    { status },
  );
}

const PNG_1x1_BASE64 =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HBwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
function pngResponse() {
  const binary = atob(PNG_1x1_BASE64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
  return new HttpResponse(bytes, {
    headers: {
      "Content-Type": "image/png",
      "Content-Disposition": 'attachment; filename="qr.png"',
    },
  });
}

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-qrhook-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-qrhook-t",
    refreshTokenExpiresAt: future,
    user: { username: "qrhook", tier: "PREMIUM", role: "USER" },
    profileStatus: "resolved",
  });
}

beforeEach(() => {
  window.localStorage.clear();
  queryClient.clear();
  seedSession();
});

afterEach(() => {
  sessionStore.getState().clearSession();
});

describe("useExpenseQrQuery", () => {
  it("UseExpenseQrQuery_Disabled_NeverFiresTheRequest", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr", () => {
        gets += 1;
        return pngResponse();
      }),
    );
    function Probe() {
      useExpenseQrQuery("e-1", undefined, false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await new Promise((r) => setTimeout(r, 30));
    expect(gets).toBe(0);
  });

  it("UseExpenseQrQuery_Enabled_ResolvesABlobResult", async () => {
    server.use(http.get("*/api/v1/expenses/:uuid/qr", () => pngResponse()));
    const captured: { q?: ReturnType<typeof useExpenseQrQuery> } = {};
    function Probe() {
      captured.q = useExpenseQrQuery("e-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isSuccess).toBe(true));
    expect(captured.q?.data?.blob).toBeInstanceOf(Blob);
    expect(captured.q?.data?.filename).toBe("qr.png");
  });

  it("UseExpenseQrQuery_TerminalError_DoesNotRetry", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr", () => {
        gets += 1;
        return fail(13003, "Premium.", 403);
      }),
    );
    const captured: { q?: ReturnType<typeof useExpenseQrQuery> } = {};
    function Probe() {
      captured.q = useExpenseQrQuery("e-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isError).toBe(true));
    // retry:false → the queryFn ran exactly once (no transient re-attempts).
    expect(gets).toBe(1);
  });
});

describe("useEventQrQuery", () => {
  it("UseEventQrQuery_Disabled_NeverFiresTheRequest", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/events/:uuid/qr", () => {
        gets += 1;
        return pngResponse();
      }),
    );
    function Probe() {
      useEventQrQuery("ev-1", undefined, false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await new Promise((r) => setTimeout(r, 30));
    expect(gets).toBe(0);
  });

  it("UseEventQrQuery_Enabled_ResolvesABlobResult", async () => {
    server.use(http.get("*/api/v1/events/:uuid/qr", () => pngResponse()));
    const captured: { q?: ReturnType<typeof useEventQrQuery> } = {};
    function Probe() {
      captured.q = useEventQrQuery("ev-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isSuccess).toBe(true));
    expect(captured.q?.data?.blob).toBeInstanceOf(Blob);
  });

  it("UseEventQrQuery_NonDefaultDestination_SendsBankAccountUuidQuery", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/events/:uuid/qr", ({ request }) => {
        seenUrl = request.url;
        return pngResponse();
      }),
    );
    function Probe() {
      useEventQrQuery("ev-1", "ba-override", true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("bankAccountUuid")).toBe(
      "ba-override",
    );
  });
});
