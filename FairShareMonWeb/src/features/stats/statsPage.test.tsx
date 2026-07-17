import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { delay, http, HttpResponse } from "msw";
import { server } from "@/test/msw/server";
import { renderWithProviders } from "@/test/utils";
import { sessionStore } from "@/lib/auth/session";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatMoneyVnd } from "@/i18n/format";
import i18n from "@/i18n";
import { StatsPage } from "./pages/StatsPage";
import type {
  ByCategoryStatsResponse,
  OverviewStatsResponse,
} from "./api/types";

/** VND renders with a non-breaking space the DOM normalizer collapses. */
const vnd = (n: number) => formatMoneyVnd(n).replace(/\s+/g, " ");

/**
 * StatsPage integration — the REAL page + range control + hooks + centralized
 * client against MSW at the boundary. The per-user MSW store isn't seeded; every
 * test overrides the two stats endpoints with canned fixtures (and captures the
 * request URLs) so behavior is deterministic. Proves: success → KPI row + chart +
 * table; loading → skeletons; changing a preset refetches with new `from`/`to`;
 * empty scope → EmptyState in the breakdown while KPI tiles show zeros; a `1001`
 * surfaces the range-control inline message; a generic error → ErrorState; and an
 * inverted custom range is blocked client-side (never reaches the API).
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

function overview(overrides: Partial<OverviewStatsResponse> = {}): OverviewStatsResponse {
  return {
    from: null,
    to: null,
    totalSpending: 1250000,
    expenseCount: 8,
    ...overrides,
  };
}
function byCategory(rows: ByCategoryStatsResponse["rows"] = DEFAULT_ROWS): ByCategoryStatsResponse {
  return { eventUuid: null, from: null, to: null, rows };
}
const DEFAULT_ROWS: ByCategoryStatsResponse["rows"] = [
  { categoryUuid: "c-1", categoryName: "Ăn uống", color: "#F97316", icon: "🍜", isDeleted: false, total: 1000000, expenseCount: 5 },
  { categoryUuid: "c-2", categoryName: "Đi lại", color: "#3B82F6", icon: "🚗", isDeleted: false, total: 250000, expenseCount: 3 },
];

function seedSession() {
  const future = new Date(Date.now() + 3_600_000).toISOString();
  sessionStore.setState({
    status: "authenticated",
    accessToken: "access-statspage-t",
    accessTokenExpiresAt: future,
    refreshToken: "refresh-statspage-t",
    refreshTokenExpiresAt: future,
    user: { username: "statspage", tier: "FREE", role: "USER" },
    profileStatus: "resolved",
  });
}

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
  seedSession();
});

afterEach(async () => {
  sessionStore.getState().clearSession();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("StatsPage — success composition", () => {
  it("StatsPage_Success_RendersKpiRowChartAndTable", async () => {
    server.use(
      http.get("*/api/v1/stats/overview", () => ok(overview())),
      http.get("*/api/v1/stats/by-category", () => ok(byCategory())),
    );
    renderWithProviders(<StatsPage />);

    // The overview total appears twice: the KPI tile AND the table footer echo.
    await screen.findByRole("table", { name: "Chi tiêu theo danh mục" });
    expect(screen.getAllByText(vnd(1250000)).length).toBeGreaterThanOrEqual(2);
    // The expense count likewise shows in the KPI tile and the footer echo.
    expect(screen.getAllByText("8").length).toBeGreaterThanOrEqual(2);
    // The decorative chart region…
    expect(screen.getByRole("img", { name: /Ăn uống/ })).toBeInTheDocument();
    // …and the accessible data table.
    expect(
      screen.getByRole("table", { name: "Chi tiêu theo danh mục" }),
    ).toBeInTheDocument();
  });

  it("StatsPage_WhileLoading_ShowsSkeletonsBeforeData", async () => {
    server.use(
      http.get("*/api/v1/stats/overview", async () => {
        await delay(40);
        return ok(overview());
      }),
      http.get("*/api/v1/stats/by-category", async () => {
        await delay(40);
        return ok(byCategory());
      }),
    );
    const { container } = renderWithProviders(<StatsPage />);

    // Before the data resolves: labels present, values still skeleton.
    expect(screen.getByText("Tổng chi tiêu")).toBeInTheDocument();
    expect(screen.queryAllByText(vnd(1250000))).toHaveLength(0);
    expect(container.querySelectorAll('[aria-hidden="true"]').length).toBeGreaterThan(0);

    // Then it resolves (KPI tile + table footer echo).
    await screen.findByRole("table", { name: "Chi tiêu theo danh mục" });
    expect(screen.getAllByText(vnd(1250000)).length).toBeGreaterThanOrEqual(2);
  });
});

