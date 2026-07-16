import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderWithProviders } from "@/test/utils";
import { useLocale } from "./LocaleProvider";
import { useT } from "./useT";
import { LanguageToggle } from "@/components/ui";
import i18n from "./index";
import { getActiveLocale, setActiveLocale } from "@/lib/api/runtime";

/**
 * Locale is the single owner of UI copy language, the API client's
 * Accept-Language header, and <html lang>. A switch must move all three.
 */

function LocaleHarness() {
  const { locale, setLocale } = useLocale();
  const { t } = useT();
  return (
    <div>
      <p data-testid="title">{t("auth:login.title")}</p>
      <LanguageToggle
        value={locale}
        onChange={setLocale}
        labels={{ "vi-VN": "Tiếng Việt", "en-US": "English" }}
        groupLabel="Ngôn ngữ"
      />
    </div>
  );
}

beforeEach(async () => {
  window.localStorage.clear();
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
  document.documentElement.lang = "vi";
});

afterEach(async () => {
  await i18n.changeLanguage("vi-VN");
  setActiveLocale("vi-VN");
  window.localStorage.clear();
});

describe("locale", () => {
  it("Locale_Default_RendersViVnCopy", () => {
    renderWithProviders(<LocaleHarness />);
    expect(screen.getByTestId("title")).toHaveTextContent("Đăng nhập");
    expect(document.documentElement.lang).toBe("vi");
    expect(getActiveLocale()).toBe("vi-VN");
  });

  it("Locale_ToggleToEnUs_SwitchesCopyAcceptLanguageAndHtmlLang", async () => {
    const user = userEvent.setup();
    renderWithProviders(<LocaleHarness />);

    await user.click(screen.getByRole("radio", { name: "English" }));

    // UI copy switches to en-US.
    await waitFor(() =>
      expect(screen.getByTestId("title")).toHaveTextContent("Log in"),
    );
    // API client Accept-Language + <html lang> follow.
    expect(getActiveLocale()).toBe("en-US");
    expect(document.documentElement.lang).toBe("en");
    // Persisted for the next boot.
    expect(window.localStorage.getItem("fsm.locale")).toBe("en-US");
  });

  it("Locale_ToggleBackToViVn_RestoresCopy", async () => {
    const user = userEvent.setup();
    renderWithProviders(<LocaleHarness />);

    await user.click(screen.getByRole("radio", { name: "English" }));
    await waitFor(() =>
      expect(screen.getByTestId("title")).toHaveTextContent("Log in"),
    );
    await user.click(screen.getByRole("radio", { name: "Tiếng Việt" }));
    await waitFor(() =>
      expect(screen.getByTestId("title")).toHaveTextContent("Đăng nhập"),
    );
    expect(getActiveLocale()).toBe("vi-VN");
    expect(document.documentElement.lang).toBe("vi");
  });
});
