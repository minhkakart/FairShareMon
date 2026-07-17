import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import type { Locale } from "@/components/ui";
import { setActiveLocale } from "@/lib/api/runtime";

import viCommon from "./locales/vi-VN/common.json";
import viAuth from "./locales/vi-VN/auth.json";
import viErrors from "./locales/vi-VN/errors.json";
import viValidation from "./locales/vi-VN/validation.json";
import viSettings from "./locales/vi-VN/settings.json";
import viMembers from "./locales/vi-VN/members.json";
import viCategories from "./locales/vi-VN/categories.json";
import viTags from "./locales/vi-VN/tags.json";
import viExpenses from "./locales/vi-VN/expenses.json";
import viEvents from "./locales/vi-VN/events.json";
import viStats from "./locales/vi-VN/stats.json";
import enCommon from "./locales/en-US/common.json";
import enAuth from "./locales/en-US/auth.json";
import enErrors from "./locales/en-US/errors.json";
import enValidation from "./locales/en-US/validation.json";
import enSettings from "./locales/en-US/settings.json";
import enMembers from "./locales/en-US/members.json";
import enCategories from "./locales/en-US/categories.json";
import enTags from "./locales/en-US/tags.json";
import enExpenses from "./locales/en-US/expenses.json";
import enEvents from "./locales/en-US/events.json";
import enStats from "./locales/en-US/stats.json";

export const SUPPORTED_LOCALES = ["vi-VN", "en-US"] as const;
export const DEFAULT_LOCALE: Locale = "vi-VN";
export const LOCALE_STORAGE_KEY = "fsm.locale";

/** Namespaces + resources; vi-VN is the source of truth for key typing. */
export const resources = {
  "vi-VN": {
    common: viCommon,
    auth: viAuth,
    errors: viErrors,
    validation: viValidation,
    settings: viSettings,
    members: viMembers,
    categories: viCategories,
    tags: viTags,
    expenses: viExpenses,
    events: viEvents,
    stats: viStats,
  },
  "en-US": {
    common: enCommon,
    auth: enAuth,
    errors: enErrors,
    validation: enValidation,
    settings: enSettings,
    members: enMembers,
    categories: enCategories,
    tags: enTags,
    expenses: enExpenses,
    events: enEvents,
    stats: enStats,
  },
} as const;

export const NAMESPACES = [
  "common",
  "auth",
  "errors",
  "validation",
  "settings",
  "members",
  "categories",
  "tags",
  "expenses",
  "events",
  "stats",
] as const;

function isLocale(value: string | null): value is Locale {
  return (
    value !== null && (SUPPORTED_LOCALES as readonly string[]).includes(value)
  );
}

export function readStoredLocale(): Locale {
  try {
    const stored = window.localStorage.getItem(LOCALE_STORAGE_KEY);
    if (isLocale(stored)) return stored;
  } catch {
    // ignore
  }
  return DEFAULT_LOCALE;
}

void i18n.use(initReactI18next).init({
  resources,
  lng: readStoredLocale(),
  fallbackLng: DEFAULT_LOCALE,
  supportedLngs: SUPPORTED_LOCALES as unknown as string[],
  ns: NAMESPACES as unknown as string[],
  defaultNS: "common",
  interpolation: { escapeValue: false },
  returnNull: false,
});

// Seed the API client's Accept-Language from the very first request onward.
setActiveLocale(i18n.language as Locale);

export { i18n };
export default i18n;
