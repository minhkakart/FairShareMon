import { beforeAll, describe, expect, it } from "vitest";
import { setActiveLocale } from "@/lib/api/runtime";
import { formatDate } from "@/i18n/format";
import { dateInputToIso, formatRange, isoToDateInput } from "./dateRange";

/**
 * Date-range helper (OQ5a — noon anchoring). The suite runs under the pinned
 * TZ `Asia/Ho_Chi_Minh` (+07, from test/setup.ts). The critical invariant: the
 * calendar day the user picks must survive the "YYYY-MM-DD" → ISO → "YYYY-MM-DD"
 * round-trip with NO ±1-day drift, because a midnight anchor could straddle the
 * UTC boundary. Noon anchoring pins the day.
 */

describe("dateInputToIso (noon anchoring, OQ5a)", () => {
  it("DateInputToIso_KnownDate_IsNoonLocalOfThatDay", () => {
    const iso = dateInputToIso("2026-07-16");
    // Offset-aware (serialized as UTC `Z`)…
    expect(iso.endsWith("Z")).toBe(true);
    // …and it resolves back to local noon of the SAME calendar day.
    const local = new Date(iso);
    expect(local.getHours()).toBe(12);
    expect(local.getFullYear()).toBe(2026);
    expect(local.getMonth()).toBe(6); // July (0-based)
    expect(local.getDate()).toBe(16);
  });

  it("DateInputToIso_UnderPlus07_EmitsFiveAmUtc", () => {
    // 12:00 at +07 == 05:00Z — deterministic proof the anchor is local noon, not
    // UTC midnight (which would risk a boundary drift).
    expect(dateInputToIso("2026-07-16")).toBe("2026-07-16T05:00:00.000Z");
  });

  it("DateInputToIso_EmptyString_ReturnedUnchanged", () => {
    expect(dateInputToIso("")).toBe("");
  });
});

describe("isoToDateInput (prefill)", () => {
  it("IsoToDateInput_OffsetAwareStartBound_YieldsIntendedDay", () => {
    // A backend day-bound (00:00 local at +07) prefills to that calendar day.
    expect(isoToDateInput("2026-07-12T00:00:00+07:00")).toBe("2026-07-12");
  });

  it("IsoToDateInput_OffsetAwareEndBound_YieldsIntendedDay", () => {
    // 23:59:59 local at +07 stays on the same day (no drift into the next day).
    expect(isoToDateInput("2026-07-18T23:59:59.999999+07:00")).toBe("2026-07-18");
  });

  it("IsoToDateInput_InvalidIso_ReturnsEmptyString", () => {
    expect(isoToDateInput("not-a-date")).toBe("");
  });
});

describe("round-trip (no ±1-day drift)", () => {
  it("DateRange_RoundTrip_PreservesCalendarDayAcrossBoundaries", () => {
    // Includes month/year edges and a leap day — every one must round-trip.
    const days = [
      "2026-01-01",
      "2026-02-28",
      "2028-02-29",
      "2026-06-30",
      "2026-07-01",
      "2026-07-16",
      "2026-12-31",
    ];
    for (const day of days) {
      expect(isoToDateInput(dateInputToIso(day))).toBe(day);
    }
  });
});

describe("formatRange", () => {
  beforeAll(() => setActiveLocale("vi-VN"));

  it("FormatRange_TwoIsoDates_JoinsWithEnDashInViewerZone", () => {
    const startIso = dateInputToIso("2026-07-12");
    const endIso = dateInputToIso("2026-07-18");
    const rendered = formatRange(startIso, endIso);
    // Composition + order: "start – end" using the shared date formatter.
    expect(rendered).toBe(`${formatDate(startIso)} – ${formatDate(endIso)}`);
    expect(rendered).toContain("–");
    // Both endpoints' day-of-month appear (rendered in the +07 viewer zone).
    expect(rendered).toContain("12");
    expect(rendered).toContain("18");
  });
});
