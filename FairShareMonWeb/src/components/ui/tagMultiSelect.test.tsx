import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TagMultiSelect } from "./TagMultiSelect/TagMultiSelect";
import type { TagOption } from "./TagMultiSelect/TagMultiSelect";

/**
 * TagMultiSelect — the tag multi-select (OQ9a): selected tags read as removable
 * chips; a checkbox-list popover toggles membership. Controlled/presentational,
 * so a stateful harness drives it and a spy captures each emitted id array.
 * Keyboard-operable (native checkboxes + chip remove buttons); the popover closes
 * on Escape. The open/close toggle is located by its `aria-expanded` state (its
 * visible text is the placeholder when empty, the toggle label otherwise).
 */
const OPTIONS: TagOption[] = [
  { value: "t1", label: "Công tác" },
  { value: "t2", label: "Du lịch" },
  { value: "t3", label: "Ăn uống" },
];

function Harness({
  initial = [],
  onChange,
  options = OPTIONS,
}: {
  initial?: string[];
  onChange?: (value: string[]) => void;
  options?: TagOption[];
}) {
  const [value, setValue] = useState<string[]>(initial);
  return (
    <TagMultiSelect
      label="Nhãn"
      placeholder="Chưa gắn nhãn"
      value={value}
      onChange={(next) => {
        setValue(next);
        onChange?.(next);
      }}
      options={options}
      toggleLabel="Chọn nhãn"
      removeLabel={(l) => `Bỏ nhãn ${l}`}
      emptyLabel="Chưa có nhãn nào"
    />
  );
}

/** The popover toggle — the only button carrying aria-expanded. */
function toggleButton(): HTMLElement {
  return screen.getByRole("button", { expanded: false });
}

describe("TagMultiSelect", () => {
  it("TagMultiSelect_CheckOption_AddsIdToSelection", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness onChange={onChange} />);

    await user.click(toggleButton());
    await user.click(screen.getByRole("checkbox", { name: "Du lịch" }));

    expect(onChange).toHaveBeenLastCalledWith(["t2"]);
  });

  it("TagMultiSelect_SelectedTags_RenderAsChips", () => {
    render(<Harness initial={["t1", "t3"]} />);
    // Chips render in options order and carry a named remove button each.
    expect(
      screen.getByRole("button", { name: "Bỏ nhãn Công tác" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Bỏ nhãn Ăn uống" }),
    ).toBeInTheDocument();
  });

  it("TagMultiSelect_RemoveChip_RemovesIdFromSelection", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness initial={["t1", "t2"]} onChange={onChange} />);

    await user.click(screen.getByRole("button", { name: "Bỏ nhãn Công tác" }));
    expect(onChange).toHaveBeenLastCalledWith(["t2"]);
  });

  it("TagMultiSelect_UncheckOption_RemovesId", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Harness initial={["t1"]} onChange={onChange} />);

    await user.click(toggleButton());
    await user.click(screen.getByRole("checkbox", { name: "Công tác" }));
    expect(onChange).toHaveBeenLastCalledWith([]);
  });

  it("TagMultiSelect_EmptyOptions_ShowsEmptyMessage", async () => {
    const user = userEvent.setup();
    render(<Harness options={[]} />);

    await user.click(toggleButton());
    expect(screen.getByText("Chưa có nhãn nào")).toBeInTheDocument();
  });

  it("TagMultiSelect_Escape_ClosesPopover", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(toggleButton());
    expect(screen.getByRole("checkbox", { name: "Du lịch" })).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(
      screen.queryByRole("checkbox", { name: "Du lịch" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("button", { expanded: false })).toBeInTheDocument();
  });

  it("TagMultiSelect_CheckboxList_IsGroupedUnderTheLabel", async () => {
    const user = userEvent.setup();
    render(<Harness />);
    await user.click(toggleButton());
    const groups = screen.getAllByRole("group", { name: "Nhãn" });
    // The popover list is grouped under the field label with one checkbox each.
    expect(
      within(groups[groups.length - 1]).getAllByRole("checkbox"),
    ).toHaveLength(3);
  });
});
