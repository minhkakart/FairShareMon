import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import type { MemberQrResponse } from "./api/types";
import {
  useEventMemberQrsQuery,
  useExpenseMemberQrsQuery,
} from "./hooks/useQr";

/**
 * Per-member QR list hooks over MSW. They return `MemberQrResponse[]` from the new
 * JSON `…/qr/members` endpoints (no blob, no object URL), are `enabled`-gated (the
 * dialog drives it from open-state + Premium tier; the event variant is
 * additionally closed-only), and set `retry: false` because the terminal codes
 * (13003 / 12001 / 12002 / 12003 / ownership 404) are not transient. The optional
 * `bankAccountUuid` maps straight to the `?bankAccountUuid=` query param (the
 * client's `buildUrl` drops it when undefined) — the dialog decides "non-default".
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

const DATA_URL =
  "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HBwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

function members(): MemberQrResponse[] {
  return [
    { memberUuid: "m-1", memberName: "An Nguyễn", amount: 120000, image: DATA_URL },
    { memberUuid: "m-2", memberName: "Bình Trần", amount: 80000, image: DATA_URL },
  ];
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

describe("useExpenseMemberQrsQuery", () => {
  it("UseExpenseMemberQrsQuery_Disabled_NeverFiresTheRequest", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr/members", () => {
        gets += 1;
        return ok(members());
      }),
    );
    function Probe() {
      useExpenseMemberQrsQuery("e-1", undefined, false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await new Promise((r) => setTimeout(r, 30));
    expect(gets).toBe(0);
  });

  it("UseExpenseMemberQrsQuery_Enabled_ResolvesAMemberQrList", async () => {
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr/members", () => ok(members())),
    );
    const captured: { q?: ReturnType<typeof useExpenseMemberQrsQuery> } = {};
    function Probe() {
      captured.q = useExpenseMemberQrsQuery("e-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isSuccess).toBe(true));
    // The unwrapped payload is the MemberQrResponse[] (data URLs, not a blob).
    expect(captured.q?.data).toHaveLength(2);
    expect(captured.q?.data?.[0]).toMatchObject({
      memberUuid: "m-1",
      memberName: "An Nguyễn",
      amount: 120000,
    });
    expect(captured.q?.data?.[0].image.startsWith("data:image/png")).toBe(true);
  });

  it("UseExpenseMemberQrsQuery_DefaultAccount_OmitsTheBankAccountUuidQuery", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr/members", ({ request }) => {
        seenUrl = request.url;
        return ok(members());
      }),
    );
    function Probe() {
      // `undefined` bankAccountUuid → the implicit default account (no override).
      useExpenseMemberQrsQuery("e-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.has("bankAccountUuid")).toBe(false);
  });

  it("UseExpenseMemberQrsQuery_NonDefaultDestination_SendsBankAccountUuidQuery", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr/members", ({ request }) => {
        seenUrl = request.url;
        return ok(members());
      }),
    );
    function Probe() {
      useExpenseMemberQrsQuery("e-1", "ba-override", true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("bankAccountUuid")).toBe(
      "ba-override",
    );
  });

  it("UseExpenseMemberQrsQuery_TerminalError_DoesNotRetry", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/expenses/:uuid/qr/members", () => {
        gets += 1;
        return fail(13003, "Premium.", 403);
      }),
    );
    const captured: { q?: ReturnType<typeof useExpenseMemberQrsQuery> } = {};
    function Probe() {
      captured.q = useExpenseMemberQrsQuery("e-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isError).toBe(true));
    // retry:false → the queryFn ran exactly once (no transient re-attempts).
    expect(gets).toBe(1);
  });
});

describe("useEventMemberQrsQuery", () => {
  it("UseEventMemberQrsQuery_Disabled_NeverFiresTheRequest", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/events/:uuid/qr/members", () => {
        gets += 1;
        return ok(members());
      }),
    );
    function Probe() {
      useEventMemberQrsQuery("ev-1", undefined, false);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await new Promise((r) => setTimeout(r, 30));
    expect(gets).toBe(0);
  });

  it("UseEventMemberQrsQuery_Enabled_ResolvesAMemberQrList", async () => {
    server.use(
      http.get("*/api/v1/events/:uuid/qr/members", () => ok(members())),
    );
    const captured: { q?: ReturnType<typeof useEventMemberQrsQuery> } = {};
    function Probe() {
      captured.q = useEventMemberQrsQuery("ev-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isSuccess).toBe(true));
    expect(captured.q?.data).toHaveLength(2);
    expect(captured.q?.data?.[1].memberName).toBe("Bình Trần");
  });

  it("UseEventMemberQrsQuery_NonDefaultDestination_SendsBankAccountUuidQuery", async () => {
    let seenUrl = "";
    server.use(
      http.get("*/api/v1/events/:uuid/qr/members", ({ request }) => {
        seenUrl = request.url;
        return ok(members());
      }),
    );
    function Probe() {
      useEventMemberQrsQuery("ev-1", "ba-override", true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(seenUrl).not.toBe(""));
    expect(new URL(seenUrl).searchParams.get("bankAccountUuid")).toBe(
      "ba-override",
    );
  });

  it("UseEventMemberQrsQuery_TerminalError_DoesNotRetry", async () => {
    let gets = 0;
    server.use(
      http.get("*/api/v1/events/:uuid/qr/members", () => {
        gets += 1;
        return fail(12003, "Không còn ai nợ.", 400);
      }),
    );
    const captured: { q?: ReturnType<typeof useEventMemberQrsQuery> } = {};
    function Probe() {
      captured.q = useEventMemberQrsQuery("ev-1", undefined, true);
      return null;
    }
    renderWithProviders(<Probe />, { queryClient });

    await waitFor(() => expect(captured.q?.isError).toBe(true));
    expect(gets).toBe(1);
  });
});
