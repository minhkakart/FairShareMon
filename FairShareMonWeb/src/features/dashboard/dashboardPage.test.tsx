import { beforeEach, afterEach, describe, expect, it } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { DashboardPage } from "./pages/DashboardPage";
import { sessionStore } from "@/lib/auth/session";
import type { ProfileStatus, SessionUser } from "@/lib/auth/session";
import i18n from "@/i18n";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatMoneyVnd } from "@/i18n/format";
import type {
  ByCategoryStatsResponse,
  OverviewStatsResponse,
} from "@/features/stats/api/types";
import type { ExpenseSummaryResponse } from "@/features/expenses/api/types";

/** VND renders with a non-breaking space the DOM normalizer collapses. */
const vnd = (n: number) => formatMoneyVnd(n).replace(/\s+/g, " ");

/**
 * Rich home (M6). A welcome greeting, a this-month KPI row, a compact category
 * breakdown, a recent-expenses card + quick actions, and the role-filtered
 * quick-link cards from `useNavEntries` (dashboard tile excluded, admin tile
 * admin-only). The this-month overview/by-category + `GET /expenses` queries run
 * against the MSW mock backend, whose per-user store is empty here — so the data
 * panels resolve to their zero/empty states, and this file focuses on structure +
 * role filtering. (Fuller data-panel coverage is the web-test-engineer's Step 10.)
 *
 * `access-demo-1` is a mock-parseable token (username = `demo`) so the queries
 * resolve 200 (empty) rather than 401 → refresh → session-clear.
 */

function setSession(
  user: SessionUser | null,
  profileStatus: ProfileStatus = "resolved",
) {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-demo-1",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-demo-1",
    refreshTokenExpiresAt: future,
    user,
    profileStatus,
  });
}

beforeEach(async () => {
  window.localStorage.clear();
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
});

afterEach(async () => {
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
  window.localStorage.clear();
});

