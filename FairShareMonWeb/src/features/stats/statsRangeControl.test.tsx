import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { StatsRangeControl } from "./components/StatsRangeControl";
import { DEFAULT_RANGE } from "./dateRange";
import type { RangeValue } from "./dateRange";

/**
 * StatsRangeControl — preset chips + a custom two-date mode (OQ4a). Controlled;
 * a small stateful harness mirrors the page's ownership so interactions are
 * observable. Proves: `role="group"` + label; the active preset carries
 * `aria-pressed` (state is not color-alone); default is "This month"; switching to
 * Custom reveals the two date inputs; an inverted custom range (`from > to`) is
 * blocked client-side with an inline message (`role="alert"`); a server `1001`
 * message surfaces inline; the chips are keyboard-reachable with accessible names.
 */

function Harness({
  initial = DEFAULT_RANGE,
  apiError,
}: {
  initial?: RangeValue;
  apiError?: string;
}) {
  const [value, setValue] = useState<RangeValue>(initial);
  return (
    <StatsRangeControl value={value} onChange={setValue} apiError={apiError} />
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("StatsRangeControl", () => {
  it("StatsRangeControl_IsALabeledGroupOfPresetChips", () => {
    renderWithProviders(<Harness />);
    const group = screen.getByRole("group", { name: "Khoảng thời gian" });
    expect(group).toBeInTheDocument();
    for (const label of ["Tháng này", "30 ngày qua", "Năm nay", "Tất cả", "Tùy chỉnh"]) {
      expect(within(group).getByRole("button", { name: label })).toBeInTheDocument();
    }
  });

  it("StatsRangeControl_DefaultThisMonth_IsTheOnlyPressedChip", () => {
    renderWithProviders(<Harness />);
    // Active state is carried by aria-pressed (not color alone).
    expect(screen.getByRole("button", { name: "Tháng này" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(screen.getByRole("button", { name: "Năm nay" })).toHaveAttribute(
      "aria-pressed",
      "false",
    );
  });

  it("StatsRangeControl_SelectPreset_MovesThePressedState", async () => {
    renderWithProviders(<Harness />);
    await userEvent.click(screen.getByRole("button", { name: "Năm nay" }));
    expect(screen.getByRole("button", { name: "Năm nay" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(screen.getByRole("button", { name: "Tháng này" })).toHaveAttribute(
      "aria-pressed",
      "false",
    );
  });

  it("StatsRangeControl_CustomMode_RevealsTheTwoDateInputs", async () => {
    renderWithProviders(<Harness />);
    // No custom inputs until Custom is chosen.
    expect(screen.queryByLabelText("Từ ngày")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Tùy chỉnh" }));
    expect(screen.getByLabelText("Từ ngày")).toBeInTheDocument();
    expect(screen.getByLabelText("Đến ngày")).toBeInTheDocument();
  });

  it("StatsRangeControl_CustomFromAfterTo_ShowsInlineInvalidMessage", () => {
    // Start already inverted so the guard fires without brittle date typing.
    renderWithProviders(
      <Harness initial={{ preset: "custom", from: "2026-03-20", to: "2026-03-05" }} />,
    );
    // A single invalid-range message is announced via the `to` field's error
    // (the redundant standalone alert was removed — M6 review nit).
    expect(
      screen.getByText("“Đến ngày” phải sau hoặc bằng “Từ ngày”."),
    ).toBeInTheDocument();
    // The `to` field carries the localized rule message and is flagged invalid.
    expect(screen.getByLabelText("Đến ngày")).toHaveAttribute(
      "aria-invalid",
      "true",
    );
  });

  it("StatsRangeControl_CustomValidRange_ShowsNoInvalidMessage", () => {
    renderWithProviders(
      <Harness initial={{ preset: "custom", from: "2026-03-05", to: "2026-03-20" }} />,
    );
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("StatsRangeControl_ServerBadRange_SurfacesTheApiMessageInline", () => {
    renderWithProviders(<Harness apiError="Khoảng thời gian không hợp lệ." />);
    // A server-returned 1001 message surfaces on the control verbatim.
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Khoảng thời gian không hợp lệ.",
    );
  });

  it("StatsRangeControl_Chips_AreKeyboardReachableWithAccessibleNames", async () => {
    renderWithProviders(<Harness />);
    await userEvent.tab();
    // The first focusable control is the first preset chip, with an accessible name.
    expect(screen.getByRole("button", { name: "Tháng này" })).toHaveFocus();
  });
});
