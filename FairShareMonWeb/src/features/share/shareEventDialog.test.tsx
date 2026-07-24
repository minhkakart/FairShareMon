import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ShareEventDialog } from "./components/ShareEventDialog";
import type { ShareLinkResponse } from "./api/types";
import type { EventResponse } from "@/features/events/api/types";

/**
 * ShareEventDialog — the owner-side Premium-gated Share dialog against the REAL
 * hooks + client + MSW. Covers: the Premium gate (proactive Free + reactive 403
 * 13003, both suppressing the create call); the create form (bank preselected)
 * and the QR-less no-bank path with a /wallet hint; the active-link view
 * (URL + copy-to-{origin}/share/{token} + absolute expiry + bank snapshot); the
 * two-step inline-confirm Revoke and Regenerate (OQ3); the expired-link state
 * (OQ4); open-event 16001 → warning; and ownership 404 (9000) → close + toast.
 * Uses the app singleton queryClient so mutation invalidations refetch, mirroring
 * qrDialog.test. Pinned vi-VN + Asia/Ho_Chi_Minh.
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

const ACCOUNTS = [
  {
    uuid: "ba-def",
    bankBin: "970436",
    bankName: "Vietcombank",
    accountNumber: "0071001234567",
    accountHolderName: "NGUYEN VAN MINH",
    isDefault: true,
    createdAt: "2026-01-01T00:00:00+00:00",
  },
  {
    uuid: "ba-alt",
    bankBin: "970407",
    bankName: "Techcombank",
    accountNumber: "19024681012345",
    accountHolderName: "NGUYEN VAN MINH",
    isDefault: false,
    createdAt: "2026-01-02T00:00:00+00:00",
  },
];

const EVENT: EventResponse = {
  uuid: "ev-share",
  name: "Chuyến Đà Lạt",
  description: null,
  startDate: "2026-07-01T00:00:00+07:00",
  endDate: "2026-07-05T23:59:59+07:00",
  isClosed: true,
  closedAt: "2026-07-20T10:00:00+00:00",
  expenseCount: 2,
  createdAt: "2026-07-01T00:00:00+00:00",
};

function activeLink(over: Partial<ShareLinkResponse> = {}): ShareLinkResponse {
  return {
    token: "tok-123",
    expiresAt: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
    createdAt: new Date().toISOString(),
    hasQr: true,
    bankName: "Vietcombank",
    accountNumber: "0071001234567",
    accountHolderName: "NGUYEN VAN MINH",
    ...over,
  };
}

function seedSession(tier: "FREE" | "PREMIUM") {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-sed-tok",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-sed-tok",
    refreshTokenExpiresAt: future,
    user: { username: "sed", tier, role: "USER" },
    profileStatus: "resolved",
  });
}

function renderDialog(onOpenChange: (open: boolean) => void = () => {}) {
  return renderWithProviders(
    <ShareEventDialog open onOpenChange={onOpenChange} event={EVENT} />,
    { queryClient },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  vi.restoreAllMocks();
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("ShareEventDialog premium gate", () => {
  it("ShareEventDialog_FreeUser_ShowsUpgradePromptAndNeverFiresQuery", async () => {
    seedSession("FREE");
    let getCalls = 0;
    server.use(
      http.get("*/api/v1/events/:uuid/share", () => {
        getCalls += 1;
        return ok(null);
      }),
    );
    renderDialog();

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Tính năng Premium")).toBeInTheDocument();
    // Proactive gate: the active-link query is never enabled for a Free user.
    await new Promise((r) => setTimeout(r, 40));
    expect(getCalls).toBe(0);
    // No create form for a Free user.
    expect(
      screen.queryByRole("button", { name: "Tạo liên kết" }),
    ).not.toBeInTheDocument();
  });

  it("ShareEventDialog_StaleTier403_ReactivelyShowsUpgradePrompt", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () =>
        fail(13003, "Tính năng này chỉ dành cho Premium.", 403),
      ),
    );
    renderDialog();

    const dialog = await screen.findByRole("dialog");
    expect(
      await within(dialog).findByText("Tính năng Premium"),
    ).toBeInTheDocument();
  });
});

