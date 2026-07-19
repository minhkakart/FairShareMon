import { dateBoundToIso } from "@/features/expenses/dateTime";
import type { StatsRangeRequest } from "./api/types";

/**
 * Date-range presets for the Stats page + home (OQ4a). A preset maps to inclusive
 * local-day bounds converted to offset-aware ISO exactly like the M4/M5 filters
 * (`dateBoundToIso`), so the range is timezone-aware by construction (it matches
 * the browser-default `X-Time-Zone` the client sends). "All time" omits both
 * bounds. "Custom" carries the two date inputs verbatim.
 */
export type RangePreset =
  | "thisMonth"
  | "last30Days"
  | "thisYear"
  | "allTime"
  | "custom";

/** The controlled value the range control owns. `from`/`to` are `YYYY-MM-DD`
 *  (only meaningful in `custom` mode). */
export interface RangeValue {
  preset: RangePreset;
  from: string;
  to: string;
}

const pad = (n: number) => String(n).padStart(2, "0");

/** A local `Date` → `YYYY-MM-DD` in the viewer's zone. */
function toYmd(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The local-day `YYYY-MM-DD` bounds for a non-custom, non-all-time preset. */
function presetDays(preset: RangePreset): { from: string; to: string } {
  const now = new Date();
  const to = toYmd(now);
  let from: Date;
  switch (preset) {
    case "thisMonth":
      from = new Date(now.getFullYear(), now.getMonth(), 1);
      break;
    case "last30Days":
      // 30 calendar days inclusive of today.
      from = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 29);
      break;
    case "thisYear":
      from = new Date(now.getFullYear(), 0, 1);
      break;
    default:
      from = now;
  }
  return { from: toYmd(from), to };
}

/** The default range for the Stats page and the home KPI tiles. */
export const DEFAULT_RANGE: RangeValue = {
  preset: "thisMonth",
  from: "",
  to: "",
};

/** True when a custom range has both bounds set and is inverted (`from > to`). */
export function isCustomRangeInvalid(value: RangeValue): boolean {
  return (
    value.preset === "custom" &&
    value.from !== "" &&
    value.to !== "" &&
    value.from > value.to
  );
}

/**
 * True while a custom range is still missing a bound. An empty bound would
 * otherwise resolve to `{}` (the all-time key) and flash all-time figures until
 * both dates are picked — so the query stays disabled until the range is set.
 */
export function isCustomRangeIncomplete(value: RangeValue): boolean {
  return value.preset === "custom" && (value.from === "" || value.to === "");
}

/**
 * Resolve a `RangeValue` to the API request. All-time → `{}` (both omitted);
 * every other preset (and custom) → inclusive ISO bounds. An empty custom bound
 * is simply omitted, matching the optional-bound contract.
 */
export function presetToRequest(value: RangeValue): StatsRangeRequest {
  if (value.preset === "allTime") return {};
  if (value.preset === "custom") {
    return {
      from: dateBoundToIso(value.from, false),
      to: dateBoundToIso(value.to, true),
    };
  }
  const { from, to } = presetDays(value.preset);
  return {
    from: dateBoundToIso(from, false),
    to: dateBoundToIso(to, true),
  };
}

/** The this-month request used by the home dashboard (same default as Stats). */
export function thisMonthRequest(): StatsRangeRequest {
  return presetToRequest(DEFAULT_RANGE);
}
