import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Select } from "./Select/Select";
import type { SelectOption } from "./Select/Select";

/**
 * Select — the Radix-backed single-select primitive (M4's first combobox). It is
 * controlled/presentational, so a small stateful harness drives it and a spy
 * captures each emitted value. Interactions assert accessible roles (combobox /
 * option) and the option-renderer slot; deterministic (no network, no timers).
 * The jsdom pointer/scroll polyfills live in `src/test/setup.ts`.
 */

type Meta = { badge?: string };

function Harness({
  initial,
  onChange,
  options,
  renderOption,
  error,
  label = "Danh mục",
  placeholder = "Chọn danh mục",
}: {
  initial?: string;
  onChange?: (value: string) => void;
  options: SelectOption<Meta>[];
  renderOption?: (o: SelectOption<Meta>) => React.ReactNode;
  error?: string;
  label?: string;
  placeholder?: string;
}) {
  const [value, setValue] = useState<string | undefined>(initial);
  return (
    <Select<Meta>
      label={label}
      placeholder={placeholder}
      value={value}
      onValueChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      options={options}
      renderOption={renderOption}
      error={error}
    />
  );
}

const OPTIONS: SelectOption<Meta>[] = [
  { value: "a", label: "Ăn uống" },
  { value: "b", label: "Đi lại" },
  { value: "c", label: "Khách sạn" },
];

describe("Select", () => {
  it("Select_Trigger_ExposesComboboxRoleWithAccessibleName", () => {
    render(<Harness options={OPTIONS} />);
    expect(
      screen.getByRole("combobox", { name: "Danh mục" }),
    ).toBeInTheDocument();
  });

  it("Select_Placeholder_ShownWhenNoValue", () => {
    render(<Harness options={OPTIONS} />);
    expect(screen.getByText("Chọn danh mục")).toBeInTheDocument();
  });

  it("Select_ClickOption_EmitsValueAndUpdatesTrigger", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness options={OPTIONS} onChange={onChange} />);

    await user.click(screen.getByRole("combobox", { name: "Danh mục" }));
    await user.click(await screen.findByRole("option", { name: "Đi lại" }));

    expect(onChange).toHaveBeenLastCalledWith("b");
    // Radix mirrors the selected option text into the trigger.
    expect(screen.getByRole("combobox", { name: "Danh mục" })).toHaveTextContent(
      "Đi lại",
    );
  });

  it("Select_KeyboardOpenAndSelect_IsOperable", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness options={OPTIONS} onChange={onChange} />);

    const trigger = screen.getByRole("combobox", { name: "Danh mục" });
    trigger.focus();
    // Enter opens; ArrowDown moves to the first option; Enter selects it.
    await user.keyboard("{Enter}");
    await screen.findByRole("option", { name: "Ăn uống" });
    await user.keyboard("{ArrowDown}{Enter}");

    expect(onChange).toHaveBeenCalled();
  });

  it("Select_RenderOptionSlot_RendersCustomOptionContent", async () => {
    const user = userEvent.setup();
    const options: SelectOption<Meta>[] = [
      { value: "a", label: "Ăn uống", meta: { badge: "mặc định" } },
      { value: "b", label: "Đi lại" },
    ];
    render(
      <Harness
        options={options}
        renderOption={(o) => (
          <span>
            {o.label}
            {o.meta?.badge ? <em> {o.meta.badge}</em> : null}
          </span>
        )}
      />,
    );

    await user.click(screen.getByRole("combobox", { name: "Danh mục" }));
    const option = await screen.findByRole("option", { name: /Ăn uống/ });
    expect(option).toHaveTextContent("mặc định");
  });

  it("Select_ErrorProp_RendersAlertAndMarksInvalid", () => {
    render(<Harness options={OPTIONS} error="Danh mục không hợp lệ." />);
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Danh mục không hợp lệ.",
    );
    expect(screen.getByRole("combobox", { name: "Danh mục" })).toHaveAttribute(
      "aria-invalid",
      "true",
    );
  });
});