describe("ShareEventDialog create form", () => {
  it("ShareEventDialog_PremiumNoLink_ShowsCreateFormWithDefaultBankPreselected", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () => ok(null)),
    );
    renderDialog();

    // The destination picker + create button (no active link yet).
    const picker = await screen.findByRole("combobox", {
      name: "Tài khoản nhận tiền",
    });
    // Default account (Vietcombank) is preselected in the trigger.
    expect(picker).toHaveTextContent("Vietcombank");
    expect(
      screen.getByRole("button", { name: "Tạo liên kết" }),
    ).toBeInTheDocument();
  });

  it("ShareEventDialog_NoBankAccount_ShowsWalletHintAndCreatesQrLessLink", async () => {
    seedSession("PREMIUM");
    let postBody: Record<string, unknown> | null = null;
    let current: ShareLinkResponse | null = null;
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok([])),
      http.get("*/api/v1/events/:uuid/share", () => ok(current)),
      http.post("*/api/v1/events/:uuid/share", async ({ request }) => {
        postBody = (await request.json()) as Record<string, unknown>;
        current = activeLink({ token: "noqr", hasQr: false, bankName: null, accountNumber: null, accountHolderName: null });
        return ok(current);
      }),
    );
    const user = userEvent.setup();
    renderDialog();

    // No account → the /wallet hint (no picker), but create is still allowed.
    expect(
      await screen.findByText("Chưa có tài khoản nhận tiền"),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Tới Ví" })).toHaveAttribute(
      "href",
      "/wallet",
    );
    expect(
      screen.queryByRole("combobox", { name: "Tài khoản nhận tiền" }),
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Tạo liên kết" }));

    // Created without a destination (QR-less) — no bankAccountUuid in the body.
    await waitFor(() => expect(postBody).not.toBeNull());
    expect(postBody).not.toHaveProperty("bankAccountUuid");
    // The link view now renders with the no-QR hint.
    expect(
      await screen.findByText(/không kèm mã QR/),
    ).toBeInTheDocument();
  });
});

describe("ShareEventDialog active link view", () => {
  it("ShareEventDialog_ActiveLink_ShowsUrlCopyExpiryBankRevokeRegenerate", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () => ok(activeLink())),
    );
    renderDialog();

    const urlField = await screen.findByRole("textbox", {
      name: "Liên kết chia sẻ",
    });
    expect((urlField as HTMLInputElement).value).toContain("/share/tok-123");
    expect(
      screen.getByRole("button", { name: "Sao chép liên kết" }),
    ).toBeInTheDocument();
    // Absolute expiry (formatDateTime) + bank snapshot.
    expect(screen.getByText(/Hết hạn lúc/)).toBeInTheDocument();
    expect(screen.getByText("Vietcombank")).toBeInTheDocument();
    // Both destructive actions available.
    expect(screen.getByRole("button", { name: "Thu hồi" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tạo lại" })).toBeInTheDocument();
  });

  it("ShareEventDialog_Copy_WritesOriginShareTokenToClipboard", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () => ok(activeLink())),
    );
    const user = userEvent.setup();
    const writeSpy = vi.spyOn(navigator.clipboard, "writeText");
    renderDialog();
    await screen.findByRole("textbox", { name: "Liên kết chia sẻ" });

    await user.click(screen.getByRole("button", { name: "Sao chép liên kết" }));

    expect(writeSpy).toHaveBeenCalledTimes(1);
    expect(writeSpy.mock.calls[0][0]).toBe(
      `${window.location.origin}/share/tok-123`,
    );
    // Confirms only on a successful write.
    expect(await screen.findByText("Đã sao chép")).toBeInTheDocument();
  });

  it("ShareEventDialog_ExpiredLink_ShowsExpiredStateNotUrlField", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () =>
        ok(activeLink({ expiresAt: "2020-01-01T00:00:00.000Z" })),
      ),
    );
    renderDialog();

    expect(await screen.findByText("Liên kết đã hết hạn")).toBeInTheDocument();
    // The copy-able URL field is suppressed once expired; regenerate remains.
    expect(
      screen.queryByRole("textbox", { name: "Liên kết chia sẻ" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Tạo lại" })).toBeInTheDocument();
  });
});

