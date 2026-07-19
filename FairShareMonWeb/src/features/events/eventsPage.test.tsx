import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useLocation } from "react-router-dom";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { EventsPage } from "./pages/EventsPage";
import type { EventSummaryResponse } from "./api/types";

/**
 * EventsPage integration — the REAL page/table/filter/hooks against MSW at the
 * client boundary. The per-user events store starts empty, so row tests override
 * GET /events with canned summaries; the status filter test captures the refetch
 * URLs to prove both the ?status= URL state and the ?closed= server refetch.
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

let userSeq = 0;
function seedSession(): string {
  userSeq += 1;
  const username = `evpage${userSeq}`;
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

function makeSummary(
  overrides: Partial<EventSummaryResponse> = {},
): EventSummaryResponse {
  return {
    uuid: "ev-1",
    name: "Đà Lạt",
    startDate: "2026-07-12T00:00:00+07:00",
    endDate: "2026-07-18T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 3,
    createdAt: "2026-07-01T00:00:00+00:00",
    totalAdvanced: 1500000,
    updatedAt: "2026-07-12T00:00:00+00:00",
    ...overrides,
  };
}

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="loc-search">{location.search}</div>;
}

function renderEvents(initialPath = "/events") {
  return renderWithProviders(
    <Routes>
      <Route
        path="/events"
        element={
          <>
            <EventsPage />
            <LocationProbe />
          </>
        }
      />
      <Route path="/events/:uuid" element={<div>DETAIL</div>} />
    </Routes>,
    { initialPath, queryClient },
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

describe("EventsPage states", () => {
  it("EventsPage_Loading_ShowsSkeletonRows", () => {
    server.use(
      http.get("*/api/v1/events", async () => {
        await delay(50);
        return ok([]);
      }),
    );
    renderEvents();
    const rowHeaders = screen.getAllByRole("rowheader");
    expect(rowHeaders).toHaveLength(5);
    expect(rowHeaders.every((c) => c.textContent === "")).toBe(true);
  });

  it("EventsPage_EmptyStore_ShowsNoEventsEmptyState", async () => {
    server.use(http.get("*/api/v1/events", () => ok([])));
    renderEvents();
    expect(
      await screen.findByText("Chưa có đợt chi tiêu nào"),
    ).toBeInTheDocument();
  });

  it("EventsPage_ActiveFilterNoMatches_ShowsNoMatchesEmptyState", async () => {
    server.use(http.get("*/api/v1/events", () => ok([])));
    renderEvents("/events?status=closed");
    // A filter is active but nothing matches → the no-matches (not empty) state.
    expect(
      await screen.findByText("Không có đợt nào khớp bộ lọc"),
    ).toBeInTheDocument();
  });

  it("EventsPage_ListError_ShowsErrorStateThenRetryRecovers", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/events", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok([makeSummary()]);
      }),
    );
    const user = userEvent.setup();
    renderEvents();

    expect(
      await screen.findByText("Không tải được danh sách đợt chi tiêu"),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Thử lại" }));
    expect(
      await screen.findByRole("rowheader", { name: "Đà Lạt" }),
    ).toBeInTheDocument();
  });
});

describe("EventsPage rows", () => {
  it("EventsPage_Row_RendersRangeStatusBadgeAndExpenseCount", async () => {
    server.use(http.get("*/api/v1/events", () => ok([makeSummary()])));
    renderEvents();

    const row = (
      await screen.findByRole("rowheader", { name: "Đà Lạt" })
    ).closest("tr") as HTMLElement;

    // Date range (rendered in the +07 viewer zone), the open status badge, and count.
    expect(within(row).getByText(/12/)).toBeInTheDocument();
    expect(within(row).getByText(/18/)).toBeInTheDocument();
    expect(within(row).getByText("Đang mở")).toBeInTheDocument();
    expect(within(row).getByText("3")).toBeInTheDocument();
  });

  it("EventsPage_ClosedEvent_RendersClosedBadge", async () => {
    server.use(
      http.get("*/api/v1/events", () =>
        ok([makeSummary({ isClosed: true, closedAt: "2026-07-20T00:00:00+00:00" })]),
      ),
    );
    renderEvents();
    const row = (
      await screen.findByRole("rowheader", { name: "Đà Lạt" })
    ).closest("tr") as HTMLElement;
    expect(within(row).getByText("Đã chốt")).toBeInTheDocument();
  });

  it("EventsPage_NameCell_LinksToDetailRoute", async () => {
    server.use(http.get("*/api/v1/events", () => ok([makeSummary()])));
    renderEvents();
    const link = await screen.findByRole("link", { name: "Đà Lạt" });
    expect(link).toHaveAttribute("href", "/events/ev-1");
  });
});

describe("EventsPage status filter", () => {
  it("EventsPage_ClosedFilter_UpdatesUrlAndRefetchesWithClosedTrue", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/events", ({ request }) => {
        urls.push(request.url);
        return ok([]);
      }),
    );
    const user = userEvent.setup();
    renderEvents();
    await waitFor(() => expect(urls.length).toBeGreaterThanOrEqual(1));

    await user.click(screen.getByRole("combobox", { name: "Trạng thái" }));
    await user.click(await screen.findByRole("option", { name: "Đã chốt" }));

    // The refetch carries closed=true…
    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("closed") === "true"),
      ).toBe(true),
    );
    // …and the state is reflected in the URL (shareable / back-friendly).
    expect(screen.getByTestId("loc-search").textContent).toContain(
      "status=closed",
    );
  });

  it("EventsPage_OpenFilterFromUrl_RefetchesWithClosedFalse", async () => {
    const urls: string[] = [];
    server.use(
      http.get("*/api/v1/events", ({ request }) => {
        urls.push(request.url);
        return ok([]);
      }),
    );
    renderEvents("/events?status=open");
    await waitFor(() =>
      expect(
        urls.some((u) => new URL(u).searchParams.get("closed") === "false"),
      ).toBe(true),
    );
  });

  it("EventsPage_ClearFilters_RemovesUrlState", async () => {
    server.use(http.get("*/api/v1/events", () => ok([])));
    const user = userEvent.setup();
    renderEvents("/events?status=closed");

    const clear = await screen.findByRole("button", { name: "Xóa lọc" });
    expect(clear).toBeEnabled();
    await user.click(clear);
    await waitFor(() =>
      expect(screen.getByTestId("loc-search").textContent).toBe(""),
    );
  });
});

describe("EventsPage i18n", () => {
  it("EventsPage_EnUsLocale_RendersEnglishChrome", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    server.use(http.get("*/api/v1/events", () => ok([])));
    renderEvents();
    expect(
      await screen.findByRole("heading", { level: 1, name: "Events" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Add event" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("No events yet")).toBeInTheDocument();
  });
});
