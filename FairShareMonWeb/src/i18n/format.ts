import { getActiveLocale, getTimeZone } from "@/lib/api/runtime";

export { getTimeZone };

/**
 * VND formatter — formats the API-computed value; NEVER does arithmetic on
 * money. The API returns a decimal string/number; we parse for display only.
 * VND has 0 fraction digits and vi-VN grouping.
 */
const vndFormatter = new Intl.NumberFormat("vi-VN", {
  style: "currency",
  currency: "VND",
  maximumFractionDigits: 0,
});

export function formatMoneyVnd(value: string | number): string {
  const amount = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(amount)) return String(value);
  return vndFormatter.format(amount);
}

/**
 * Datetimes arrive offset-aware ISO-8601 and are presented in the VIEWER's
 * timezone (the browser default). Formatting follows the active UI locale.
 */
export function formatDateTime(iso: string, locale?: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(locale ?? getActiveLocale(), {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: getTimeZone(),
  }).format(date);
}

export function formatDate(iso: string, locale?: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(locale ?? getActiveLocale(), {
    dateStyle: "medium",
    timeZone: getTimeZone(),
  }).format(date);
}
