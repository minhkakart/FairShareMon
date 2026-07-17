import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatMoneyVnd } from "@/i18n/format";
import i18n from "@/i18n";
import { CategoryBarChart } from "./components/CategoryBarChart";
import type { CategoryStatRow } from "./api/types";

/** VND renders with a non-breaking space the DOM normalizer collapses. */
const vnd = (n: number) => formatMoneyVnd(n).replace(/\s+/g, " ");

/**
 * CategoryBarChart — the hand-rolled ranked bar breakdown (OQ1a/OQ2a).
 * Presentational: rendered directly with rows in the API's total-DESC order.
 * Proves: rows render VERBATIM (no client re-sort); the region is `role="img"`
 * with a summarizing accessible name while the bars are `aria-hidden` (the paired
 * table carries the data for AT); each bar ships a direct label (marker + name +
 * `<Money>` value + % share) so identity never rests on the fill color
 * (color-independence / relief rule); bar length = total/maxTotal and % share =
 * total/overviewTotal are display-only ratios; the bar FILL uses `--fs-viz-cat-1..8`
 * by rank and folds a 9th+ row to the muted neutral; deleted categories keep their
 * slot with `(đã xóa)`.
 */

function row(overrides: Partial<CategoryStatRow>): CategoryStatRow {
  return {
    categoryUuid: "c-x",
    categoryName: "Danh mục",
    color: "#3B82F6",
    icon: null,
    isDeleted: false,
    total: 100000,
    expenseCount: 1,
    ...overrides,
  };
}

// API DESC order (the client must not re-sort these).
const ROWS: CategoryStatRow[] = [
  { ...row({}), categoryUuid: "c-1", categoryName: "Ăn uống", color: "#F97316", icon: "🍜", total: 500000, expenseCount: 5 },
  { ...row({}), categoryUuid: "c-2", categoryName: "Đi lại", color: "#3B82F6", icon: "🚗", total: 250000, expenseCount: 3 },
  { ...row({}), categoryUuid: "c-3", categoryName: "Giải trí", color: "#8B5CF6", icon: "🎮", total: 100000, expenseCount: 1, isDeleted: true },
];
const OVERVIEW_TOTAL = 1000000;

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("CategoryBarChart", () => {
  it("CategoryBarChart_Region_IsRoleImgWithSummarizingAccessibleName", () => {
    renderWithProviders(
      <CategoryBarChart rows={ROWS} overviewTotal={OVERVIEW_TOTAL} />,
    );
    // The region is an image named for AT — top category + category count.
    const chart = screen.getByRole("img", { name: /Ăn uống/ });
    expect(chart).toHaveAccessibleName(/3 danh mục/);
  });

  it("CategoryBarChart_Bars_AreAriaHiddenSoTheTableIsTheDataChannel", () => {
    renderWithProviders(
      <CategoryBarChart rows={ROWS} overviewTotal={OVERVIEW_TOTAL} />,
    );
    const chart = screen.getByRole("img", { name: /Ăn uống/ });
    // Each bar row is decorative — the paired CategoryStatsTable carries the data.
    const hidden = chart.querySelectorAll('[aria-hidden="true"]');
    expect(hidden.length).toBeGreaterThanOrEqual(ROWS.length);
  });

  it("CategoryBarChart_RowsRenderInApiOrder_VerbatimNoReSort", () => {
    renderWithProviders(
      <CategoryBarChart rows={ROWS} overviewTotal={OVERVIEW_TOTAL} />,
    );
    const chart = screen.getByRole("img", { name: /Ăn uống/ });
    const text = chart.textContent ?? "";
    // Order preserved exactly as delivered (total DESC), not re-sorted.
    expect(text.indexOf("Ăn uống")).toBeLessThan(text.indexOf("Đi lại"));
    expect(text.indexOf("Đi lại")).toBeLessThan(text.indexOf("Giải trí"));
  });

  it("CategoryBarChart_EachBar_HasDirectMarkerNameMoneyAndShareLabel", () => {
    renderWithProviders(
      <CategoryBarChart rows={ROWS} overviewTotal={OVERVIEW_TOTAL} />,
    );
    // Names + emoji markers (color is never the sole signal).
    expect(screen.getByText("Ăn uống")).toBeInTheDocument();
    expect(screen.getByText("🍜")).toBeInTheDocument();
    // Exact API money via <Money> (no client-computed figure).
    expect(screen.getByText(vnd(500000))).toBeInTheDocument();
    expect(screen.getByText(vnd(250000))).toBeInTheDocument();
    expect(screen.getByText(vnd(100000))).toBeInTheDocument();
    // % share = total / overviewTotal (display-only ratio).
    expect(screen.getByText("50%")).toBeInTheDocument();
    expect(screen.getByText("25%")).toBeInTheDocument();
    expect(screen.getByText("10%")).toBeInTheDocument();
  });

  it("CategoryBarChart_DeletedCategory_KeepsItsSlotWithDeletedTag", () => {
    renderWithProviders(
      <CategoryBarChart rows={ROWS} overviewTotal={OVERVIEW_TOTAL} />,
    );
    // §4.7 — a soft-deleted category with history still appears, flagged.
    expect(screen.getByText("Giải trí")).toBeInTheDocument();
    expect(screen.getByText("(đã xóa)")).toBeInTheDocument();
  });

  it("CategoryBarChart_BarLength_IsTotalOverMaxTotal", () => {
    const { container } = renderWithProviders(
      <CategoryBarChart rows={ROWS} overviewTotal={OVERVIEW_TOTAL} />,
    );
    // The fills (in API order) normalize to the longest bar (500000 = 100%).
    const fills = [...container.querySelectorAll('[style*="--bar-color"]')].map(
      (el) => el.getAttribute("style") ?? "",
    );
    expect(fills).toHaveLength(3);
    expect(fills[0]).toContain("width: 100%");
    expect(fills[1]).toContain("width: 50%");
    expect(fills[2]).toContain("width: 20%");
  });

  it("CategoryBarChart_BarFill_UsesVizCatSlotsByRankThenMutedNeutral", () => {
    // Nine rows: slots 1..8 get --fs-viz-cat-1..8 in rank order; the 9th folds
    // to the muted neutral (never a recycled categorical slot).
    const many: CategoryStatRow[] = Array.from({ length: 9 }, (_, i) =>
      row({
        categoryUuid: `c-${i}`,
        categoryName: `Danh mục ${i + 1}`,
        total: 900000 - i * 100000,
        expenseCount: 9 - i,
      }),
    );
    const { container } = renderWithProviders(
      <CategoryBarChart rows={many} overviewTotal={4_500_000} />,
    );
    const fills = [...container.querySelectorAll('[style*="--bar-color"]')].map(
      (el) => el.getAttribute("style") ?? "",
    );
    expect(fills).toHaveLength(9);
    for (let i = 0; i < 8; i += 1) {
      expect(fills[i]).toContain(`--bar-color: var(--fs-viz-cat-${i + 1})`);
    }
    expect(fills[8]).toContain("--bar-color: var(--fs-viz-ink-muted)");
  });

  it("CategoryBarChart_EmptyRows_RendersNothing", () => {
    const { container } = renderWithProviders(
      <CategoryBarChart rows={[]} overviewTotal={0} />,
    );
    // No bars, no region — the breakdown owns the empty state.
    expect(container.querySelector('[role="img"]')).toBeNull();
  });
});
