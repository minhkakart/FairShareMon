import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { CategoryBreakdown } from "./components/CategoryBreakdown";
import type { ByCategoryStatsResponse } from "./api/types";

/**
 * CategoryBreakdown — composes the decorative chart + the accessible table, and
 * owns the loading / empty / error states. Proves each state renders the right
 * surface: loading → skeleton bars (no chart region, no table); empty rows →
 * EmptyState; error → ErrorState with a working retry; success → BOTH the
 * `role="img"` chart and its paired data table.
 */

const DATA: ByCategoryStatsResponse = {
  eventUuid: null,
  from: "2026-07-01T00:00:00.000Z",
  to: "2026-07-31T23:59:59.999Z",
  rows: [
    { categoryUuid: "c-1", categoryName: "Ăn uống", color: "#F97316", icon: "🍜", isDeleted: false, total: 500000, expenseCount: 5 },
    { categoryUuid: "c-2", categoryName: "Đi lại", color: "#3B82F6", icon: "🚗", isDeleted: false, total: 250000, expenseCount: 3 },
  ],
};
const EMPTY: ByCategoryStatsResponse = {
  eventUuid: null,
  from: DATA.from,
  to: DATA.to,
  rows: [],
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

describe("CategoryBreakdown", () => {
  it("CategoryBreakdown_Loading_ShowsSkeletonBarsNotChartOrTable", () => {
    const { container } = renderWithProviders(
      <CategoryBreakdown loading overviewTotal={0} />,
    );
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    // Skeleton placeholders present (aria-hidden).
    expect(
      container.querySelectorAll('[aria-hidden="true"]').length,
    ).toBeGreaterThan(0);
  });

  it("CategoryBreakdown_EmptyRows_ShowsEmptyState", () => {
    renderWithProviders(
      <CategoryBreakdown data={EMPTY} overviewTotal={0} overviewCount={0} />,
    );
    expect(
      screen.getByText("Chưa có chi tiêu trong khoảng này"),
    ).toBeInTheDocument();
    // Not a chart/table — nothing to show.
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("CategoryBreakdown_Error_ShowsErrorStateAndRetries", async () => {
    const onRetry = vi.fn();
    renderWithProviders(
      <CategoryBreakdown
        error="Máy chủ gặp sự cố."
        overviewTotal={0}
        onRetry={onRetry}
      />,
    );
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Không tải được thống kê theo danh mục");
    expect(alert).toHaveTextContent("Máy chủ gặp sự cố.");

    await userEvent.click(screen.getByRole("button", { name: "Thử lại" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("CategoryBreakdown_Success_RendersBothChartAndTable", () => {
    renderWithProviders(
      <CategoryBreakdown
        data={DATA}
        overviewTotal={1000000}
        overviewCount={8}
      />,
    );
    // The decorative chart…
    expect(screen.getByRole("img", { name: /Ăn uống/ })).toBeInTheDocument();
    // …and the accessible data table both render.
    expect(
      screen.getByRole("table", { name: "Chi tiêu theo danh mục" }),
    ).toBeInTheDocument();
  });
});
