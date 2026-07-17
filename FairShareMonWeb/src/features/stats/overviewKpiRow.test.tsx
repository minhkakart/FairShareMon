import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatMoneyVnd } from "@/i18n/format";
import i18n from "@/i18n";
import { OverviewKpiRow } from "./components/OverviewKpiRow";
import type { OverviewStatsResponse } from "./api/types";

/** VND rendered with a non-breaking space; the DOM normalizer collapses it to a
 *  regular space, so normalize the expected string to match `getByText`. */
const vnd = (n: number) => formatMoneyVnd(n).replace(/\s+/g, " ");

/**
 * OverviewKpiRow — the two-tile KPI row. Presentational: rendered directly with
 * props (its data comes from `useOverviewQuery` in the page, tested there). Proves
 * total spending renders via `<Money>` as the EXACT API number (vi-VN VND
 * grouping) with NO derived "average" tile (R3, no float math); the count renders
 * grouped; loading → skeleton tiles that keep the labels; error → a compact
 * ErrorState with retry; a zero range → `0` tiles (valid data, not an empty state).
 */

const DATA: OverviewStatsResponse = {
  from: "2026-07-01T00:00:00.000Z",
  to: "2026-07-31T23:59:59.999Z",
  totalSpending: 1250000,
  expenseCount: 1234,
};

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("OverviewKpiRow", () => {
  it("OverviewKpiRow_WithData_RendersTotalAsExactVndAndGroupedCount", () => {
    renderWithProviders(<OverviewKpiRow data={DATA} />);

    // Total spending: the EXACT API number via the shared VND formatter.
    expect(screen.getByText("Tổng chi tiêu")).toBeInTheDocument();
    expect(screen.getByText(vnd(1250000))).toBeInTheDocument();
    // Proves vi-VN grouping specifically (dot thousands separator).
    expect(screen.getByText(/1\.250\.000/)).toBeInTheDocument();

    // Expense count: grouped via formatCount.
    expect(screen.getByText("Số phiếu chi tiêu")).toBeInTheDocument();
    expect(screen.getByText("1.234")).toBeInTheDocument();
  });

  it("OverviewKpiRow_WithData_ShowsNoDerivedAverageTile", () => {
    renderWithProviders(<OverviewKpiRow data={DATA} />);

    // The forbidden float-math figure (1250000 / 1234 ≈ 1013) must never appear…
    const average = Math.round(DATA.totalSpending / DATA.expenseCount);
    expect(screen.queryByText(vnd(average))).not.toBeInTheDocument();
    // …and there is no average label at all.
    expect(screen.queryByText(/trung bình/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/average/i)).not.toBeInTheDocument();
  });

  it("OverviewKpiRow_ZeroRange_RendersZeroTilesNotAnEmptyState", () => {
    const zero: OverviewStatsResponse = {
      from: null,
      to: null,
      totalSpending: 0,
      expenseCount: 0,
    };
    renderWithProviders(<OverviewKpiRow data={zero} />);

    // Zeros are valid data — the tiles render `0`, not an empty/placeholder state.
    expect(screen.getByText("Tổng chi tiêu")).toBeInTheDocument();
    expect(screen.getByText(vnd(0))).toBeInTheDocument();
    expect(screen.getByText("0")).toBeInTheDocument();
  });

  it("OverviewKpiRow_Loading_ShowsSkeletonTilesKeepingLabels", () => {
    const { container } = renderWithProviders(<OverviewKpiRow loading />);

    // Labels stay so the layout does not jump; values are skeleton placeholders.
    expect(screen.getByText("Tổng chi tiêu")).toBeInTheDocument();
    expect(screen.getByText("Số phiếu chi tiêu")).toBeInTheDocument();
    // No real values yet.
    expect(screen.queryByText(/1\.250\.000/)).not.toBeInTheDocument();
    // One skeleton placeholder per tile (aria-hidden).
    const skeletons = container.querySelectorAll('span[aria-hidden="true"]');
    expect(skeletons.length).toBeGreaterThanOrEqual(2);
  });

  it("OverviewKpiRow_Error_RendersCompactErrorStateWithRetry", async () => {
    const onRetry = vi.fn();
    renderWithProviders(
      <OverviewKpiRow error="Máy chủ gặp sự cố." onRetry={onRetry} />,
    );

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Không tải được số liệu tổng quan");
    expect(alert).toHaveTextContent("Máy chủ gặp sự cố.");

    await userEvent.click(screen.getByRole("button", { name: "Thử lại" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
