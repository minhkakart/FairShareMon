import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes } from "react-router-dom";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { EventDetailPage } from "@/features/events/pages/EventDetailPage";
import type { EventResponse } from "@/features/events/api/types";

/**
 * Event-detail Share affordance — the "Chia sẻ" button is closed-events-only
 * (mirrors the settlement-QR button's `closed ?` guard) and opens the
 * ShareEventDialog. Driven through the real detail route + hooks against MSW.
 * Pinned vi-VN + Asia/Ho_Chi_Minh.
 */

interface Envelope {
  data: unknown;
  isSuccess: boolean;
  error: { code: number; message: string } | null;
}
function ok(data: unknown) {
  return HttpResponse.json<Envelope>({ data, isSuccess: true, error: null });
}

const UUID = "ev-detail-share";

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-eds-tok",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-eds-tok",
    refreshTokenExpiresAt: future,
    user: { username: "eds", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

function makeEvent(over: Partial<EventResponse> = {}): EventResponse {
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
    ...over,
  };
}

function balancePayload(isClosed: boolean) {
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
        outstanding: 0,
        isSettled: false,
        settledAt: null,
      },
    ],
    totalOutstanding: 0,
    owingMemberCount: 0,
    settledMemberCount: 0,
  };
}

function stubDetail(event: EventResponse) {
  server.use(
    http.get(`*/api/v1/events/${UUID}/balance`, () =>
      ok(balancePayload(event.isClosed)),
    ),
    http.get(`*/api/v1/events/${UUID}`, () => ok(event)),
    http.get("*/api/v1/expenses", () => ok([])),
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

describe("EventDetailPage Share affordance", () => {
  it("EventDetailShare_OpenEvent_HidesShareButton", async () => {
    stubDetail(makeEvent());
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Đà Lạt" });

    // Closed-only: no Share action while the event is open.
    expect(
      screen.queryByRole("button", { name: "Chia sẻ" }),
    ).not.toBeInTheDocument();
  });

  it("EventDetailShare_ClosedEvent_ShowsShareButton", async () => {
    stubDetail(makeEvent({ isClosed: true, closedAt: "2026-07-20T10:00:00+00:00" }));
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Đà Lạt" });

    expect(screen.getByRole("button", { name: "Chia sẻ" })).toBeEnabled();
  });

  it("EventDetailShare_ClickShare_OpensShareDialog", async () => {
    stubDetail(makeEvent({ isClosed: true, closedAt: "2026-07-20T10:00:00+00:00" }));
    const user = userEvent.setup();
    renderDetail();
    await screen.findByRole("heading", { level: 1, name: "Đà Lạt" });

    await user.click(screen.getByRole("button", { name: "Chia sẻ" }));

    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByRole("heading", { name: "Chia sẻ báo cáo quyết toán" }),
    ).toBeInTheDocument();
  });
});
