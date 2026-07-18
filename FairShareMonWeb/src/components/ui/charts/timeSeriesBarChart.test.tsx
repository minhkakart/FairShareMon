import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { TimeSeriesBarChart } from "./TimeSeriesBarChart";
import type { TimeSeriesBarItem } from "./TimeSeriesBarChart";
import styles from "./charts.module.css";

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

/**
 * Phone-density behavior (cycle-2 2b, OQ3a). The chart keeps EVERY column and the
 * caller's paired table carries the full series; only the DECORATIVE axis captions
 * thin above ~12 buckets (`stride = max(1, ceil(items.length / 12))`, a label renders
 * only when `index % stride === 0`, hidden slots kept for alignment, `.tsAxisThinned`
 * modifier applied when `stride > 1`), and the plot + axis share ONE `.tsScroll` box
 * so they scroll together. jsdom computes no layout, so these assert on rendered
 * structure/classes + text presence + `aria-*` — never pixel widths or scroll offsets.
 *
 * NOTE ON THE "PAIRED TABLE": `TimeSeriesBarChart` renders NO `<table>` itself — the
 * accessible data table is composed by each CALLER (e.g. `SignupsPanel` /
 * `RevenueChart` via the `Table` primitive). So this primitive's own "no data dropped"
 * guarantee is asserted here as: rendered columns === `items.length` AND every column
 * still carries its value (cap + `title`) regardless of thinning.
 */

/** n items with unique, index-encoded key/label/value so slots can be indexed. */
function makeItems(n: number): TimeSeriesBarItem[] {
  return Array.from({ length: n }, (_, i) => ({
    key: `k${i}`,
    periodLabel: `p${i}`,
    ratio: (i % 10) / 10,
    value: `v${i}`,
    title: `p${i}: v${i}`,
  }));
}

const axisLabelsOf = (c: HTMLElement) =>
  [...c.getElementsByClassName(styles.tsAxisLabel)] as HTMLElement[];
const columnsOf = (c: HTMLElement) =>
  [...c.getElementsByClassName(styles.tsCol)] as HTMLElement[];
const axisOf = (c: HTMLElement) =>
  c.getElementsByClassName(styles.tsAxis)[0] as HTMLElement;

