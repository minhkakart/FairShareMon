import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { ExpenseDetailPage } from "./pages/ExpenseDetailPage";
import type { ExpenseResponse } from "./api/types";

/**
 * ExpenseDetailPage integration — the REAL detail route/hooks against MSW. GET
 * /expenses/:uuid is stubbed with a canned ExpenseResponse (the per-user store is
 * empty); the shares breakdown, audit section, and write-control guards render off
 * it. Ownership 6000 → the shared NotFound view (R1, no existence leak); a closed
 * event disables every write control except the settled toggle (R4).
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

const UUID = "e-detail";

let userSeq = 0;
function seedSession(): string {
  userSeq += 1;
  const username = `dtest${userSeq}`;
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: `access-${username}-t`,
    accessTokenExpiresAt: future,
    refreshToken: `refresh-${username}-t`,
    refreshTokenExpiresAt: future,
    user: { username, tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
  return username;
}

function makeExpense(overrides: Partial<ExpenseResponse> = {}): ExpenseResponse {
  return {
    uuid: UUID,
    name: "Thuê xe",
    description: "Đi Đà Lạt",
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 300000,
    category: {
      uuid: "c-1",
      name: "Đi lại",
      color: "#3B82F6",
      icon: "🚗",
      isDefault: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    payer: {
      uuid: "m-1",
      name: "An Nguyễn",
      isOwnerRepresentative: false,
      isDeleted: false,
      createdAt: "2026-01-01T00:00:00+00:00",
    },
    isSettled: false,
    settledAt: null,
    shares: [
      {
        uuid: "s-owner",
        member: {
          uuid: "m-owner",
          name: "Bạn (chủ sổ)",
          isOwnerRepresentative: true,
          isDeleted: false,
          createdAt: "2026-01-01T00:00:00+00:00",
        },
        amount: 0,
        note: null,
        createdAt: "2026-07-16T03:00:00+00:00",
      },
      {
        uuid: "s-1",
        member: {
          uuid: "m-1",
          name: "An Nguyễn",
          isOwnerRepresentative: false,
          isDeleted: false,
          createdAt: "2026-01-01T00:00:00+00:00",
        },
        amount: 300000,
        note: "Cả nhóm",
        createdAt: "2026-07-16T03:00:00+00:00",
      },
    ],
    tags: [{ uuid: "t-1", name: "Du lịch", isDeleted: false, createdAt: "2026-01-01T00:00:00+00:00" }],
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-16T03:00:00+00:00",
    ...overrides,
  };
}

function renderDetail() {
  return renderWithProviders(
    <Routes>
      <Route path="/expenses/:uuid" element={<ExpenseDetailPage />} />
    </Routes>,
    { initialPath: `/expenses/${UUID}`, queryClient },
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  queryClient.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("ExpenseDetailPage read view", () => {
  it("ExpenseDetailPage_Loaded_RendersInfoSharesAndTotal", async () => {
    server.use(http.get(`*/api/v1/expenses/${UUID}`, () => ok(makeExpense())));
    renderDetail();

    expect(
      await screen.findByRole("heading", { level: 1, name: "Thuê xe" }),
    ).toBeInTheDocument();
    // Info list.
    expect(screen.getByText("Đi Đà Lạt")).toBeInTheDocument();
    expect(screen.getByText("Du lịch")).toBeInTheDocument();
    // Shares table: owner-rep + member rows with the derived total.
    const sharesTable = screen.getByRole("table", {
      name: "Phần gánh của phiếu",
    });
    expect(
      within(sharesTable).getByRole("rowheader", { name: /Bạn \(chủ sổ\)/ }),
    ).toBeInTheDocument();
    expect(
      within(sharesTable).getByRole("rowheader", { name: "An Nguyễn" }),
    ).toBeInTheDocument();
    expect(within(sharesTable).getAllByText(/300\.000/).length).toBeGreaterThanOrEqual(1);
  });

  it("ExpenseDetailPage_AuditSection_RendersEmptyStateWhenNoHistory", async () => {
    server.use(http.get(`*/api/v1/expenses/${UUID}`, () => ok(makeExpense())));
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });
    // Global history handler returns [] for a canned (foreign) uuid.
    expect(
      await screen.findByText("Chưa có thay đổi nào."),
    ).toBeInTheDocument();
  });

  it("ExpenseDetailPage_Ownership6000_RendersSharedNotFoundView", async () => {
    server.use(
      http.get(`*/api/v1/expenses/${UUID}`, () =>
        fail(6000, "Không tìm thấy phiếu chi tiêu.", 404),
      ),
    );
    renderDetail();
    // R1: the ownership miss renders the shared not-found (never leaks existence),
    // NOT an expense-specific error.
    expect(
      await screen.findByRole("heading", { name: "Không tìm thấy" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Thuê xe" }),
    ).not.toBeInTheDocument();
  });

  it("ExpenseDetailPage_ServerError_ShowsRetryNotNotFound", async () => {
    server.use(
      http.get(`*/api/v1/expenses/${UUID}`, () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    renderDetail();
    expect(
      await screen.findByText("Không tải được phiếu chi tiêu"),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Không tìm thấy" }),
    ).not.toBeInTheDocument();
  });

  it("ExpenseDetailPage_DeletedLinkedMemberAndCategory_ShowDeletedTag", async () => {
    server.use(
      http.get(`*/api/v1/expenses/${UUID}`, () =>
        ok(
          makeExpense({
            payer: {
              uuid: "m-1",
              name: "An Nguyễn",
              isOwnerRepresentative: false,
              isDeleted: true,
              createdAt: "2026-01-01T00:00:00+00:00",
            },
            category: {
              uuid: "c-1",
              name: "Đi lại",
              color: "#3B82F6",
              icon: "🚗",
              isDefault: false,
              isDeleted: true,
              createdAt: "2026-01-01T00:00:00+00:00",
            },
          }),
        ),
      ),
    );
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });
    // Historical soft-deleted links stay visible with the "(đã xóa)" treatment.
    expect(screen.getAllByText("(đã xóa)").length).toBeGreaterThanOrEqual(2);
  });
});

describe("ExpenseDetailPage closed-event guard (R4)", () => {
  it("ExpenseDetailPage_ClosedEvent_DisablesWritesButKeepsSettledToggle", async () => {
    server.use(
      http.get(`*/api/v1/expenses/${UUID}`, () =>
        ok(
          makeExpense({
            eventUuid: "ev-1",
            eventName: "Đà Lạt",
            eventIsClosed: true,
          }),
        ),
      ),
    );
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    // Edit + Delete are disabled…
    expect(screen.getByRole("button", { name: "Sửa" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Xóa" })).toBeDisabled();
    // …the closed-event notice is shown…
    expect(screen.getByText("Đợt đã chốt")).toBeInTheDocument();
    // …the shares add control is gone (read-only)…
    expect(
      screen.queryByRole("button", { name: "Thêm phần gánh" }),
    ).not.toBeInTheDocument();
    // …but the settled toggle stays enabled (the one allowed write).
    expect(
      screen.getByRole("switch", { name: "Trạng thái đã trả của Thuê xe" }),
    ).toBeEnabled();
    // …and export stays available.
    expect(screen.getByRole("button", { name: "Xuất CSV" })).toBeEnabled();
  });
});

describe("ExpenseDetailPage QR action (M7-MOD)", () => {
  it("ExpenseDetailPage_ShowQrButton_IsPresentAndEnabled", async () => {
    server.use(http.get(`*/api/v1/expenses/${UUID}`, () => ok(makeExpense())));
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    expect(screen.getByRole("button", { name: "Xem mã QR" })).toBeEnabled();
  });

  it("ExpenseDetailPage_ShowQrEnabledOnClosedEvent_QrIsAReadAllowedWrite", async () => {
    server.use(
      http.get(`*/api/v1/expenses/${UUID}`, () =>
        ok(
          makeExpense({
            eventUuid: "ev-1",
            eventName: "Đà Lạt",
            eventIsClosed: true,
          }),
        ),
      ),
    );
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    // Edit/Delete are disabled on a closed event, but QR (a read) stays enabled.
    expect(screen.getByRole("button", { name: "Sửa" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Xem mã QR" })).toBeEnabled();
  });

  it("ExpenseDetailPage_ClickShowQr_OpensTheQrDialog", async () => {
    server.use(http.get(`*/api/v1/expenses/${UUID}`, () => ok(makeExpense())));
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Thuê xe" });

    await user.click(screen.getByRole("button", { name: "Xem mã QR" }));

    // The shared QrDialog opens with the expense QR title (Free session → the
    // dialog handles the gate internally; here we only prove the wiring).
    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByRole("heading", { name: "Mã QR chuyển khoản" }),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole("dialog")).toBeInTheDocument(),
    );
  });
});
