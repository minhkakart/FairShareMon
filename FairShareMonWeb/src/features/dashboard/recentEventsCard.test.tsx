import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { queryClient } from "@/lib/query/queryClient";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { RecentEventsCard } from "./components/RecentEventsCard";
import type { EventSummaryResponse } from "@/features/events/api/types";

/**
 * RecentEventsCard (F3) — the REAL card + `useEventsQuery({})` + the pure
 * `sortEventsForDashboard` helper against MSW at the client boundary. GET
 * /events is overridden per-test with canned summaries. Assertions target
 * accessible roles/names (row `Link`s, "view all"), the vi-VN status-badge copy,
 * localized ranges (viewer TZ +07 pinned in setup), and VND grouping — never
 * internal state. Ordering is exercised at the boundary (open-first, updatedAt
 * DESC) and capped at 5.
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
function seedSession(): void {
  userSeq += 1;
  const username = `recard${userSeq}`;
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
}

function makeSummary(
  overrides: Partial<EventSummaryResponse> = {},
): EventSummaryResponse {
  return {
    uuid: "ev-x",
    name: "Đợt",
    startDate: "2026-07-12T00:00:00+07:00",
    endDate: "2026-07-18T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 0,
    createdAt: "2026-07-01T00:00:00+00:00",
    totalAdvanced: 0,
    updatedAt: "2026-07-12T00:00:00+00:00",
    ...overrides,
  };
}

// 3 open + 3 closed. Sorted: open (updatedAt DESC) then closed (updatedAt DESC):
// [o-b, o-c, o-a, c-a, c-b, c-c]; sliced to 5 → c-c ("Trại hè cũ") is dropped.
const MIXED: EventSummaryResponse[] = [
  makeSummary({ uuid: "o-a", name: "Đà Lạt", isClosed: false, updatedAt: "2026-07-10T00:00:00Z", totalAdvanced: 1500000 }),
  makeSummary({ uuid: "c-a", name: "Nha Trang", isClosed: true, closedAt: "2026-07-18T00:00:00Z", updatedAt: "2026-07-18T00:00:00Z", totalAdvanced: 2400000 }),
  makeSummary({ uuid: "o-b", name: "Sa Pa", isClosed: false, updatedAt: "2026-07-19T00:00:00Z", totalAdvanced: 990000 }),
  makeSummary({ uuid: "c-b", name: "Phú Quốc", isClosed: true, closedAt: "2026-07-12T00:00:00Z", updatedAt: "2026-07-12T00:00:00Z", totalAdvanced: 750000 }),
  makeSummary({ uuid: "o-c", name: "Hội An", isClosed: false, updatedAt: "2026-07-15T00:00:00Z", totalAdvanced: 300000 }),
  makeSummary({ uuid: "c-c", name: "Trại hè cũ", isClosed: true, closedAt: "2026-07-05T00:00:00Z", updatedAt: "2026-07-05T00:00:00Z", totalAdvanced: 120000 }),
];

function renderCard() {
  return renderWithProviders(<RecentEventsCard />, {
    initialPath: "/dashboard",
    queryClient,
  });
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

describe("RecentEventsCard states", () => {
  it("RecentEventsCard_Loading_ShowsSkeletonNotEmptyOrRows", () => {
    server.use(
      http.get("*/api/v1/events", async () => {
        await delay(50);
        return ok(MIXED);
      }),
    );
    const { container } = renderCard();

    // The header renders immediately; the pending branch shows skeletons only —
    // no empty state, no rows, no error while the query is in flight.
    expect(screen.getByText("Đợt gần đây")).toBeInTheDocument();
    expect(screen.queryByText("Chưa có đợt nào")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(
      container.querySelectorAll("span[aria-hidden='true']").length,
    ).toBeGreaterThan(0);
    // No event-detail links yet.
    expect(
      screen.queryByRole("link", { name: /Sa Pa/ }),
    ).not.toBeInTheDocument();
  });

  it("RecentEventsCard_EmptyList_ShowsNoEventsEmptyState", async () => {
    server.use(http.get("*/api/v1/events", () => ok([])));
    renderCard();
    expect(await screen.findByText("Chưa có đợt nào")).toBeInTheDocument();
  });

  it("RecentEventsCard_ListError_ShowsErrorStateThenRetryRecovers", async () => {
    let calls = 0;
    server.use(
      http.get("*/api/v1/events", () => {
        calls += 1;
        if (calls === 1) return fail(1000, "Đã xảy ra lỗi máy chủ.", 500);
        return ok(MIXED);
      }),
    );
    const user = userEvent.setup();
    renderCard();

    // The section-level error is announced (role=alert) with the backend message.
    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Đã xảy ra lỗi máy chủ.")).toBeInTheDocument();
    await user.click(within(alert).getByRole("button", { name: "Thử lại" }));
    // Retry refetches and the list renders.
    expect(await screen.findByRole("link", { name: /Sa Pa/ })).toBeInTheDocument();
  });
});

