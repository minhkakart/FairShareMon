import { describe, expect, it } from "vitest";
import { sortEventsForDashboard } from "./eventOrdering";
import type { EventSummaryResponse } from "@/features/events/api/types";

/**
 * `sortEventsForDashboard` — the pure ordering helper behind the dashboard
 * "Recent events" card. Contract (OQ-E): OPEN events first, then CLOSED; within
 * each group by `updatedAt` DESC, tie-breaking on `createdAt` DESC then
 * `startDate` DESC; absent/unparseable timestamps sort LAST within their group
 * (never float to the top); does not slice. Deterministic, no React/network.
 */

function makeSummary(
  overrides: Partial<EventSummaryResponse> = {},
): EventSummaryResponse {
  return {
    uuid: "ev-x",
    name: "Đợt",
    startDate: "2026-07-01T00:00:00+07:00",
    endDate: "2026-07-05T23:59:59+07:00",
    isClosed: false,
    closedAt: null,
    expenseCount: 0,
    createdAt: "2026-07-01T00:00:00+00:00",
    totalAdvanced: 0,
    updatedAt: "2026-07-01T00:00:00+00:00",
    ...overrides,
  };
}

const uuids = (events: EventSummaryResponse[]) => events.map((e) => e.uuid);

describe("sortEventsForDashboard grouping", () => {
  it("SortEventsForDashboard_MixedStatuses_PlacesAllOpenBeforeAllClosed", () => {
    const input = [
      makeSummary({ uuid: "c1", isClosed: true, updatedAt: "2026-07-19T00:00:00Z" }),
      makeSummary({ uuid: "o1", isClosed: false, updatedAt: "2026-07-10T00:00:00Z" }),
      makeSummary({ uuid: "c2", isClosed: true, updatedAt: "2026-07-18T00:00:00Z" }),
      makeSummary({ uuid: "o2", isClosed: false, updatedAt: "2026-07-11T00:00:00Z" }),
    ];
    const sorted = sortEventsForDashboard(input);
    // Every open event precedes every closed event, regardless of updatedAt.
    const closedStart = sorted.findIndex((e) => e.isClosed);
    expect(sorted.slice(0, closedStart).every((e) => !e.isClosed)).toBe(true);
    expect(sorted.slice(closedStart).every((e) => e.isClosed)).toBe(true);
    expect(closedStart).toBe(2);
  });

  it("SortEventsForDashboard_DoesNotSlice_ReturnsEveryInputEvent", () => {
    const input = Array.from({ length: 9 }, (_, i) =>
      makeSummary({ uuid: `e${i}`, updatedAt: `2026-07-${10 + i}T00:00:00Z` }),
    );
    expect(sortEventsForDashboard(input)).toHaveLength(9);
  });

  it("SortEventsForDashboard_EmptyInput_ReturnsEmptyArray", () => {
    expect(sortEventsForDashboard([])).toEqual([]);
  });
});

describe("sortEventsForDashboard within-group ordering", () => {
  it("SortEventsForDashboard_WithinGroup_OrdersByUpdatedAtDesc", () => {
    const input = [
      makeSummary({ uuid: "a", updatedAt: "2026-07-10T00:00:00Z" }),
      makeSummary({ uuid: "b", updatedAt: "2026-07-19T00:00:00Z" }),
      makeSummary({ uuid: "c", updatedAt: "2026-07-15T00:00:00Z" }),
    ];
    expect(uuids(sortEventsForDashboard(input))).toEqual(["b", "c", "a"]);
  });

  it("SortEventsForDashboard_EqualUpdatedAt_TieBreaksByCreatedAtDesc", () => {
    const sameUpdated = "2026-07-19T00:00:00Z";
    const input = [
      makeSummary({ uuid: "older", updatedAt: sameUpdated, createdAt: "2026-07-01T00:00:00Z" }),
      makeSummary({ uuid: "newer", updatedAt: sameUpdated, createdAt: "2026-07-05T00:00:00Z" }),
    ];
    expect(uuids(sortEventsForDashboard(input))).toEqual(["newer", "older"]);
  });

  it("SortEventsForDashboard_EqualUpdatedAtAndCreatedAt_TieBreaksByStartDateDesc", () => {
    const same = "2026-07-19T00:00:00Z";
    const input = [
      makeSummary({
        uuid: "earlyStart",
        updatedAt: same,
        createdAt: same,
        startDate: "2026-06-01T00:00:00+07:00",
      }),
      makeSummary({
        uuid: "lateStart",
        updatedAt: same,
        createdAt: same,
        startDate: "2026-07-01T00:00:00+07:00",
      }),
    ];
    expect(uuids(sortEventsForDashboard(input))).toEqual(["lateStart", "earlyStart"]);
  });
});

describe("sortEventsForDashboard missing/unparseable timestamps", () => {
  it("SortEventsForDashboard_UnparseableUpdatedAt_SortsLastNeverFloatsToTop", () => {
    const input = [
      makeSummary({ uuid: "garbage", updatedAt: "not-a-date" }),
      makeSummary({ uuid: "valid", updatedAt: "2026-07-19T00:00:00Z" }),
      // Empty string is also unparseable (Date.parse("") → NaN).
      makeSummary({ uuid: "empty", updatedAt: "" }),
    ];
    const sorted = sortEventsForDashboard(input);
    // The event with a real timestamp leads; the unparseable ones sink to the end.
    expect(sorted[0].uuid).toBe("valid");
    expect(new Set(uuids(sorted).slice(1))).toEqual(new Set(["garbage", "empty"]));
  });

  it("SortEventsForDashboard_AbsentUpdatedAt_DoesNotOutrankAValidTimestamp", () => {
    // A momentarily-absent updatedAt (API lag) must not outrank a dated peer.
    const input = [
      makeSummary({ uuid: "absent", updatedAt: undefined as unknown as string }),
      makeSummary({ uuid: "dated", updatedAt: "2026-01-01T00:00:00Z" }),
    ];
    expect(sortEventsForDashboard(input)[0].uuid).toBe("dated");
  });
});

describe("sortEventsForDashboard stability", () => {
  it("SortEventsForDashboard_FullyEqualKeys_PreservesInputOrder", () => {
    const k = "2026-07-19T00:00:00Z";
    const input = [
      makeSummary({ uuid: "first", updatedAt: k, createdAt: k, startDate: k }),
      makeSummary({ uuid: "second", updatedAt: k, createdAt: k, startDate: k }),
      makeSummary({ uuid: "third", updatedAt: k, createdAt: k, startDate: k }),
    ];
    expect(uuids(sortEventsForDashboard(input))).toEqual(["first", "second", "third"]);
  });
});