describe("StatsPage — range drives refetch", () => {
  it("StatsPage_ChangePresetToAllTime_RefetchesWithoutBounds", async () => {
    const overviewUrls: string[] = [];
    server.use(
      http.get("*/api/v1/stats/overview", ({ request }) => {
        overviewUrls.push(request.url);
        return ok(overview());
      }),
      http.get("*/api/v1/stats/by-category", () => ok(byCategory())),
    );
    renderWithProviders(<StatsPage />);

    // Initial "This month" fetch carries a from bound.
    await waitFor(() => expect(overviewUrls.length).toBeGreaterThanOrEqual(1));
    expect(new URL(overviewUrls[0]).searchParams.has("from")).toBe(true);

    // Switch to All time → a fresh fetch with NO bounds.
    await userEvent.click(screen.getByRole("button", { name: "Tất cả" }));
    await waitFor(() => expect(overviewUrls.length).toBeGreaterThanOrEqual(2));
    const latest = new URL(overviewUrls[overviewUrls.length - 1]).searchParams;
    expect(latest.has("from")).toBe(false);
    expect(latest.has("to")).toBe(false);
  });
});

describe("StatsPage — empty / error branches", () => {
  it("StatsPage_EmptyScope_ShowsZeroKpisAndBreakdownEmptyState", async () => {
    server.use(
      http.get("*/api/v1/stats/overview", () =>
        ok(overview({ totalSpending: 0, expenseCount: 0 })),
      ),
      http.get("*/api/v1/stats/by-category", () => ok(byCategory([]))),
    );
    renderWithProviders(<StatsPage />);

    // KPI tiles show valid zeros (not an empty state)…
    expect(await screen.findByText(vnd(0))).toBeInTheDocument();
    // …while the breakdown shows its empty state.
    expect(
      screen.getByText("Chưa có chi tiêu trong khoảng này"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("StatsPage_BadRange1001_SurfacesTheInlineRangeControlMessage", async () => {
    server.use(
      http.get("*/api/v1/stats/overview", () =>
        fail(1001, "Khoảng thời gian không hợp lệ.", 400),
      ),
      http.get("*/api/v1/stats/by-category", () =>
        fail(1001, "Khoảng thời gian không hợp lệ.", 400),
      ),
    );
    renderWithProviders(<StatsPage />);

    // The 1001 message surfaces on the range control (verbatim), not as a panel error.
    const group = screen.getByRole("group", { name: "Khoảng thời gian" });
    await waitFor(() =>
      expect(
        within(group).getByText("Khoảng thời gian không hợp lệ."),
      ).toBeInTheDocument(),
    );
  });

  it("StatsPage_GenericError_ShowsPanelErrorStates", async () => {
    server.use(
      http.get("*/api/v1/stats/overview", () =>
        fail(1000, "Đã có lỗi xảy ra.", 500),
      ),
      http.get("*/api/v1/stats/by-category", () =>
        fail(1000, "Đã có lỗi xảy ra.", 500),
      ),
    );
    renderWithProviders(<StatsPage />);

    // Both panels render an ErrorState (role=alert) with the backend message.
    await waitFor(() => {
      const alerts = screen.getAllByRole("alert");
      expect(alerts.length).toBeGreaterThanOrEqual(2);
    });
    expect(screen.getAllByText("Đã có lỗi xảy ra.").length).toBeGreaterThanOrEqual(1);
  });
});

describe("StatsPage — invalid custom range is blocked client-side", () => {
  it("StatsPage_InvertedCustomRange_ShowsInlineMessageAndSendsNoInvertedRequest", async () => {
    const overviewUrls: string[] = [];
    server.use(
      http.get("*/api/v1/stats/overview", ({ request }) => {
        overviewUrls.push(request.url);
        return ok(overview());
      }),
      http.get("*/api/v1/stats/by-category", () => ok(byCategory())),
    );
    renderWithProviders(<StatsPage />);
    await waitFor(() => expect(overviewUrls.length).toBeGreaterThanOrEqual(1));

    // Enter Custom, then invert the two bounds.
    await userEvent.click(screen.getByRole("button", { name: "Tùy chỉnh" }));
    fireEvent.change(screen.getByLabelText("Từ ngày"), {
      target: { value: "2026-03-20" },
    });
    fireEvent.change(screen.getByLabelText("Đến ngày"), {
      target: { value: "2026-03-05" },
    });

    // The inline invalid message is announced on the `to` field…
    expect(
      await screen.findByText("“Đến ngày” phải sau hoặc bằng “Từ ngày”."),
    ).toBeInTheDocument();

    // …and the inverted bounds NEVER reach the API (client-side guard).
    // 2026-03-20 → 2026-03-19T17:00:00.000Z (+07); 2026-03-05 → 2026-03-05T16:59:59.999Z.
    const sentInverted = overviewUrls.some((u) => {
      const p = new URL(u).searchParams;
      return (
        p.get("from") === "2026-03-19T17:00:00.000Z" &&
        p.get("to") === "2026-03-05T16:59:59.999Z"
      );
    });
    expect(sentInverted).toBe(false);
  });
});
