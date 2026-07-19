import type { EventSummaryResponse } from "@/features/events/api/types";

/**
 * Dashboard ordering for the "Recent events" card (pure, unit-testable).
 *
 * Partitions events into OPEN (`!isClosed`) first, then CLOSED; within each
 * partition sorts by `updatedAt` DESC, tie-breaking on `createdAt` DESC then
 * `startDate` DESC (OQ-E). Returns `[...open, ...closed]`; does not slice — the
 * component slices to `RECENT_N`.
 *
 * Timestamps are compared via `Date.parse`; an unparseable/absent value sorts
 * last within its comparison (treated as `-Infinity`) so a momentarily missing
 * `updatedAt` never floats an event to the top.
 */
export function sortEventsForDashboard(
  events: EventSummaryResponse[],
): EventSummaryResponse[] {
  const time = (iso: string | null | undefined): number => {
    const parsed = iso ? Date.parse(iso) : NaN;
    return Number.isNaN(parsed) ? Number.NEGATIVE_INFINITY : parsed;
  };

  const byRecency = (a: EventSummaryResponse, b: EventSummaryResponse): number =>
    time(b.updatedAt) - time(a.updatedAt) ||
    time(b.createdAt) - time(a.createdAt) ||
    time(b.startDate) - time(a.startDate);

  const open = events.filter((e) => !e.isClosed).sort(byRecency);
  const closed = events.filter((e) => e.isClosed).sort(byRecency);
  return [...open, ...closed];
}
