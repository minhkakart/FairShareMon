import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { KpiRow, KpiTile, KpiValue } from "./KpiTile";

/**
 * KpiTile / KpiValue / KpiRow — the shared KPI primitives extracted from M6
 * (OQ1a) and consumed by both the Stats (M6) and Admin (M8) dashboards.
 * Presentational: rendered directly with props (no providers needed). Proves the
 * tile keeps its label while loading and swaps only the value for a skeleton, a
 * zero renders as valid data (never an empty state), the hint is suppressed while
 * loading, and the row/value wrappers render their children.
 */

describe("KpiTile", () => {
  it("KpiTile_WithValueAndHint_RendersBoth", () => {
    render(
      <KpiTile
        label="Tổng người dùng"
        value={<KpiValue>25</KpiValue>}
        hint="Mọi hạng"
      />,
    );
    expect(screen.getByText("Tổng người dùng")).toBeInTheDocument();
    expect(screen.getByText("25")).toBeInTheDocument();
    expect(screen.getByText("Mọi hạng")).toBeInTheDocument();
  });

  it("KpiTile_Loading_KeepsLabelSwapsValueForSkeletonAndHidesHint", () => {
    const { container } = render(
      <KpiTile label="Tổng người dùng" hint="Mọi hạng" loading />,
    );
    // Label stays so the layout does not jump…
    expect(screen.getByText("Tổng người dùng")).toBeInTheDocument();
    // …the hint is suppressed while loading…
    expect(screen.queryByText("Mọi hạng")).not.toBeInTheDocument();
    // …and a skeleton placeholder (aria-hidden) stands in for the value.
    expect(
      container.querySelectorAll('[aria-hidden="true"]').length,
    ).toBeGreaterThanOrEqual(1);
  });

  it("KpiTile_ZeroValue_RendersZeroNotEmptyState", () => {
    render(<KpiTile label="Đang hoạt động" value={<KpiValue>0</KpiValue>} />);
    // Zero is valid data — the tile shows `0`.
    expect(screen.getByText("0")).toBeInTheDocument();
  });
});

describe("KpiValue / KpiRow", () => {
  it("KpiValue_RendersItsChildren", () => {
    render(<KpiValue>1.234</KpiValue>);
    expect(screen.getByText("1.234")).toBeInTheDocument();
  });

  it("KpiRow_RendersAllTiles", () => {
    render(
      <KpiRow>
        <KpiTile label="A" value={<KpiValue>1</KpiValue>} />
        <KpiTile label="B" value={<KpiValue>2</KpiValue>} />
      </KpiRow>,
    );
    expect(screen.getByText("A")).toBeInTheDocument();
    expect(screen.getByText("B")).toBeInTheDocument();
  });
});
