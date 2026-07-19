import { dateBoundToIso } from "@/features/expenses/dateTime";
import type { AdminMetricsRequest, Bucket } from "./api/types";

/**
 * Date-range presets + bucket toggle for the admin dashboards (M8). Mirrors the
 * M6 stats preset shape (`dateBoundToIso` → offset-aware ISO bounds, timezone-aware
 * by construction) and adds a month/day bucket for the over-time charts. "All time"
 * omits both bounds; "Custom" carries the two date inputs verbatim.
 */
export type RangePreset =
  | "thisMonth"
  | "last30Days"
  | "thisYear"
  | "allTime"
  | "custom";

export interface RangeValue {
  preset: RangePreset;
  /** `YYYY-MM-DD` (only meaningful in `custom` mode). */
  from: string;
  to: string;
  bucket: Bucket;
}

const pad = (n: number) => String(n).padStart(2, "0");

function toYmd(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function presetDays(preset: RangePreset): { from: string; to: string } {
  const now = new Date();
  const to = toYmd(now);
  let from: Date;
  switch (preset) {
    case "thisMonth":
      from = new Date(now.getFullYear(), now.getMonth(), 1);
      break;
    case "last30Days":
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

/** Default: this year, bucketed by month (a sensible operator overview). */
export const DEFAULT_RANGE: RangeValue = {
  preset: "thisYear",
  from: "",
  to: "",
  bucket: "month",
};

/** True when a custom range has both bounds and is inverted (`from > to`). */
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
 * otherwise resolve to an all-time request and flash all-time figures until both
 * dates are picked — so the query stays disabled until the range is set.
 */
export function isCustomRangeIncomplete(value: RangeValue): boolean {
  return value.preset === "custom" && (value.from === "" || value.to === "");
}

/**
 * Resolve a `RangeValue` to the dashboard request (`{ from?, to?, bucket }`).
 * All-time omits both bounds; every other preset (and custom) → inclusive ISO
 * bounds. An empty custom bound is omitted (matching the optional-bound contract).
 */
export function rangeToRequest(value: RangeValue): AdminMetricsRequest {
  if (value.preset === "allTime") return { bucket: value.bucket };
  if (value.preset === "custom") {
    return {
      from: dateBoundToIso(value.from, false),
      to: dateBoundToIso(value.to, true),
      bucket: value.bucket,
    };
  }
  const { from, to } = presetDays(value.preset);
  return {
    from: dateBoundToIso(from, false),
    to: dateBoundToIso(to, true),
    bucket: value.bucket,
  };
}
