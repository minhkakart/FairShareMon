import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ColorPicker, CURATED_COLORS } from "./ColorPicker/ColorPicker";

/**
 * ColorPicker — a controlled swatch radiogroup + custom-hex path yielding a
 * validated `#RRGGBB`. Presentational (parent owns value/onChange), so a small
 * stateful harness drives it and a spy captures every emitted value. Deterministic
 * (no network, no timers).
 */
function Harness({
  initial = "#F97316",
  onChange,
}: {
  initial?: string;
  onChange?: (value: string) => void;
}) {
  const [value, setValue] = useState(initial);
  return (
    <ColorPicker
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      label="Màu"
      hexLabel="Mã màu tùy chỉnh"
      invalidHexMessage="Mã màu phải có dạng #RRGGBB."
      required
    />
  );
}

describe("ColorPicker", () => {
  it("ColorPicker_Swatches_RenderAsLabeledRadioGroup", () => {
    render(<Harness />);
    const group = screen.getByRole("radiogroup", { name: "Màu" });
    const radios = within(group).getAllByRole("radio");
    expect(radios).toHaveLength(CURATED_COLORS.length);
    // Each swatch has an accessible name (its hex) — color is never the sole cue.
    expect(within(group).getByRole("radio", { name: "#F97316" })).toBeInTheDocument();
  });

  it("ColorPicker_SeedColorSelected_HasAriaChecked", () => {
    render(<Harness initial="#3B82F6" />);
    const selected = screen.getByRole("radio", { name: "#3B82F6" });
    expect(selected).toHaveAttribute("aria-checked", "true");
  });

  it("ColorPicker_ClickSwatch_EmitsUppercasedHex", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(screen.getByRole("radio", { name: "#0EA5E9" }));
    // The stored value is a validated, upper-cased #RRGGBB.
    expect(onChange).toHaveBeenLastCalledWith("#0EA5E9");
    expect(screen.getByRole("radio", { name: "#0EA5E9" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("ColorPicker_ArrowKey_MovesSelectionAndEmits", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness initial={CURATED_COLORS[0]} onChange={onChange} />);

    await user.click(screen.getByRole("radio", { name: CURATED_COLORS[0] }));
    onChange.mockClear();
    // Roving-tabindex arrow keys move within the group (keyboard-operable).
    await user.keyboard("{ArrowRight}");
    expect(onChange).toHaveBeenLastCalledWith(CURATED_COLORS[1].toUpperCase());
  });

  it("ColorPicker_CustomHexValid_EmitsUppercasedHex", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    const hex = screen.getByRole("textbox", { name: "Mã màu tùy chỉnh" });
    await user.clear(hex);
    await user.type(hex, "#1a2b3c");
    expect(onChange).toHaveBeenLastCalledWith("#1A2B3C");
  });

  it("ColorPicker_CustomHexInvalid_ShowsFieldErrorAndDoesNotEmit", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    const hex = screen.getByRole("textbox", { name: "Mã màu tùy chỉnh" });
    await user.clear(hex);
    await user.type(hex, "#12 zz");
    // A partial/invalid hex surfaces the field error and never commits a value.
    expect(
      await screen.findByText("Mã màu phải có dạng #RRGGBB."),
    ).toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("ColorPicker_ErrorProp_RendersAlert", () => {
    render(
      <ColorPicker
        value="#F97316"
        onChange={() => {}}
        label="Màu"
        hexLabel="Mã màu tùy chỉnh"
        error="Màu không hợp lệ."
      />,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("Màu không hợp lệ.");
  });
});
