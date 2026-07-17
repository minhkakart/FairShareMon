import type { ReactNode } from "react";
import { Card, Skeleton } from "@/components/ui";
import styles from "./stats.module.css";

export interface StatTileProps {
  /** Small label above the value. */
  label: ReactNode;
  /** The large value (a `<Money>` for currency, a formatted count, etc.). */
  value?: ReactNode;
  /** Optional sub-label under the value. */
  hint?: ReactNode;
  /** Show a skeleton value while the source query is pending. */
  loading?: boolean;
}

/**
 * A single KPI tile: label + big value + optional hint, built on `Card`. Purely
 * presentational (feature-local per OQ5a). Loading swaps the value for a skeleton
 * of stable height so the tile does not jump.
 */
export function StatTile({ label, value, hint, loading }: StatTileProps) {
  return (
    <Card>
      <div className={styles.statTile}>
        <span className={styles.statLabel}>{label}</span>
        {loading ? (
          <span className={styles.statValueSkeleton}>
            <Skeleton width="7rem" height="1.75rem" />
          </span>
        ) : (
          value
        )}
        {hint && !loading ? <span className={styles.statHint}>{hint}</span> : null}
      </div>
    </Card>
  );
}
