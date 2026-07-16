import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test/utils";
import { useTheme } from "./ThemeProvider";
import { ThemeToggle } from "@/components/ui";

/**
 * ThemeProvider owns the `[data-theme]` contract: `light`/`dark` stamp + win,
 * `system` removes the attribute (follow the OS). The choice persists.
 */

function ThemeHarness() {
  const { theme, setTheme } = useTheme();
  return (
    <ThemeToggle
      value={theme}
      onChange={setTheme}
      labels={{ light: "Sáng", dark: "Tối", system: "Theo hệ thống" }}
      groupLabel="Giao diện"
    />
  );
}

beforeEach(() => {
  window.localStorage.clear();
  document.documentElement.removeAttribute("data-theme");
});

afterEach(() => {
  window.localStorage.clear();
  document.documentElement.removeAttribute("data-theme");
});

describe("theme", () => {
  it("Theme_SelectDark_StampsDataThemeAndPersists", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ThemeHarness />);

    await user.click(screen.getByRole("radio", { name: "Tối" }));

    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(window.localStorage.getItem("fsm.theme")).toBe("dark");
  });

  it("Theme_SelectLight_StampsLightDataTheme", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ThemeHarness />);

    await user.click(screen.getByRole("radio", { name: "Sáng" }));

    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(window.localStorage.getItem("fsm.theme")).toBe("light");
  });

  it("Theme_SelectSystem_RemovesDataThemeAttribute", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ThemeHarness />);

    // Force a value first, then back to system.
    await user.click(screen.getByRole("radio", { name: "Tối" }));
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");

    await user.click(screen.getByRole("radio", { name: "Theo hệ thống" }));
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
    expect(window.localStorage.getItem("fsm.theme")).toBe("system");
  });

  it("Theme_Toggle_ReflectsCheckedStateForA11y", async () => {
    const user = userEvent.setup();
    renderWithProviders(<ThemeHarness />);

    await user.click(screen.getByRole("radio", { name: "Tối" }));
    expect(screen.getByRole("radio", { name: "Tối" })).toBeChecked();
    expect(screen.getByRole("radio", { name: "Sáng" })).not.toBeChecked();
  });
});
