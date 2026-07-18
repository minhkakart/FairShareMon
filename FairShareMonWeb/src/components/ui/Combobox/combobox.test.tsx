import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Combobox, normalizeForSearch } from "./Combobox";
import type { ComboboxOption, ComboboxProps } from "./Combobox";

/**
 * Combobox — the hand-rolled ARIA-1.2 "combobox with listbox popup" primitive
 * (searchable single-select). Controlled/presentational, so a small stateful
 * harness drives it and a spy captures each emitted value. Assertions target
 * accessible roles/names + ARIA state (combobox / listbox / option, aria-expanded,
 * aria-activedescendant, aria-selected, aria-invalid), never internal state.
 * Deterministic (no network, no timers); jsdom pointer/scroll polyfills live in
 * `src/test/setup.ts`.
 */

const OPTIONS: ComboboxOption[] = [
  {
    value: "vcb",
    label: "Vietcombank",
    keywords: ["Ngân hàng TMCP Ngoại thương Việt Nam", "970436", "VCB"],
  },
  {
    value: "tcb",
    label: "Techcombank",
    keywords: ["Ngân hàng TMCP Kỹ thương Việt Nam", "970407", "TCB"],
  },
  {
    value: "dab",
    label: "DongABank",
    keywords: ["Ngân hàng TMCP Đông Á", "970406", "DAB"],
  },
];

function Harness({
  initial,
  onChange,
  options = OPTIONS,
  ...props
}: Partial<ComboboxProps> & {
  initial?: string;
  onChange?: (value: string) => void;
}) {
  const [value, setValue] = useState<string | undefined>(initial);
  return (
    <>
      <Combobox
        label="Ngân hàng"
        placeholder="Chọn ngân hàng"
        searchPlaceholder="Tìm ngân hàng"
        emptyLabel="Không tìm thấy ngân hàng"
        {...props}
        value={value}
        options={options}
        onValueChange={(next) => {
          setValue(next);
          onChange?.(next);
        }}
      />
      <button type="button">outside</button>
    </>
  );
}

/** The collapsed trigger — the button whose accessible name carries the label. */
function trigger(): HTMLElement {
  return screen.getByRole("button", { name: /Ngân hàng/ });
}

describe("Combobox rendering", () => {
  it("Combobox_Closed_RendersLabelAndPlaceholderAndNoListbox", () => {
    render(<Harness />);
    expect(screen.getByText("Ngân hàng")).toBeInTheDocument();
    expect(screen.getByText("Chọn ngân hàng")).toBeInTheDocument();
    // Collapsed: no popover, aria-expanded=false, haspopup=listbox.
    expect(trigger()).toHaveAttribute("aria-expanded", "false");
    expect(trigger()).toHaveAttribute("aria-haspopup", "listbox");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("Combobox_SelectedValue_MirrorsOptionContentIntoTrigger", () => {
    render(<Harness initial="tcb" />);
    // The selected option's label is rendered inside the trigger (Select parity).
    expect(
      screen.getByRole("button", { name: /Techcombank/ }),
    ).toBeInTheDocument();
    expect(screen.queryByText("Chọn ngân hàng")).not.toBeInTheDocument();
  });
});

describe("Combobox open/close", () => {
  it("Combobox_ClickTrigger_OpensAndFocusesSearchInput", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(trigger());
    // The search input (role=combobox) appears and takes focus.
    const search = screen.getByRole("combobox", { name: "Ngân hàng" });
    await waitFor(() => expect(search).toHaveFocus());
    expect(trigger()).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("listbox", { name: "Ngân hàng" })).toBeInTheDocument();
  });

  it("Combobox_ArrowDownOnTrigger_OpensPanel", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    trigger().focus();
    await user.keyboard("{ArrowDown}");
    expect(await screen.findByRole("listbox")).toBeInTheDocument();
  });

  it("Combobox_EnterOnTrigger_OpensPanel", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    trigger().focus();
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("listbox")).toBeInTheDocument();
  });

  it("Combobox_Escape_ClosesWithoutChangingValue", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(trigger());
    await screen.findByRole("listbox");
    await user.keyboard("{Escape}");

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("Combobox_OutsidePointerDown_Closes", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(trigger());
    await screen.findByRole("listbox");
    // A pointerdown outside the field root closes it (TagMultiSelect parity).
    fireEvent.pointerDown(screen.getByText("outside"));

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("Combobox_TabOutOfSearch_ClosesPanelAndAdvancesFocus", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(trigger());
    const search = await screen.findByRole("combobox", { name: "Ngân hàng" });
    await waitFor(() => expect(search).toHaveFocus());

    // Tab must close the absolutely-positioned panel (else it overlaps the
    // field below) while focus still advances — value is unchanged.
    await user.tab();

    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(search).not.toHaveFocus();
    expect(onChange).not.toHaveBeenCalled();
  });
});

describe("Combobox filtering", () => {
  it("Combobox_TypeQuery_FiltersOptionsByLabel", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(trigger());
    await user.type(screen.getByRole("combobox", { name: "Ngân hàng" }), "techcom");

    const options = screen.getAllByRole("option");
    expect(options).toHaveLength(1);
    expect(options[0]).toHaveTextContent("Techcombank");
  });

  it("Combobox_TypeKeyword_FiltersDiacriticInsensitively", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(trigger());
    // "dong a" (no diacritics) matches the "Đông Á" keyword (đ→d fold).
    await user.type(screen.getByRole("combobox", { name: "Ngân hàng" }), "dong a");

    const options = screen.getAllByRole("option");
    expect(options).toHaveLength(1);
    expect(options[0]).toHaveTextContent("DongABank");
  });

  it("Combobox_NoMatch_ShowsEmptyLabelAndNoOptions", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(trigger());
    await user.type(screen.getByRole("combobox", { name: "Ngân hàng" }), "zzzzz");

    expect(screen.queryAllByRole("option")).toHaveLength(0);
    expect(screen.getByText("Không tìm thấy ngân hàng")).toBeInTheDocument();
  });
});

