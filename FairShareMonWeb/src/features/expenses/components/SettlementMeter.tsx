import { cx } from "@/components/ui";
import styles from "./SettlementMeter.module.css";

export type SettlementMeterProps = {
  /** `EventMemberSettlement.ClearedAmount` — cumulative amount credited so far. */
  clearedAmount: number;
  /** The member's net owed amount in this event (NOT the original gross sum of
   *  their bills — see the component doc comment below on why that distinction
   *  matters). */
  netOwed: number;
  /** Inject the app's shared VND formatter (mirrors `Money`'s `format` prop —
   *  one formatter of record, never re-implemented here). */
  format: (amount: number) => string;
  /** Accessible name for the `role="progressbar"` (e.g. "Đã tất toán của Bình"). */
  accessibleLabel: string;
  className?: string;
};

/**
 * The partial-clearance money-metaphor (event-expense-settlement-sync,
 * 2026-08-25 — BA doc Design workstream item 3 / OQ-K input 2): a compact
 * fraction — "300.000đ / 500.000đ" — plus a two-segment bar, for a member
 * whose event-level `SettlementStatus` is `partial`.
 *
 * Dataviz-skill reasoning: this is a single part-to-whole proportion inline in
 * a dense table cell, not a categorical/sequential/diverging chart — so the
 * form is a compact meter, not a chart. Its ONE fill color is
 * `--fs-color-partial` (the SAME teal used by `SettlementStatusBadge`'s
 * partial tone), so the badge and the meter read as one visual language, not
 * two unrelated color decisions. The numeric fraction is the primary channel
 * (never color alone) — the bar is reinforcement, exactly like the polarity
 * word beside `Money variant="balance"`.
 *
 * IMPORTANT — legibility note (OQ-L corollary, BA doc): `netOwed` here MUST be
 * the member's current net owed amount (`max(balance, 0) inverted` /
 * `NetOwed` from the API), NEVER a client-side sum of that member's individual
 * settled share amounts. A member can be single-sided-by-net-balance yet still
 * hold a payer-share on one expense and a debtor-share on another — meaning
 * the sum of their per-share bills can legitimately exceed `netOwed`. Feeding
 * this component a locally-summed "total of their bills" instead of the
 * server's `netOwed` would make an accurate partial-clearance figure look like
 * a rendering bug. Always pass the server-computed value verbatim (D2).
 */
export function SettlementMeter({
  clearedAmount,
  netOwed,
  format,
  accessibleLabel,
  className,
}: SettlementMeterProps) {
  const safeTotal = netOwed > 0 ? netOwed : 0;
  const pct =
    safeTotal <= 0
      ? 0
      : Math.min(100, Math.max(0, (clearedAmount / safeTotal) * 100));

  return (
    <div className={cx(styles.root, className)}>
      <span className={styles.fraction}>
        {format(clearedAmount)}
        <span className={styles.fractionSlash} aria-hidden="true">
          {" / "}
        </span>
        {format(safeTotal)}
      </span>
      <span
        className={styles.track}
        role="progressbar"
        aria-label={accessibleLabel}
        aria-valuemin={0}
        aria-valuemax={safeTotal}
        aria-valuenow={Math.min(clearedAmount, safeTotal)}
      >
        <span className={styles.fill} style={{ width: `${pct}%` }} />
      </span>
    </div>
  );
}
