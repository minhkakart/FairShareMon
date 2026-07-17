import type { ReactNode } from "react";
import { Card } from "../Card/Card";
import { Skeleton } from "../Feedback/Skeleton";
import { cx } from "../utils/cx";
import styles from "./charts.module.css";

export interface KpiTileProps {
  /** Small label above the value (sentence case, no trailing colon). */
  label: ReactNode;
  /**
   * The large value. For currency pass a `<Money size="xl">` (it carries its own
   * sizing); for a count wrap a formatted number in `<KpiValue>` so it gets the
   * big tabular display treatment. Omit while `loading`.
   */
  value?: ReactNode;
  /** Optional sub-label under the value. */
  hint?: ReactNode;
  /** Show a stable-height skeleton in place of the value while the query pends. */
  loading?: boolean;
  className?: string;
}

/**
 * A single KPI tile: label + big value + optional hint, on a `Card`. Purely
 * presentational and generalized from the M6 `StatTile` — shared by the Stats
 * (M6) and Admin (M8) dashboards. Loading swaps the value for a skeleton of
 * stable height so the tile never jumps.
 *
 * A zero value is valid data (`0`), NOT an empty state — render `<KpiValue>0…`.
 */
export function KpiTile({ label, value, hint, loading, className }: KpiTileProps) {
  return (
    <Card className={className}>
      <div className={styles.kpiTile}>
        <span className={styles.kpiLabel}>{label}</span>
        {loading ? (
          <span className={styles.kpiValueSkeleton}>
            <Skeleton width="7rem" height="1.75rem" />
          </span>
        ) : (
          value
        )}
        {hint && !loading ? <span className={styles.kpiHint}>{hint}</span> : null}
      </div>
    </Card>
  );
}

/**
 * The big tabular display treatment for a KPI's value line — use for counts and
 * other non-`Money` figures (`<Money>` already brings its own size). Tabular
 * numerals keep a range-driven value from reflowing as it updates.
 */
export function KpiValue({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return <span className={cx(styles.kpiValue, className)}>{children}</span>;
}

/**
 * Responsive KPI row — tiles wrap and stretch (auto-fit + minmax): side-by-side
 * on wide viewports, stacked on narrow, with no breakpoint math. Drop `KpiTile`s
 * inside it.
 */
export function KpiRow({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return <div className={cx(styles.kpiRow, className)}>{children}</div>;
}
