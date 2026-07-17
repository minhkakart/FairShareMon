import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  DEFAULT_RANGE,
  isCustomRangeInvalid,
  presetToRequest,
  thisMonthRequest,
} from "./dateRange";
import type { RangeValue } from "./dateRange";

/**
 * Range-preset date math (OQ4a). The suite runs under the pinned TZ
 * `Asia/Ho_Chi_Minh` (+07, from test/setup.ts) AND a pinned wall clock
 * (`2026-07-17T10:00:00+07:00`), because every preset derives its bounds from
 * `new Date()`. Each preset must map to the expected INCLUSIVE local-day bounds
 * converted to offset-aware ISO exactly like the M4/M5 filters (`dateBoundToIso`):
 * `from` = local 00:00 of the start day, `to` = local 23:59:59.999 of today.
 * "All time" omits both bounds. The `from` bound of each preset lands at 17:00Z
 * the previous day — deterministic proof the anchor is local, not UTC.
 */

const FIXED_NOW = new Date("2026-07-17T10:00:00+07:00");

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(FIXED_NOW);
});

afterEach(() => {
  vi.useRealTimers();
});

function custom(from: string, to: string): RangeValue {
  return { preset: "custom", from, to };
}

describe("presetToRequest — preset → inclusive ISO bounds (pinned TZ + clock)", () => {
  it("PresetToRequest_ThisMonth_IsFirstOfMonthToTodayInclusiveIso", () => {
    expect(presetToRequest({ preset: "thisMonth", from: "", to: "" })).toEqual({
      from: "2026-06-30T17:00:00.000Z", // local 2026-07-01T00:00:00 +07
      to: "2026-07-17T16:59:59.999Z", // local 2026-07-17T23:59:59.999 +07
    });
  });

  it("PresetToRequest_Last30Days_Is30CalendarDaysInclusiveOfToday", () => {
    // 30 days inclusive of today (2026-07-17) → from 2026-06-18.
    expect(presetToRequest({ preset: "last30Days", from: "", to: "" })).toEqual({
      from: "2026-06-17T17:00:00.000Z", // local 2026-06-18T00:00:00 +07
      to: "2026-07-17T16:59:59.999Z",
    });
  });

  it("PresetToRequest_ThisYear_IsJan1ToTodayInclusiveIso", () => {
    expect(presetToRequest({ preset: "thisYear", from: "", to: "" })).toEqual({
      from: "2025-12-31T17:00:00.000Z", // local 2026-01-01T00:00:00 +07
      to: "2026-07-17T16:59:59.999Z",
    });
  });

  it("PresetToRequest_AllTime_OmitsBothBounds", () => {
    // All-time = both bounds omitted (the client then drops the empty keys).
    expect(presetToRequest({ preset: "allTime", from: "", to: "" })).toEqual({});
  });

  it("PresetToRequest_Custom_UsesTheTwoDatesAsInclusiveIsoBounds", () => {
    expect(presetToRequest(custom("2026-03-05", "2026-03-20"))).toEqual({
      from: "2026-03-04T17:00:00.000Z", // local 2026-03-05T00:00:00 +07
      to: "2026-03-20T16:59:59.999Z", // local 2026-03-20T23:59:59.999 +07
    });
  });

  it("PresetToRequest_CustomWithEmptyBound_OmitsThatBoundOnly", () => {
    // An empty custom bound is simply omitted (matches the optional-bound contract).
    expect(presetToRequest(custom("2026-03-05", ""))).toEqual({
      from: "2026-03-04T17:00:00.000Z",
      to: undefined,
    });
  });
});

describe("DEFAULT_RANGE + thisMonthRequest", () => {
  it("DefaultRange_IsThisMonth_TheNonEmptyDefaultForStatsAndHome", () => {
    expect(DEFAULT_RANGE.preset).toBe("thisMonth");
  });

  it("ThisMonthRequest_MatchesTheThisMonthPreset_SoHomeAndStatsShareCaches", () => {
    // Same request shape → same query key → the home + Stats page dedupe.
    expect(thisMonthRequest()).toEqual(
      presetToRequest({ preset: "thisMonth", from: "", to: "" }),
    );
  });
});

describe("isCustomRangeInvalid — client-side from>to guard", () => {
  it("IsCustomRangeInvalid_CustomFromAfterTo_IsTrue", () => {
    expect(isCustomRangeInvalid(custom("2026-03-20", "2026-03-05"))).toBe(true);
  });

  it("IsCustomRangeInvalid_CustomFromEqualsTo_IsFalse", () => {
    // Inclusive [from,to]; equal bounds is a valid single-day range.
    expect(isCustomRangeInvalid(custom("2026-03-05", "2026-03-05"))).toBe(false);
  });

  it("IsCustomRangeInvalid_CustomWithAMissingBound_IsFalse", () => {
    // Not "invalid" until BOTH bounds are set and inverted.
    expect(isCustomRangeInvalid(custom("2026-03-20", ""))).toBe(false);
    expect(isCustomRangeInvalid(custom("", "2026-03-05"))).toBe(false);
  });

  it("IsCustomRangeInvalid_NonCustomPreset_IsAlwaysFalse", () => {
    // Presets can never be inverted, even with stale from/to left in state.
    expect(
      isCustomRangeInvalid({ preset: "thisMonth", from: "2026-12-31", to: "2026-01-01" }),
    ).toBe(false);
  });
});
