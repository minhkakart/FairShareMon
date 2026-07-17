import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatMoneyVnd } from "@/i18n/format";
import i18n from "@/i18n";
import { CategoryStatsTable } from "./components/CategoryStatsTable";
import type { CategoryStatRow } from "./api/types";

/** VND renders with a non-breaking space the DOM normalizer collapses. */
const vnd = (n: number) => formatMoneyVnd(n).replace(/\s+/g, " ");

/**
 * CategoryStatsTable — the always-present accessible data channel paired with the
 * decorative chart. Proves: a named `<table>` (caption) with the four column
 * headers; each row's total renders via `<Money>` as the EXACT API number (no
 * client-summed money); the footer ECHOES the authoritative `overview.totalSpending`
 * and `overview.expenseCount` VERBATIM — NOT a sum of the rows (the footer-echo
 * invariant); deleted categories keep their slot flagged `(đã xóa)`; share % is a
 * display-only ratio.
 *
 * The fixture deliberately makes the overview total (1.000.000) and count (42)
 * DIFFER from the row sums (850.000 / 9) so an accidental client-sum would be
 * caught red-handed.
 */

const ROWS: CategoryStatRow[] = [
  { categoryUuid: "c-1", categoryName: "Ăn uống", color: "#F97316", icon: "🍜", isDeleted: false, total: 500000, expenseCount: 5 },
  { categoryUuid: "c-2", categoryName: "Đi lại", color: "#3B82F6", icon: "🚗", isDeleted: false, total: 250000, expenseCount: 3 },
  { categoryUuid: "c-3", categoryName: "Giải trí", color: "#8B5CF6", icon: "🎮", isDeleted: true, total: 100000, expenseCount: 1 },
];
const ROW_TOTAL_SUM = 850000; // 500000 + 250000 + 100000 — must NOT be echoed.
const ROW_COUNT_SUM = 9; // 5 + 3 + 1 — must NOT be echoed.
const OVERVIEW_TOTAL = 1000000; // authoritative — the footer must show THIS.
const OVERVIEW_COUNT = 42;

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

function renderTable(overviewCount?: number) {
  return renderWithProviders(
    <CategoryStatsTable
      rows={ROWS}
      overviewTotal={OVERVIEW_TOTAL}
      overviewCount={overviewCount}
    />,
  );
}

describe("CategoryStatsTable", () => {
  it("CategoryStatsTable_HasNamedTableWithFourColumnHeaders", () => {
    renderTable(OVERVIEW_COUNT);
    // Named by its caption (kept for AT even when visually hidden).
    expect(
      screen.getByRole("table", { name: "Chi tiêu theo danh mục" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Danh mục" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Tổng" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Số phiếu" })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Tỷ trọng" })).toBeInTheDocument();
  });

  it("CategoryStatsTable_EachRow_ShowsExactApiMoneyAndShare", () => {
    renderTable(OVERVIEW_COUNT);
    // Every total is the raw API number via <Money> — never client-derived.
    expect(screen.getByText(vnd(500000))).toBeInTheDocument();
    expect(screen.getByText(vnd(250000))).toBeInTheDocument();
    // Share % against the authoritative overview total (display-only ratio).
    expect(screen.getByText("50%")).toBeInTheDocument();
    expect(screen.getByText("25%")).toBeInTheDocument();
    expect(screen.getByText("10%")).toBeInTheDocument();
  });

  it("CategoryStatsTable_Footer_EchoesOverviewTotalAndCountNotTheRowSums", () => {
    const { container } = renderTable(OVERVIEW_COUNT);
    const footRow = container.querySelector("tr[data-total]");
    expect(footRow).not.toBeNull();
    const foot = within(footRow as HTMLElement);

    // The footer money is the authoritative overview total, VERBATIM…
    expect(foot.getByText(vnd(OVERVIEW_TOTAL))).toBeInTheDocument();
    // …and NOT the sum of the rendered rows.
    expect(foot.queryByText(vnd(ROW_TOTAL_SUM))).not.toBeInTheDocument();

    // The footer count echoes overview.expenseCount, NOT the row-count sum.
    expect(foot.getByText(String(OVERVIEW_COUNT))).toBeInTheDocument();
    expect(foot.queryByText(String(ROW_COUNT_SUM))).not.toBeInTheDocument();

    // The whole is 100% by definition.
    expect(foot.getByText("100%")).toBeInTheDocument();
  });

  it("CategoryStatsTable_FooterWithoutOverviewCount_ShowsDashPlaceholder", () => {
    const { container } = renderTable(undefined);
    const footRow = container.querySelector("tr[data-total]");
    const foot = within(footRow as HTMLElement);
    // No authoritative count → an em-dash, never a client sum.
    expect(foot.getByText("—")).toBeInTheDocument();
    expect(foot.queryByText(String(ROW_COUNT_SUM))).not.toBeInTheDocument();
  });

  it("CategoryStatsTable_DeletedCategory_IsFlaggedAndMuted", () => {
    const { container } = renderTable(OVERVIEW_COUNT);
    expect(screen.getByText("(đã xóa)")).toBeInTheDocument();
    // The muted-row hook is set for the deleted category's row.
    const deletedRows = container.querySelectorAll("tbody tr[data-deleted]");
    expect(deletedRows).toHaveLength(1);
    expect(deletedRows[0]).toHaveTextContent("Giải trí");
  });
});