describe("DashboardPage home", () => {
  it("Dashboard_UserResolved_ShowsWelcomeGreetingWithUsername", async () => {
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    expect(
      await screen.findByRole("heading", { level: 1, name: "Chào demo" }),
    ).toBeInTheDocument();
  });

  it("Dashboard_NoUsername_ShowsGenericGreeting", async () => {
    setSession(null, "pending");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    expect(
      await screen.findByRole("heading", { level: 1, name: "Chào mừng bạn" }),
    ).toBeInTheDocument();
  });

  it("Dashboard_UserResolved_RendersThisMonthOverviewAndQuickLinks", async () => {
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // This-month overview header + KPI labels.
    expect(await screen.findByText("Tháng này")).toBeInTheDocument();
    expect(screen.getByText("Tổng chi tiêu")).toBeInTheDocument();
    expect(screen.getByText("Số phiếu chi tiêu")).toBeInTheDocument();

    // One quick-link card (a link) per visible non-dashboard area.
    for (const [label, href] of [
      ["Thành viên", "/members"],
      ["Danh mục", "/categories"],
      ["Nhãn", "/tags"],
      ["Chi tiêu", "/expenses"],
      ["Đợt", "/events"],
      ["Thống kê", "/stats"],
      ["Ví", "/wallet"],
    ] as const) {
      expect(
        screen.getByRole("link", { name: new RegExp(label) }),
      ).toHaveAttribute("href", href);
    }
    // The dashboard tile itself is excluded (you're already home).
    expect(
      screen.queryByRole("link", { name: /Tổng quan/ }),
    ).not.toBeInTheDocument();
    // Admin tile is hidden for a USER.
    expect(
      screen.queryByRole("link", { name: /Quản trị/ }),
    ).not.toBeInTheDocument();

    // Quick actions.
    expect(
      screen.getByRole("link", { name: "Thêm phiếu chi tiêu" }),
    ).toHaveAttribute("href", "/expenses/new");
  });

  it("Dashboard_AdminResolved_ShowsAdminQuickLinkCard", async () => {
    setSession({ username: "root", role: "ADMIN" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    const adminLink = await screen.findByRole("link", { name: /Quản trị/ });
    expect(adminLink).toHaveAttribute("href", "/admin");
  });

  it("Dashboard_EnUsLocale_RendersEnglishGreetingAndCards", async () => {
    window.localStorage.setItem("fsm.locale", "en-US");
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // LocaleProvider syncs i18n on mount; en-US copy resolves.
    expect(
      await screen.findByRole("heading", { level: 1, name: "Welcome, demo" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: /Members/ }),
    ).toHaveAttribute("href", "/members");
  });
});

// ─── Rich home data panels (M6, fuller coverage) ──────────────────────────────

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

function overview(overrides: Partial<OverviewStatsResponse> = {}): OverviewStatsResponse {
  return {
    from: null,
    to: null,
    totalSpending: 1250000,
    expenseCount: 8,
    ...overrides,
  };
}

const BREAKDOWN_ROWS: ByCategoryStatsResponse["rows"] = [
  { categoryUuid: "c-1", categoryName: "Ăn uống", color: "#F97316", icon: "🍜", isDeleted: false, total: 600000, expenseCount: 6 },
  { categoryUuid: "c-2", categoryName: "Đi lại", color: "#3B82F6", icon: "🚗", isDeleted: false, total: 300000, expenseCount: 3 },
  { categoryUuid: "c-3", categoryName: "Giải trí", color: "#8B5CF6", icon: "🎮", isDeleted: false, total: 150000, expenseCount: 2 },
  { categoryUuid: "c-4", categoryName: "Mua sắm", color: "#10B981", icon: "🛍️", isDeleted: false, total: 120000, expenseCount: 2 },
  { categoryUuid: "c-5", categoryName: "Sức khỏe", color: "#EF4444", icon: "💊", isDeleted: false, total: 80000, expenseCount: 1 },
  { categoryUuid: "c-6", categoryName: "Khác", color: "#6B7280", icon: "❓", isDeleted: false, total: 40000, expenseCount: 1 },
];

function summary(overrides: Partial<ExpenseSummaryResponse> = {}): ExpenseSummaryResponse {
  return {
    uuid: "e-1",
    name: "Phiếu",
    expenseTime: "2026-07-16T03:00:00+00:00",
    total: 100000,
    category: {
      uuid: "c-1",
      name: "Ăn uống",
      color: "#F97316",
      icon: "🍜",
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
    shareCount: 1,
    eventUuid: null,
    eventName: null,
    eventIsClosed: null,
    createdAt: "2026-07-16T03:00:00+00:00",
    ...overrides,
  };
}

// Six expenses already in the API's expenseTime-DESC order (the card slices the
// top 5 VERBATIM — it must not re-sort).
const RECENT_DESC: ExpenseSummaryResponse[] = [
  summary({ uuid: "e-1", name: "Ăn tối nhà hàng", total: 500000, expenseTime: "2026-07-16T12:00:00+00:00" }),
  summary({ uuid: "e-2", name: "Taxi sân bay", total: 250000, expenseTime: "2026-07-15T09:00:00+00:00" }),
  summary({ uuid: "e-3", name: "Vé xem phim", total: 180000, expenseTime: "2026-07-14T20:00:00+00:00" }),
  summary({ uuid: "e-4", name: "Cà phê sáng", total: 60000, expenseTime: "2026-07-13T02:00:00+00:00" }),
  summary({ uuid: "e-5", name: "Mua sách", total: 120000, expenseTime: "2026-07-12T05:00:00+00:00" }),
  summary({ uuid: "e-6", name: "Phiếu cũ nhất", total: 30000, expenseTime: "2026-07-01T05:00:00+00:00" }),
];

function useDataHandlers() {
  server.use(
    http.get("*/api/v1/stats/overview", () => ok(overview())),
    http.get("*/api/v1/stats/by-category", () =>
      ok({ eventUuid: null, from: null, to: null, rows: BREAKDOWN_ROWS }),
    ),
    http.get("*/api/v1/expenses", () => ok(RECENT_DESC)),
  );
}

describe("DashboardPage home — data panels", () => {
  it("Dashboard_ThisMonthKpi_RendersTotalMoneyAndCount", async () => {
    useDataHandlers();
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    expect(await screen.findByText("Tháng này")).toBeInTheDocument();
    // Total via <Money> (exact API value) + grouped count.
    expect(await screen.findByText(vnd(1250000))).toBeInTheDocument();
    expect(screen.getByText("8")).toBeInTheDocument();
    // The overview header links to the full Stats page.
    expect(screen.getByRole("link", { name: "Xem thống kê" })).toHaveAttribute(
      "href",
      "/stats",
    );
  });

  it("Dashboard_CompactBreakdown_ShowsTop5BarsAndLinksToStats", async () => {
    useDataHandlers();
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // The compact chart region renders (top categories, led by Ăn uống)…
    expect(
      await screen.findByRole("img", { name: /Ăn uống/ }),
    ).toBeInTheDocument();
    // …the top-5 categories show, but the 6th is sliced off.
    expect(screen.getByText("Sức khỏe")).toBeInTheDocument();
    expect(screen.queryByText("Khác")).not.toBeInTheDocument();
    // "Xem tất cả" links to the full Stats page.
    const viewAll = screen.getAllByRole("link", { name: "Xem tất cả" });
    expect(viewAll.some((l) => l.getAttribute("href") === "/stats")).toBe(true);
  });

  it("Dashboard_RecentExpenses_ShowsTop5DescVerbatimEachLinkingToDetail", async () => {
    useDataHandlers();
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // Each of the top 5 links to its own expense detail…
    const first = await screen.findByRole("link", { name: /Ăn tối nhà hàng/ });
    expect(first).toHaveAttribute("href", "/expenses/e-1");
    expect(screen.getByRole("link", { name: /Taxi sân bay/ })).toHaveAttribute(
      "href",
      "/expenses/e-2",
    );
    expect(screen.getByRole("link", { name: /Mua sách/ })).toHaveAttribute(
      "href",
      "/expenses/e-5",
    );
    // …the 6th (oldest) is sliced off.
    expect(
      screen.queryByRole("link", { name: /Phiếu cũ nhất/ }),
    ).not.toBeInTheDocument();

    // Order is verbatim (expenseTime DESC as delivered) — not re-sorted.
    const list = first.closest("a")!.parentElement!;
    const text = list.textContent ?? "";
    expect(text.indexOf("Ăn tối nhà hàng")).toBeLessThan(text.indexOf("Taxi sân bay"));
    expect(text.indexOf("Taxi sân bay")).toBeLessThan(text.indexOf("Mua sách"));
  });

  it("Dashboard_QuickActions_LinkToCreateExpenseAndEvents", async () => {
    useDataHandlers();
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    expect(
      await screen.findByRole("link", { name: "Thêm phiếu chi tiêu" }),
    ).toHaveAttribute("href", "/expenses/new");
    expect(screen.getByRole("link", { name: "Tạo đợt mới" })).toHaveAttribute(
      "href",
      "/events",
    );
  });

  it("Dashboard_EmptyLedger_ShowsBreakdownAndRecentEmptyStates", async () => {
    server.use(
      http.get("*/api/v1/stats/overview", () =>
        ok(overview({ totalSpending: 0, expenseCount: 0 })),
      ),
      http.get("*/api/v1/stats/by-category", () =>
        ok({ eventUuid: null, from: null, to: null, rows: [] }),
      ),
      http.get("*/api/v1/expenses", () => ok([])),
    );
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // KPI zeros (valid) + both panels' empty states.
    expect(await screen.findByText(vnd(0))).toBeInTheDocument();
    expect(screen.getByText("Chưa có chi tiêu tháng này")).toBeInTheDocument();
    expect(screen.getByText("Chưa có phiếu chi tiêu nào")).toBeInTheDocument();
    // Quick actions remain available on the empty ledger.
    expect(
      screen.getByRole("link", { name: "Thêm phiếu chi tiêu" }),
    ).toBeInTheDocument();
  });

  it("Dashboard_RecentExpensesError_ShowsErrorStateWithRetry", async () => {
    server.use(
      http.get("*/api/v1/stats/overview", () => ok(overview())),
      http.get("*/api/v1/stats/by-category", () =>
        ok({ eventUuid: null, from: null, to: null, rows: BREAKDOWN_ROWS }),
      ),
      http.get("*/api/v1/expenses", () => fail(1000, "Đã có lỗi xảy ra.", 500)),
    );
    setSession({ username: "demo", role: "USER" }, "resolved");
    renderWithProviders(<DashboardPage />, { initialPath: "/dashboard" });

    // The recent-activity card surfaces the backend error message + a retry.
    await waitFor(() => {
      const alerts = screen.getAllByRole("alert");
      const recentAlert = alerts.find((a) =>
        within(a).queryByText("Đã có lỗi xảy ra."),
      );
      expect(recentAlert).toBeDefined();
      expect(
        within(recentAlert as HTMLElement).getByRole("button", { name: "Thử lại" }),
      ).toBeInTheDocument();
    });
  });
});
