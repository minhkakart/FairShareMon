import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { IconPicker, CURATED_ICONS } from "./IconPicker/IconPicker";

/**
 * IconPicker — a controlled emoji radiogroup + a "no icon" option, emitting the
 * emoji glyph verbatim (or null). Presentational (parent owns value/onChange), so
 * a small stateful harness drives it and a spy captures each emitted value.
 */
function Harness({
  initial = null,
  onChange,
}: {
  initial?: string | null;
  onChange?: (value: string | null) => void;
}) {
  const [value, setValue] = useState<string | null>(initial);
  return (
    <IconPicker
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      label="Biểu tượng"
      noIconLabel="Không có"
    />
  );
}

describe("IconPicker", () => {
  it("IconPicker_Grid_RendersCuratedEmojiPlusNoIconOption", () => {
    render(<Harness />);
    const group = screen.getByRole("radiogroup", { name: "Biểu tượng" });
    const radios = within(group).getAllByRole("radio");
    // Curated set + the "no icon" option.
    expect(radios).toHaveLength(CURATED_ICONS.length + 1);
    // The "no icon" option carries an accessible name (not color/shape alone).
    expect(
      within(group).getByRole("radio", { name: "Không có" }),
    ).toBeInTheDocument();
    // Every seed glyph is selectable by its accessible name (the emoji itself).
    expect(within(group).getByRole("radio", { name: "🍜" })).toBeInTheDocument();
    expect(within(group).getByRole("radio", { name: "🚗" })).toBeInTheDocument();
  });

  it("IconPicker_ClickEmoji_EmitsGlyphVerbatim", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(screen.getByRole("radio", { name: "☕" }));
    // The stored value is the emoji glyph itself (no key-mapping layer).
    expect(onChange).toHaveBeenLastCalledWith("☕");
    expect(screen.getByRole("radio", { name: "☕" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("IconPicker_ClickNoIcon_EmitsNull", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness initial="🍜" onChange={onChange} />);

    await user.click(screen.getByRole("radio", { name: "Không có" }));
    expect(onChange).toHaveBeenLastCalledWith(null);
    expect(screen.getByRole("radio", { name: "Không có" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("IconPicker_ArrowKey_MovesSelectionAndEmits", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    // Focus the "no icon" (index 0) then arrow to the first emoji (index 1).
    await user.click(screen.getByRole("radio", { name: "Không có" }));
    onChange.mockClear();
    await user.keyboard("{ArrowRight}");
    expect(onChange).toHaveBeenLastCalledWith(CURATED_ICONS[0]);
  });

  it("IconPicker_SelectedEmoji_HasAriaChecked", () => {
    render(<Harness initial="🍜" />);
    expect(screen.getByRole("radio", { name: "🍜" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });
});