describe("ShareEventDialog destructive actions (inline confirm, OQ3)", () => {
  it("ShareEventDialog_Revoke_TwoStepConfirm_CallsDeleteAndToasts", async () => {
    seedSession("PREMIUM");
    let deleteCalls = 0;
    let current: ShareLinkResponse | null = activeLink();
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () => ok(current)),
      http.delete("*/api/v1/events/:uuid/share", () => {
        deleteCalls += 1;
        current = null;
        return ok({ message: "Đã thu hồi liên kết chia sẻ." });
      }),
    );
    const user = userEvent.setup();
    renderDialog();
    await screen.findByRole("textbox", { name: "Liên kết chia sẻ" });

    // First click reveals the inline confirm — DELETE not yet fired.
    await user.click(screen.getByRole("button", { name: "Thu hồi" }));
    expect(screen.getByText(/Thu hồi liên kết\?/)).toBeInTheDocument();
    expect(deleteCalls).toBe(0);

    // Confirm fires the DELETE + success toast.
    await user.click(screen.getByRole("button", { name: "Xác nhận thu hồi" }));
    await waitFor(() => expect(deleteCalls).toBe(1));
    expect(
      await screen.findByText("Đã thu hồi liên kết chia sẻ."),
    ).toBeInTheDocument();
  });

  it("ShareEventDialog_Regenerate_TwoStepConfirm_PostsRegenerateTrue", async () => {
    seedSession("PREMIUM");
    let postBody: Record<string, unknown> | null = null;
    let current: ShareLinkResponse | null = activeLink({ token: "old" });
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () => ok(current)),
      http.post("*/api/v1/events/:uuid/share", async ({ request }) => {
        postBody = (await request.json()) as Record<string, unknown>;
        current = activeLink({ token: "new" });
        return ok(current);
      }),
    );
    const user = userEvent.setup();
    renderDialog();
    await screen.findByRole("textbox", { name: "Liên kết chia sẻ" });

    await user.click(screen.getByRole("button", { name: "Tạo lại" }));
    // Inline confirm; POST not yet fired.
    expect(screen.getByText(/Tạo lại liên kết\?/)).toBeInTheDocument();
    expect(postBody).toBeNull();

    await user.click(screen.getByRole("button", { name: "Xác nhận tạo lại" }));

    await waitFor(() => expect(postBody).not.toBeNull());
    expect(postBody).toMatchObject({ regenerate: true });
    expect(
      await screen.findByText("Đã tạo lại liên kết chia sẻ."),
    ).toBeInTheDocument();
  });
});

describe("ShareEventDialog error branches", () => {
  it("ShareEventDialog_OpenEvent16001_ShowsWarningAlert", async () => {
    seedSession("PREMIUM");
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () =>
        fail(16001, "Chỉ có thể chia sẻ sau khi chốt đợt.", 400),
      ),
    );
    renderDialog();

    expect(await screen.findByText("Đợt chưa được chốt")).toBeInTheDocument();
  });

  it("ShareEventDialog_Ownership9000_ClosesWithDangerToast_NoLeak", async () => {
    seedSession("PREMIUM");
    const onOpenChange = vi.fn();
    server.use(
      http.get("*/api/v1/bank-accounts", () => ok(ACCOUNTS)),
      http.get("*/api/v1/events/:uuid/share", () =>
        fail(9000, "Không tìm thấy đợt chi tiêu.", 404),
      ),
    );
    renderDialog(onOpenChange);

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    // The ownership miss is toasted verbatim (no existence leak); closed once.
    expect(
      await screen.findByText("Không tìm thấy đợt chi tiêu."),
    ).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 40));
    expect(onOpenChange).toHaveBeenCalledTimes(1);
  });
});
