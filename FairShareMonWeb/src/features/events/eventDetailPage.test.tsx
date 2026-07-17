import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { EventDetailPage } from "./pages/EventDetailPage";
import type { EventResponse } from "./api/types";

/**
 * EventDetailPage integration — the REAL detail route/hooks against MSW. GET
 * /events/:uuid, /events/:uuid/balance, and /expenses?eventUuid= are stubbed. An
 * ownership miss (code 9000) → the shared NotFound (R1, no existence leak); a
 * closed event disables Edit/Delete/Close, shows the closed Alert, keeps Export,
 * and renders the expenses section read-only. The balance renders for open AND
 * closed events (OQ8a).
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

const UUID = "ev-detail";

let userSeq = 0;
function seedSession(): string {
  userSeq += 1;
  const username = `evd${userSeq}`;
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

function makeEvent(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    uuid: UUID,
    name: "Đà Lạt",
    description: "Chuyến đi công ty",
    startDate: "2026-07-12T00:00:00+07:00",
    endDate: "2026-07-18T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 1,
    createdAt: "2026-07-01T00:00:00+00:00",
    ...overrides,
  };
}

function balancePayload(isClosed = false) {
  return {
    eventUuid: UUID,
    eventName: "Đà Lạt",
    isClosed,
    rows: [
      {
        memberUuid: "m-1",
        memberName: "An Nguyễn",
        isOwnerRepresentative: false,
        isDeleted: false,
        advanced: 300000,
        owed: 100000,
        balance: 200000,
      },
      {
        memberUuid: "m-2",
        memberName: "Bình Trần",
        isOwnerRepresentative: false,
        isDeleted: false,
        advanced: 0,
        owed: 200000,
        balance: -200000,
      },
    ],
  };
}

function inEventExpense() {
  return {
    uuid: "e-1",
    name: "Thuê xe",
    expenseTime: "2026-07-14T03:00:00+00:00",
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
    tagNames: [],
    shareCount: 2,
    eventUuid: UUID,
    eventName: "Đà Lạt",
    eventIsClosed: false,
    createdAt: "2026-07-14T03:00:00+00:00",
  };
}

function stubDetail(event: EventResponse, isClosed = false) {
  server.use(
    http.get(`*/api/v1/events/${UUID}/balance`, () => ok(balancePayload(isClosed))),
    http.get(`*/api/v1/events/${UUID}`, () => ok(event)),
    http.get("*/api/v1/expenses", () => ok([inEventExpense()])),
  );
}

function renderDetail() {
  return renderWithProviders(
    <Routes>
      <Route path="/events/:uuid" element={<EventDetailPage />} />
    </Routes>,
    { initialPath: `/events/${UUID}`, queryClient },
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

describe("EventDetailPage read view", () => {
  it("EventDetailPage_Loaded_RendersHeaderInfoBalanceAndExpenses", async () => {
    stubDetail(makeEvent());
    renderDetail();

    expect(
      await screen.findByRole("heading", { level: 1, name: "Đà Lạt" }),
    ).toBeInTheDocument();
    // Info card.
    expect(screen.getByText("Chuyến đi công ty")).toBeInTheDocument();
    // Balance table renders its member rows (rowheaders only exist once loaded —
    // the pending skeleton has empty rowheaders, so this also waits past it).
    expect(
      await screen.findByRole("rowheader", { name: /An Nguyễn/ }),
    ).toBeInTheDocument();
    // The event's expenses section lists the in-event expense.
    expect(
      await screen.findByRole("link", { name: "Thuê xe" }),
    ).toBeInTheDocument();
  });

  it("EventDetailPage_Ownership9000_RendersSharedNotFoundView", async () => {
    server.use(
      http.get(`*/api/v1/events/${UUID}`, () =>
        fail(9000, "Không tìm thấy đợt chi tiêu.", 404),
      ),
    );
    renderDetail();
    // R1: the ownership miss renders the shared not-found — never an event-specific
    // error, never leaking existence.
    expect(
      await screen.findByRole("heading", { name: "Không tìm thấy" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Đà Lạt" }),
    ).not.toBeInTheDocument();
  });

  it("EventDetailPage_ServerError_ShowsRetryNotNotFound", async () => {
    server.use(
      http.get(`*/api/v1/events/${UUID}`, () =>
        fail(1000, "Đã xảy ra lỗi máy chủ.", 500),
      ),
    );
    renderDetail();
    expect(
      await screen.findByText("Không tải được đợt chi tiêu"),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Không tìm thấy" }),
    ).not.toBeInTheDocument();
  });
});

describe("EventDetailPage open-event controls", () => {
  it("EventDetailPage_OpenEvent_EnablesEditCloseDeleteExportAndAssign", async () => {
    stubDetail(makeEvent());
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Đà Lạt" });

    expect(screen.getByRole("button", { name: "Sửa" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Chốt đợt" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Xóa" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Xuất CSV" })).toBeEnabled();
    // The expenses-section assign picker trigger is present when open.
    expect(
      await screen.findByRole("button", { name: "Gán phiếu" }),
    ).toBeInTheDocument();
    // No closed alert on an open event.
    expect(screen.queryByText("Đợt đã chốt")).not.toBeInTheDocument();
  });
});

describe("EventDetailPage closed-event immutability (R4)", () => {
  it("EventDetailPage_ClosedEvent_HidesWritesShowsClosedAlertKeepsExportAndBalance", async () => {
    stubDetail(
      makeEvent({ isClosed: true, closedAt: "2026-07-20T10:00:00+00:00" }),
      true,
    );
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Đà Lạt" });

    // Write controls are gone (open-only)…
    expect(screen.queryByRole("button", { name: "Sửa" })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Chốt đợt" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Xóa" })).not.toBeInTheDocument();
    // …the closed notice is shown…
    expect(screen.getByText("Đợt đã chốt")).toBeInTheDocument();
    // …Export stays available…
    expect(screen.getByRole("button", { name: "Xuất CSV" })).toBeEnabled();
    // …the expenses section is read-only (no assign / remove controls)…
    expect(
      screen.queryByRole("button", { name: "Gán phiếu" }),
    ).not.toBeInTheDocument();
    // …and the balance still renders (its member rows) for a closed event (OQ8a).
    expect(
      await screen.findByRole("rowheader", { name: /An Nguyễn/ }),
    ).toBeInTheDocument();
  });
});
