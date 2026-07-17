/**
 * `expenseTime` <-> `<input type="datetime-local">` conversion. The native
 * control works in the viewer's local timezone (which matches the app's
 * `getTimeZone()` — the browser default). We submit offset-aware ISO-8601
 * (UTC `Z`), and the API presents it back in the viewer's zone via `X-Time-Zone`.
 */

const pad = (n: number) => String(n).padStart(2, "0");

/** ISO-8601 -> "YYYY-MM-DDTHH:mm" in the viewer's local zone (for the input). */
export function isoToDateTimeLocal(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "";
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(
    date.getDate(),
  )}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** "YYYY-MM-DDTHH:mm" (viewer local) -> offset-aware ISO-8601 (UTC) for submit. */
export function dateTimeLocalToIso(local: string): string {
  const date = new Date(local);
  if (Number.isNaN(date.getTime())) return local;
  return date.toISOString();
}

/** A `datetime-local` value for "now", seeding the create form. */
export function nowDateTimeLocal(): string {
  return isoToDateTimeLocal(new Date().toISOString());
}

/**
 * A date-only "YYYY-MM-DD" (from the filter's date inputs) -> an inclusive ISO
 * bound in the viewer's local zone. `end` pushes to the last millisecond of the
 * day so the `to` filter is inclusive of that whole day.
 */
export function dateBoundToIso(date: string, end: boolean): string | undefined {
  if (!date) return undefined;
  const parsed = new Date(`${date}T${end ? "23:59:59.999" : "00:00:00.000"}`);
  if (Number.isNaN(parsed.getTime())) return undefined;
  return parsed.toISOString();
}