describe("TimeSeriesBarChart phone density (cycle-2 2b)", () => {
  it("TimeSeriesBarChart_DenseBuckets_ThinsAxisLabelsAndFlagsThinned", () => {
    // 31 day-buckets → stride = ceil(31/12) = 3 → labels at index 0,3,6,…,30 = 11.
    const { container } = render(
      <TimeSeriesBarChart
        items={makeItems(31)}
        ariaLabel="Đăng ký theo ngày"
        showValues={false}
      />,
    );

    const labels = axisLabelsOf(container);
    // Every bucket keeps a slot (alignment) even when its caption is thinned away.
    expect(labels).toHaveLength(31);

    const shown = labels.filter((el) => (el.textContent ?? "").trim() !== "");
    expect(shown).toHaveLength(11); // ≈ ceil(31 / stride=3)

    // Index 0 shows; index 1 is a kept-but-blank slot; the next stride hit (3) shows.
    expect(labels[0].textContent).toBe("p0");
    expect(labels[1].textContent).toBe("");
    expect(labels[2].textContent).toBe("");
    expect(labels[3].textContent).toBe("p3");

    // The thinned modifier is applied to the axis row (stride > 1).
    expect(axisOf(container).classList.contains(styles.tsAxisThinned)).toBe(true);
  });

  it("TimeSeriesBarChart_AtThresholdTwelve_AppliesNoThinning", () => {
    // Boundary: 12 buckets → stride = ceil(12/12) = 1 → every label shows, no thinning.
    const { container } = render(
      <TimeSeriesBarChart items={makeItems(12)} ariaLabel="Đăng ký theo kỳ" />,
    );

    const labels = axisLabelsOf(container);
    expect(labels).toHaveLength(12);
    expect(labels.every((el) => (el.textContent ?? "").trim() !== "")).toBe(true);
    expect(labels[0].textContent).toBe("p0");
    expect(labels[11].textContent).toBe("p11");

    // No modifier class at stride 1.
    expect(axisOf(container).classList.contains(styles.tsAxisThinned)).toBe(false);
  });

  it("TimeSeriesBarChart_JustAboveThreshold_StartsThinningAtThirteen", () => {
    // Guards the exact >12 boundary: 13 buckets → stride = ceil(13/12) = 2 →
    // labels at index 0,2,4,…,12 = 7 shown, and the thinned modifier turns on.
    const { container } = render(
      <TimeSeriesBarChart items={makeItems(13)} ariaLabel="Đăng ký theo kỳ" />,
    );

    const labels = axisLabelsOf(container);
    expect(labels).toHaveLength(13);
    expect(
      labels.filter((el) => (el.textContent ?? "").trim() !== ""),
    ).toHaveLength(7);
    expect(labels[0].textContent).toBe("p0");
    expect(labels[1].textContent).toBe("");
    expect(axisOf(container).classList.contains(styles.tsAxisThinned)).toBe(true);
  });

  it("TimeSeriesBarChart_ThinnedAxis_DropsNoColumnOrValue", () => {
    // The load-bearing invariant: thinning removes only axis CAPTIONS, never data.
    // With showValues the cap carries each value; assert one column whose axis
    // caption was thinned away (index 1) still renders with its value.
    const { container } = render(
      <TimeSeriesBarChart items={makeItems(31)} ariaLabel="Đăng ký theo ngày" />,
    );

    // Every bucket renders a column, regardless of axis thinning.
    const cols = columnsOf(container);
    expect(cols).toHaveLength(31);

    // Every column carries its full "period: value" title (the always-on data cue).
    expect(cols.every((c) => (c.getAttribute("title") ?? "") !== "")).toBe(true);
    expect(cols[1].getAttribute("title")).toBe("p1: v1");

    // Every value cap renders (showValues default true) — 31 values, none dropped.
    const caps = container.getElementsByClassName(styles.tsCap);
    expect(caps).toHaveLength(31);
    // Index 1's axis label is blank (thinned) yet its value cap is present.
    expect(axisLabelsOf(container)[1].textContent).toBe("");
    expect(screen.getByText("v1")).toBeInTheDocument();
    expect(screen.getByText("v30")).toBeInTheDocument();
  });

  it("TimeSeriesBarChart_Scroller_WrapsBothPlotAndAxisSoTheyScrollTogether", () => {
    const { container } = render(
      <TimeSeriesBarChart items={makeItems(31)} ariaLabel="Đăng ký theo ngày" />,
    );

    const scroll = container.getElementsByClassName(styles.tsScroll)[0];
    expect(scroll).toBeTruthy();

    // The single scroller contains BOTH the plot (role="img") and the axis row, so
    // labels stay column-aligned at any scroll position. Robust to DOM depth.
    const plot = screen.getByRole("img", { name: "Đăng ký theo ngày" });
    const axis = axisOf(container);
    expect(scroll.contains(plot)).toBe(true);
    expect(scroll.contains(axis)).toBe(true);
  });

  it("TimeSeriesBarChart_DenseBuckets_PreservesRoleImgAndAriaHiddenMarks", () => {
    const { container } = render(
      <TimeSeriesBarChart
        items={makeItems(31)}
        ariaLabel="Đăng ký theo ngày"
        showValues={false}
      />,
    );

    // Thinning does not disturb the a11y contract: summarizing role="img" survives…
    const plot = screen.getByRole("img", { name: "Đăng ký theo ngày" });
    expect(plot).toBeInTheDocument();

    // …and every column mark stays aria-hidden (the caller's table is the data
    // channel for assistive tech, not the decorative columns).
    const cols = columnsOf(container);
    expect(cols).toHaveLength(31);
    expect(cols.every((c) => c.getAttribute("aria-hidden") === "true")).toBe(true);

    // The axis row is likewise aria-hidden decoration.
    expect(axisOf(container).getAttribute("aria-hidden")).toBe("true");
  });
});
