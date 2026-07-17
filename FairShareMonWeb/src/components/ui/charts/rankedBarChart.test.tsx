import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { RankedBarChart } from "./RankedBarChart";
import type { RankedBarItem } from "./RankedBarChart";

/**
 * RankedBarChart — the shared ranked horizontal-bar list generalized from the M6
 * CategoryBarChart (OQ1a). Presentational: items are rendered VERBATIM in the
 * caller's order. Proves: the region is `role="img"` with a summarizing accessible
 * name while every bar row is `aria-hidden` (the caller's paired table is the data
 * channel for AT); the label SLOT + value + meta render directly (identity never
 * rests on the bar fill); the fill uses `--fs-viz-cat-1..8` by rank and folds a
 * 9th+ row to the muted neutral; a caller `color` override is honored; the bar
 * width = the caller-computed ratio; an empty list renders nothing.
 */

function item(overrides: Partial<RankedBarItem>): RankedBarItem {
  return {
    key: "k",
    label: "Nhãn",
    value: "10",
    ratio: 1,
    ...overrides,
  };
}

const ITEMS: RankedBarItem[] = [
  item({ key: "PREMIUM", label: "Premium", value: "15", ratio: 1, meta: "60%" }),
  item({ key: "FREE", label: "Free", value: "10", ratio: 0.5, meta: "40%" }),
];

describe("RankedBarChart", () => {
  it("RankedBarChart_Region_IsRoleImgWithSummarizingLabel", () => {
    render(<RankedBarChart items={ITEMS} ariaLabel="Phân bố theo hạng" />);
    expect(
      screen.getByRole("img", { name: "Phân bố theo hạng" }),
    ).toBeInTheDocument();
  });

  it("RankedBarChart_Bars_AreAriaHiddenSoTheTableIsTheDataChannel", () => {
    render(<RankedBarChart items={ITEMS} ariaLabel="Phân bố theo hạng" />);
    const chart = screen.getByRole("img", { name: "Phân bố theo hạng" });
    const hidden = chart.querySelectorAll('[aria-hidden="true"]');
    expect(hidden.length).toBeGreaterThanOrEqual(ITEMS.length);
  });

  it("RankedBarChart_EachRow_RendersLabelSlotValueAndMeta", () => {
    render(<RankedBarChart items={ITEMS} ariaLabel="Phân bố theo hạng" />);
    expect(screen.getByText("Premium")).toBeInTheDocument();
    expect(screen.getByText("Free")).toBeInTheDocument();
    expect(screen.getByText("15")).toBeInTheDocument();
    expect(screen.getByText("10")).toBeInTheDocument();
    expect(screen.getByText("60%")).toBeInTheDocument();
    expect(screen.getByText("40%")).toBeInTheDocument();
  });

  it("RankedBarChart_RowsRenderInCallerOrder_VerbatimNoReSort", () => {
    const chart = render(
      <RankedBarChart items={ITEMS} ariaLabel="Phân bố theo hạng" />,
    ).container;
    const text = chart.textContent ?? "";
    expect(text.indexOf("Premium")).toBeLessThan(text.indexOf("Free"));
  });

  it("RankedBarChart_BarWidth_IsCallerRatio", () => {
    const { container } = render(
      <RankedBarChart items={ITEMS} ariaLabel="Phân bố theo hạng" />,
    );
    const fills = [...container.querySelectorAll('[style*="--bar-color"]')].map(
      (el) => el.getAttribute("style") ?? "",
    );
    expect(fills).toHaveLength(2);
    expect(fills[0]).toContain("width: 100%");
    expect(fills[1]).toContain("width: 50%");
  });

  it("RankedBarChart_BarFill_UsesVizCatSlotsByRankThenMutedNeutral", () => {
    const many: RankedBarItem[] = Array.from({ length: 9 }, (_, i) =>
      item({ key: `k-${i}`, label: `Nhãn ${i + 1}`, ratio: 1 - i * 0.1 }),
    );
    const { container } = render(
      <RankedBarChart items={many} ariaLabel="Nhiều nhóm" />,
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

  it("RankedBarChart_ExplicitColor_OverridesTheRankSlot", () => {
    const { container } = render(
      <RankedBarChart
        items={[item({ key: "x", color: "var(--fs-viz-seq-500)" })]}
        ariaLabel="Một nhóm"
      />,
    );
    const fill = container.querySelector('[style*="--bar-color"]');
    expect(fill?.getAttribute("style") ?? "").toContain(
      "--bar-color: var(--fs-viz-seq-500)",
    );
  });

  it("RankedBarChart_EmptyItems_RendersNothing", () => {
    const { container } = render(
      <RankedBarChart items={[]} ariaLabel="Trống" />,
    );
    expect(container.querySelector('[role="img"]')).toBeNull();
  });
});
