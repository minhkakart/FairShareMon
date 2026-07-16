import { createContext, use, useEffect, useState } from "react";
import type { ReactNode } from "react";
import type { Locale } from "@/components/ui";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n, { LOCALE_STORAGE_KEY, readStoredLocale } from "./index";

interface LocaleContextValue {
  locale: Locale;
  setLocale: (locale: Locale) => void;
}

const LocaleContext = createContext<LocaleContextValue | null>(null);

/** The `<html lang>` value (short subtag) for a locale. */
function htmlLang(locale: Locale): string {
  return locale.startsWith("vi") ? "vi" : "en";
}

/**
 * Owns the active locale (persisted). On change it drives i18next, the API
 * client's `Accept-Language` (so backend messages come back in the same
 * language), and `<html lang>` — the LanguageToggle only reports intent.
 */
export function LocaleProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(() => readStoredLocale());

  useEffect(() => {
    setActiveLocale(locale);
    if (i18n.language !== locale) void i18n.changeLanguage(locale);
    document.documentElement.lang = htmlLang(locale);
  }, [locale]);

  function setLocale(next: Locale) {
    try {
      window.localStorage.setItem(LOCALE_STORAGE_KEY, next);
    } catch {
      // persistence best-effort
    }
    setLocaleState(next);
  }

  return (
    <LocaleContext value={{ locale, setLocale }}>{children}</LocaleContext>
  );
}

export function useLocale(): LocaleContextValue {
  const ctx = use(LocaleContext);
  if (!ctx) throw new Error("useLocale must be used within LocaleProvider");
  return ctx;
}
