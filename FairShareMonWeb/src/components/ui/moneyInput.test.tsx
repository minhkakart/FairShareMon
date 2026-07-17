import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MoneyInput } from "./MoneyInput/MoneyInput";
import { formatMoneyVnd } from "@/i18n/format";

/**
 * MoneyInput — the whole-VND integer input (OQ4a). It strips non-digits, emits a
 * plain integer (or null when empty), shows a grouped figure while blurred and raw
 * digits while focused. Controlled/presentational, so a stateful harness drives it
 * and a spy captures each emitted value. The app's shared vi-VN grouping formatter
 * is injected so the display matches `Money`.
 */
function Harness({
  initial = null,
  onChange,
  label = "Số tiền",
  error,
  disabled,
  useCurrencyFormat = true,
}: {
  initial?: number | null;
  onChange?: (value: number | null) => void;
  label?: string;
  error?: string;
  disabled?: boolean;
  /** When false, use the component's default grouping formatter (no ₫ glyph). */
  useCurrencyFormat?: boolean;
}) {
  const [value, setValue] = useState<number | null>(initial);
  return (
    <MoneyInput
      label={label}
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      format={useCurrencyFormat ? formatMoneyVnd : undefined}
      error={error}
      disabled={disabled}
    />
  );
}

describe("MoneyInput", () => {
  it("MoneyInput_TypedDigits_EmitWholeInteger", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    const input = screen.getByRole("textbox", { name: "Số tiền" });
    await user.type(input, "250000");
    expect(onChange).toHaveBeenLastCalledWith(250000);
    expect(Number.isInteger(onChange.mock.calls.at(-1)?.[0])).toBe(true);
  });

  it("MoneyInput_NonDigitCharacters_AreStripped", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    const input = screen.getByRole("textbox", { name: "Số tiền" });
    // Letters/punctuation are ignored → digits only, no negatives possible.
    await user.type(input, "1a2b3c");
    expect(onChange).toHaveBeenLastCalledWith(123);
  });

  it("MoneyInput_ClearedInput_EmitsNull", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness initial={5000} onChange={onChange} />);

    const input = screen.getByRole("textbox", { name: "Số tiền" });
    await user.clear(input);
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("MoneyInput_Blurred_ShowsGroupedDisplay", async () => {
    const user = userEvent.setup();
    render(<Harness initial={1234567} useCurrencyFormat={false} />);

    const input = screen.getByRole("textbox", { name: "Số tiền" });
    // Blurred → grouped vi-VN thousands (dots).
    expect(input).toHaveValue("1.234.567");

    // Focused → raw digits for frictionless editing.
    await user.click(input);
    expect(input).toHaveValue("1234567");
  });

  it("MoneyInput_ErrorProp_RendersAlertAndMarksInvalid", () => {
    render(<Harness error="Số tiền phải là số nguyên không âm." />);
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Số tiền phải là số nguyên không âm.",
    );
    expect(screen.getByRole("textbox", { name: "Số tiền" })).toHaveAttribute(
      "aria-invalid",
      "true",
    );
  });

  it("MoneyInput_Disabled_DoesNotEmit", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness disabled onChange={onChange} />);

    const input = screen.getByRole("textbox", { name: "Số tiền" });
    await user.type(input, "99");
    expect(onChange).not.toHaveBeenCalled();
  });
});