describe("Combobox keyboard selection", () => {
  it("Combobox_ArrowKeys_MoveActiveDescendantAndEnterSelects", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(trigger());
    const search = screen.getByRole("combobox", { name: "Ngân hàng" });
    // On open (no value) the first enabled option is active.
    const firstActive = search.getAttribute("aria-activedescendant");
    expect(firstActive).toBeTruthy();
    expect(document.getElementById(firstActive!)).toHaveTextContent("Vietcombank");

    await user.keyboard("{ArrowDown}");
    const secondActive = search.getAttribute("aria-activedescendant");
    expect(secondActive).not.toBe(firstActive);
    const active = document.getElementById(secondActive!);
    expect(active).toHaveAttribute("role", "option");
    expect(active).toHaveTextContent("Techcombank");

    // Enter commits the active option and closes.
    await user.keyboard("{Enter}");
    expect(onChange).toHaveBeenLastCalledWith("tcb");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("Combobox_HomeEnd_JumpToFirstAndLastOption", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(trigger());
    await user.keyboard("{End}");
    await user.keyboard("{Enter}");
    // End jumps to the last option (DongABank).
    expect(onChange).toHaveBeenLastCalledWith("dab");
  });

  it("Combobox_ClickOption_EmitsValueAndCloses", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(trigger());
    await user.click(await screen.findByRole("option", { name: "Techcombank" }));

    expect(onChange).toHaveBeenLastCalledWith("tcb");
    // Selected content mirrored into the (now closed) trigger.
    expect(
      screen.getByRole("button", { name: /Techcombank/ }),
    ).toBeInTheDocument();
  });
});

describe("Combobox ARIA + states", () => {
  it("Combobox_SelectedOption_HasAriaSelectedTrue", async () => {
    const user = userEvent.setup();
    render(<Harness initial="tcb" />);

    await user.click(trigger());
    expect(
      await screen.findByRole("option", { name: "Techcombank", selected: true }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("option", { name: "Vietcombank", selected: false }),
    ).toBeInTheDocument();
  });

  it("Combobox_Disabled_CannotOpen", async () => {
    const user = userEvent.setup();
    render(<Harness disabled />);

    expect(trigger()).toBeDisabled();
    await user.click(trigger());
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("Combobox_ErrorAndRequired_WireAriaInvalidAndDescribedBy", () => {
    render(<Harness required error="Vui lòng chọn ngân hàng." />);

    const btn = trigger();
    expect(btn).toHaveAttribute("aria-invalid", "true");
    // The error is announced (role=alert) and referenced by aria-describedby.
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Vui lòng chọn ngân hàng.");
    expect(btn.getAttribute("aria-describedby")).toBe(alert.id);
    // The required marker is present (aria-hidden, but in the DOM).
    expect(screen.getByText("*")).toBeInTheDocument();
  });

  it("Combobox_Hint_WiredViaAriaDescribedByWhenValid", () => {
    render(<Harness hint="Chọn ngân hàng phát hành tài khoản." />);

    const btn = trigger();
    const hint = screen.getByText("Chọn ngân hàng phát hành tài khoản.");
    expect(btn.getAttribute("aria-describedby")).toBe(hint.id);
    expect(btn).not.toHaveAttribute("aria-invalid");
  });

  it("Combobox_LoadingString_RendersInAriaLiveHint", async () => {
    const user = userEvent.setup();
    render(<Harness loading="Đang cập nhật danh sách ngân hàng…" />);

    await user.click(trigger());
    const loading = screen.getByText("Đang cập nhật danh sách ngân hàng…");
    expect(loading.closest('[aria-live="polite"]')).toBeInTheDocument();
  });
});

describe("normalizeForSearch", () => {
  it("NormalizeForSearch_IsCaseInsensitive", () => {
    expect(normalizeForSearch("TECHCOM")).toBe("techcom");
    expect(normalizeForSearch("VCB")).toBe("vcb");
  });

  it("NormalizeForSearch_StripsVietnameseDiacritics", () => {
    // "ky thuong" (no diacritics) equals normalized "Kỹ thương".
    expect(normalizeForSearch("Kỹ thương")).toBe("ky thuong");
  });

  it("NormalizeForSearch_FoldsDStrokeToD", () => {
    // NFD does not decompose đ/Đ — the explicit fold makes "dong a" match "Đông Á".
    expect(normalizeForSearch("Đông Á")).toBe("dong a");
  });

  it("NormalizeForSearch_LeavesDigitsUnchanged", () => {
    expect(normalizeForSearch("970407")).toBe("970407");
  });

  it("NormalizeForSearch_EnablesSubstringMatchAcrossFolds", () => {
    // The filter is a substring match over the normalized haystack.
    expect(normalizeForSearch("Techcombank").includes(normalizeForSearch("techcom"))).toBe(true);
    expect(normalizeForSearch("Đông Á").includes(normalizeForSearch("dong a"))).toBe(true);
  });
});
