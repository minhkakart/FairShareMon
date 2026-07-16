import { afterEach, describe, expect, it } from "vitest";
import { formatDate, formatDateTime, formatMoneyVnd } from "./format";
import { setActiveLocale } from "@/lib/api/runtime";

/**
 * Formatter determinism relies on the pinned TZ (Asia/Ho_Chi_Minh, UTC+7, set in
 * src/test/setup.ts). Money uses vi-VN grouping regardless of active locale.
 */

afterEach(() => {
  setActiveLocale("vi-VN");
});

describe("formatMoneyVnd", () => {
  it("FormatMoneyVnd_LargeNumber_UsesViVnGroupingAndSymbol", () => {
    const out = formatMoneyVnd(1500000);
    expect(out).toContain("1.500.000");
    expect(out).toContain("₫");
  });

  it("FormatMoneyVnd_StringInput_FormatsWithoutArithmetic", () => {
    expect(formatMoneyVnd("2500")).toContain("2.500");
  });

  it("FormatMoneyVnd_ZeroFractionDigits_RoundsForDisplay", () => {
    // VND has 0 fraction digits — the decimal is rounded for display only.
    expect(formatMoneyVnd(1234.56)).toContain("1.235");
  });

  it("FormatMoneyVnd_NonNumeric_ReturnsRawValue", () => {
    expect(formatMoneyVnd("not-a-number")).toBe("not-a-number");
  });
});

describe("formatDateTime", () => {
  it("FormatDateTime_UtcInstant_RendersInPinnedUtcPlus7Zone", () => {
    // 2026-07-16T00:00:00Z → 07:00 in UTC+7.
    const out = formatDateTime("2026-07-16T00:00:00Z", "en-US");
    expect(out).toContain("Jul 16, 2026");
    expect(out).toContain("7:00");
    expect(out).toContain("AM");
  });

  it("FormatDateTime_LateUtcInstant_RollsDateForwardInLocalZone", () => {
    // 2026-07-16T20:00:00Z → 2026-07-17 03:00 in UTC+7 (date rolls forward).
    const out = formatDateTime("2026-07-16T20:00:00Z", "en-US");
    expect(out).toContain("Jul 17, 2026");
    expect(out).toContain("3:00");
  });

  it("FormatDateTime_InvalidIso_ReturnsRawInput", () => {
    expect(formatDateTime("not-a-date")).toBe("not-a-date");
  });
});

describe("formatDate", () => {
  it("FormatDate_UtcInstant_RendersDateOnlyInLocalZone", () => {
    const out = formatDate("2026-07-16T20:00:00Z", "en-US");
    expect(out).toContain("Jul 17, 2026");
  });
});