describe("RecentEventsCard list", () => {
  it("RecentEventsCard_PopulatedList_RendersOrderedTop5WithNameRangeBadgeAndMoney", async () => {
    server.use(http.get("*/api/v1/events", () => ok(MIXED)));
    renderCard();

    await screen.findByRole("link", { name: /Sa Pa/ });

    // Order: open-first (updatedAt DESC) then closed (updatedAt DESC), capped 5.
    const rows = screen
      .getAllByRole("link")
      .filter((a) => a.getAttribute("href")?.startsWith("/events/"));
    expect(rows.map((a) => a.getAttribute("href"))).toEqual([
      "/events/o-b",
      "/events/o-c",
      "/events/o-a",
      "/events/c-a",
      "/events/c-b",
    ]);

    // The 6th (oldest closed) is sliced off.
    expect(screen.queryByRole("link", { name: /Trại hè cũ/ })).not.toBeInTheDocument();

    // A representative open row: name + localized range (+07) + open badge + VND.
    const saPa = screen.getByRole("link", { name: /Sa Pa/ });
    expect(within(saPa).getByText("Đang mở")).toBeInTheDocument();
    expect(within(saPa).getByText(/990\.000/)).toBeInTheDocument();
    expect(within(saPa).getByText(/12/)).toBeInTheDocument();
    expect(within(saPa).getByText(/18/)).toBeInTheDocument();

    // A closed row carries the closed badge + its own money.
    const nhaTrang = screen.getByRole("link", { name: /Nha Trang/ });
    expect(within(nhaTrang).getByText("Đã chốt")).toBeInTheDocument();
    expect(within(nhaTrang).getByText(/2\.400\.000/)).toBeInTheDocument();
  });

  it("RecentEventsCard_CapsAtFiveRows_WhenMoreEventsExist", async () => {
    server.use(http.get("*/api/v1/events", () => ok(MIXED)));
    renderCard();
    await screen.findByRole("link", { name: /Sa Pa/ });

    const rows = screen
      .getAllByRole("link")
      .filter((a) => a.getAttribute("href")?.startsWith("/events/"));
    expect(rows).toHaveLength(5);
  });

  it("RecentEventsCard_ViewAllLink_RoutesToEventsIndex", async () => {
    server.use(http.get("*/api/v1/events", () => ok(MIXED)));
    renderCard();
    await screen.findByRole("link", { name: /Sa Pa/ });

    expect(screen.getByRole("link", { name: "Xem tất cả" })).toHaveAttribute(
      "href",
      "/events",
    );
  });
});

describe("RecentEventsCard i18n", () => {
  it("RecentEventsCard_EnUsLocale_RendersEnglishChromeAndBadges", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    server.use(http.get("*/api/v1/events", () => ok(MIXED)));
    renderCard();

    // Wait for the list to resolve before asserting the localized chrome/badges.
    const saPa = await screen.findByRole("link", { name: /Sa Pa/ });
    expect(screen.getByText("Recent events")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View all" })).toHaveAttribute(
      "href",
      "/events",
    );
    expect(within(saPa).getByText("Open")).toBeInTheDocument();
    const nhaTrang = screen.getByRole("link", { name: /Nha Trang/ });
    expect(within(nhaTrang).getByText("Closed")).toBeInTheDocument();
  });

  it("RecentEventsCard_EnUsLocale_EmptyStateUsesEnglishCopy", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    server.use(http.get("*/api/v1/events", () => ok([])));
    renderCard();
    expect(await screen.findByText("No events yet")).toBeInTheDocument();
  });
});
