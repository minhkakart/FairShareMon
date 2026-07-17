import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { TimeSeriesBarChart } from "./TimeSeriesBarChart";
import type { TimeSeriesBarItem } from "./TimeSeriesBarChart";

/**
 * TimeSeriesBarChart — the net-new shared column chart over ordered time buckets
 * (OQ1a), used by the Admin signups + revenue dashboards. Presentational: buckets
 * render VERBATIM in the caller's ascending order. Proves: the plot is `role="img"`
 * with a summarizing accessible name while the columns are `aria-hidden` (the
 * paired table is the data channel); axis labels render for every bucket; column
 * heights = the caller-computed ratio via inline style; the value caps show when
 * `showValues` and are suppressed otherwise (the table still carries every value);
 * an empty list renders nothing.
 */

const ITEMS: TimeSeriesBarItem[] = [
  { key: "2026-01", periodLabel: "01/2026", ratio: 0.5, value: "2", title: "01/2026: 2" },
  { key: "2026-02", periodLabel: "02/2026", ratio: 1, value: "4", title: "02/2026: 4" },
];

describe("TimeSeriesBarChart", () => {
  it("TimeSeriesBarChart_Plot_IsRoleImgWithSummarizingLabel", () => {
    render(<TimeSeriesBarChart items={ITEMS} ariaLabel="Đăng ký theo kỳ" />);
    expect(
      screen.getByRole("img", { name: "Đăng ký theo kỳ" }),
    ).toBeInTheDocument();
  });

  it("TimeSeriesBarChart_Columns_AreAriaHiddenSoTheTableIsTheDataChannel", () => {
    render(<TimeSeriesBarChart items={ITEMS} ariaLabel="Đăng ký theo kỳ" />);
    const plot = screen.getByRole("img", { name: "Đăng ký theo kỳ" });
    expect(
      plot.querySelectorAll('[aria-hidden="true"]').length,
    ).toBeGreaterThanOrEqual(ITEMS.length);
  });

  it("TimeSeriesBarChart_AxisLabels_RenderForEveryBucketInOrder", () => {
    const { container } = render(
      <TimeSeriesBarChart items={ITEMS} ariaLabel="Đăng ký theo kỳ" />,
    );
    expect(screen.getByText("01/2026")).toBeInTheDocument();
    expect(screen.getByText("02/2026")).toBeInTheDocument();
    const text = container.textContent ?? "";
    expect(text.indexOf("01/2026")).toBeLessThan(text.indexOf("02/2026"));
  });

  it("TimeSeriesBarChart_ColumnHeight_IsCallerRatio", () => {
    const { container } = render(
      <TimeSeriesBarChart items={ITEMS} ariaLabel="Đăng ký theo kỳ" />,
    );
    const heights = [...container.querySelectorAll('[style*="height"]')].map(
      (el) => el.getAttribute("style") ?? "",
    );
    expect(heights.some((s) => s.includes("height: 50%"))).toBe(true);
    expect(heights.some((s) => s.includes("height: 100%"))).toBe(true);
  });

  it("TimeSeriesBarChart_ShowValuesTrue_RendersValueCaps", () => {
    render(<TimeSeriesBarChart items={ITEMS} ariaLabel="Đăng ký theo kỳ" />);
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
  });

  it("TimeSeriesBarChart_ShowValuesFalse_SuppressesValueCaps", () => {
    render(
      <TimeSeriesBarChart
        items={ITEMS}
        ariaLabel="Đăng ký theo kỳ"
        showValues={false}
      />,
    );
    // Caps hidden; the axis labels still render (period labels remain).
    expect(screen.queryByText("2")).not.toBeInTheDocument();
    expect(screen.queryByText("4")).not.toBeInTheDocument();
    expect(screen.getByText("01/2026")).toBeInTheDocument();
  });

  it("TimeSeriesBarChart_EmptyItems_RendersNothing", () => {
    const { container } = render(
      <TimeSeriesBarChart items={[]} ariaLabel="Trống" />,
    );
    expect(container.querySelector('[role="img"]')).toBeNull();
  });
});
