import { formatDate } from "@/i18n/format";

/**
 * Event date-range conversion (OQ5a — noon anchoring). The user picks calendar
 * dates via `<input type="date">` ("YYYY-MM-DD"). We submit each as an
 * offset-aware ISO at LOCAL NOON of that day in the viewer's zone. Noon-anchoring
 * makes the calendar date unambiguous under any DST/offset, so the backend's
 * day extraction (which re-normalizes to whole-day bounds) can never drift ±1
 * day. The backend re-normalizes to 00:00:00 / 23:59:59.999999 itself.
 */

const pad = (n: number) => String(n).padStart(2, "0");

/** ISO-8601 -> "YYYY-MM-DD" in the viewer's local zone (for pre-filling edit). */
export function isoToDateInput(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** "YYYY-MM-DD" -> offset-aware ISO at local noon of that day (OQ5a). */
export function dateInputToIso(date: string): string {
  if (!date) return date;
  const parsed = new Date(`${date}T12:00:00`);
  if (Number.isNaN(parsed.getTime())) return date;
  return parsed.toISOString();
}

/** A localized "start – end" range display string from two ISO datetimes. */
export function formatRange(startIso: string, endIso: string): string {
  return `${formatDate(startIso)} – ${formatDate(endIso)}`;
}
