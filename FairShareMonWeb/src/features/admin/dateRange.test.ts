import { describe, expect, it } from "vitest";
import {
  DEFAULT_RANGE,
  isCustomRangeInvalid,
  rangeToRequest,
} from "./dateRange";
import type { RangeValue } from "./dateRange";

/**
 * Admin dashboard date-range logic (mirrors the M6 stats presets + a bucket
 * toggle). Deterministic via the pinned Asia/Ho_Chi_Minh (+07) timezone. Proves:
 * All-time omits both bounds (all-time query); a custom range resolves to the
 * inclusive offset-aware ISO bounds (start-of-day / end-of-day at +07) and carries
 * the bucket; a preset carries both bounds + the bucket; and `isCustomRangeInvalid`
 * only fires for an inverted custom range with both bounds set.
 */

function range(overrides: Partial<RangeValue>): RangeValue {
  return { preset: "custom", from: "", to: "", bucket: "month", ...overrides };
}

describe("rangeToRequest", () => {
  it("RangeToRequest_AllTime_OmitsBothBoundsKeepsBucket", () => {
    const req = rangeToRequest(range({ preset: "allTime", bucket: "day" }));
    expect(req).toEqual({ bucket: "day" });
    expect(req.from).toBeUndefined();
    expect(req.to).toBeUndefined();
  });

  it("RangeToRequest_Custom_ResolvesInclusiveIsoBoundsAtPlus07", () => {
    const req = rangeToRequest(
      range({ preset: "custom", from: "2026-06-01", to: "2026-06-30", bucket: "month" }),
    );
    // Start-of-day and end-of-day at +07, expressed as UTC (offset-aware).
    expect(req.from).toBe("2026-05-31T17:00:00.000Z");
    expect(req.to).toBe("2026-06-30T16:59:59.999Z");
    expect(req.bucket).toBe("month");
  });

  it("RangeToRequest_Preset_CarriesBothBoundsAndBucket", () => {
    const req = rangeToRequest(range({ preset: "thisYear", bucket: "month" }));
    expect(req.from).toBeTruthy();
    expect(req.to).toBeTruthy();
    expect(req.bucket).toBe("month");
  });
});

describe("isCustomRangeInvalid", () => {
  it("IsCustomRangeInvalid_InvertedCustomRange_IsTrue", () => {
    expect(
      isCustomRangeInvalid(range({ from: "2026-06-30", to: "2026-06-01" })),
    ).toBe(true);
  });

  it("IsCustomRangeInvalid_EqualOrForwardBounds_IsFalse", () => {
    expect(
      isCustomRangeInvalid(range({ from: "2026-06-01", to: "2026-06-01" })),
    ).toBe(false);
    expect(
      isCustomRangeInvalid(range({ from: "2026-06-01", to: "2026-06-30" })),
    ).toBe(false);
  });

  it("IsCustomRangeInvalid_EmptyBoundOrNonCustom_IsFalse", () => {
    expect(isCustomRangeInvalid(range({ from: "2026-06-30", to: "" }))).toBe(false);
    expect(
      isCustomRangeInvalid(range({ preset: "thisYear", from: "2026-06-30", to: "2026-06-01" })),
    ).toBe(false);
  });
});

describe("DEFAULT_RANGE", () => {
  it("DefaultRange_IsThisYearBucketedByMonth", () => {
    expect(DEFAULT_RANGE.preset).toBe("thisYear");
    expect(DEFAULT_RANGE.bucket).toBe("month");
  });
});
