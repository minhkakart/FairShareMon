import { Badge } from "@/components/ui";
import { CheckIcon, ClockIcon, HalfCheckIcon } from "./icons";

/**
 * Local tri-state union, deliberately decoupled from the API's wire-format
 * enum (`EventSettlementStatus` — naming/casing TBD by web-feature-planner).
 * The caller maps whatever the API returns onto this shape, the same way
 * `SettledSwitch` takes a plain `isSettled: boolean` rather than the raw API
 * shape. Keeps this primitive stable even if the wire enum's names change.
 */
export type SettlementTriState = "unsettled" | "partial" | "settled";

export type SettlementStatusBadgeProps = {
  status: SettlementTriState;
  /** "chưa trả" */
  labelUnsettled: string;
  /** "đã trả một phần" */
  labelPartial: string;
  /** "đã trả" */
  labelSettled: string;
  className?: string;
};

/**
 * The 3-state settlement badge (event-expense-settlement-sync, 2026-08-25) —
 * replaces the old binary `tone={isSettled ? "settled" : "warning"}` used in
 * `EventBalanceTable`'s `StatusCell`. One shared component so the same visual
 * language (icon + tone + text) is used everywhere a settlement state renders
 * — this table's per-member net-clearance status AND, if a future screen ever
 * needs it, any other settlement surface — closing part of the OQ-K "unify the
 * settled notions" risk at the component level rather than by convention.
 *
 * Color-independent by construction: each state pairs a DISTINCT icon
 * silhouette (Clock / half-filled circle / Check) with a distinct text label —
 * never color alone (accessibility baseline, CLAUDE.md). The `partial` tone
 * (teal) is deliberately a new hue, not a blend of `warning`/`settled`, so it
 * cannot be misread as "close to unsettled" or "close to settled" on a hue
 * ramp — it is its own state.
 */
export function SettlementStatusBadge({
  status,
  labelUnsettled,
  labelPartial,
  labelSettled,
  className,
}: SettlementStatusBadgeProps) {
  if (status === "settled") {
    return (
      <Badge tone="settled" icon={<CheckIcon />} className={className}>
        {labelSettled}
      </Badge>
    );
  }
  if (status === "partial") {
    return (
      <Badge tone="partial" icon={<HalfCheckIcon />} className={className}>
        {labelPartial}
      </Badge>
    );
  }
  return (
    <Badge tone="warning" icon={<ClockIcon />} className={className}>
      {labelUnsettled}
    </Badge>
  );
}
