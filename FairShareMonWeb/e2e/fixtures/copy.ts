/**
 * Selector copy — the single source of truth for user-facing vi-VN strings the
 * specs select on (OQ4a). Import the SAME locale JSON the app renders, so a copy
 * edit updates the app and the selectors together (no drift). Relative imports
 * (not the `@/` alias) keep resolution trivial for the Playwright runner.
 *
 * The app default locale is vi-VN (`DEFAULT_LOCALE`, no stored override in a
 * fresh Playwright context), and the context pins `locale: "vi-VN"`, so the
 * rendered copy is always vi-VN.
 */
import common from "../../src/i18n/locales/vi-VN/common.json" with { type: "json" };
import auth from "../../src/i18n/locales/vi-VN/auth.json" with { type: "json" };
import members from "../../src/i18n/locales/vi-VN/members.json" with { type: "json" };
import expenses from "../../src/i18n/locales/vi-VN/expenses.json" with { type: "json" };
import events from "../../src/i18n/locales/vi-VN/events.json" with { type: "json" };

export const copy = { common, auth, members, expenses, events };

/**
 * Resolve an i18next-style `{{name}}` interpolation the way react-i18next does,
 * so specs can target interpolated copy (e.g. `events:assign.title`).
 */
export function interpolate(
  template: string,
  values: Record<string, string | number>,
): string {
  return template.replace(/\{\{\s*(\w+)\s*\}\}/g, (_, key: string) =>
    key in values ? String(values[key]) : `{{${key}}}`,
  );
}
