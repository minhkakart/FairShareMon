import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CategoryMarker } from "./CategoryMarker/CategoryMarker";

/**
 * CategoryMarker — a presentational chip pairing a category's color swatch with
 * its emoji glyph and (optionally) name. Color is never the sole signal: meaning
 * is carried by the visible name (labelled mode) or an accessible name (icon-only).
 */
describe("CategoryMarker", () => {
  it("CategoryMarker_WithIconAndLabel_RendersGlyphAndName", () => {
    render(<CategoryMarker color="#F97316" icon="🍜" name="Ăn uống" showLabel />);
    // The chosen emoji is rendered verbatim…
    expect(screen.getByText("🍜")).toBeInTheDocument();
    // …alongside the name, which carries the meaning.
    expect(screen.getByText("Ăn uống")).toBeInTheDocument();
  });

  it("CategoryMarker_WithoutIcon_FallsBackToColorDotAndKeepsName", () => {
    render(<CategoryMarker color="#3B82F6" name="Đi lại" showLabel />);
    // No glyph is rendered when the icon is absent…
    expect(screen.queryByText("🍜")).not.toBeInTheDocument();
    // …but the name still carries meaning (color is never the sole signal).
    expect(screen.getByText("Đi lại")).toBeInTheDocument();
  });

  it("CategoryMarker_IconOnlyMode_ExposesNameAsAccessibleImage", () => {
    render(<CategoryMarker color="#8B5CF6" icon="🏨" name="Khách sạn" />);
    // Icon-only markers expose the name via role="img" + aria-label.
    expect(screen.getByRole("img", { name: "Khách sạn" })).toBeInTheDocument();
  });

  it("CategoryMarker_IconOnlyDefault_AppendsDefaultLabelToAccessibleName", () => {
    render(
      <CategoryMarker
        color="#F97316"
        icon="🍜"
        name="Ăn uống"
        isDefault
        defaultLabel="mặc định"
      />,
    );
    expect(
      screen.getByRole("img", { name: "Ăn uống, mặc định" }),
    ).toBeInTheDocument();
  });
});
