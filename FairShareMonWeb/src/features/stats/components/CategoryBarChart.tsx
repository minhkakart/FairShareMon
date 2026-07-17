import type { CSSProperties } from "react";
import { CategoryMarker, Money } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { CategoryStatRow } from "../api/types";
import styles from "./stats.module.css";

export interface CategoryBarChartProps {
  /** Rows in the API's total-DESC order — rendered verbatim (no client re-sort). */
  rows: CategoryStatRow[];
  /** Authoritative overview total for the same range — the % share denominator. */
  overviewTotal: number;
  /** Tighter spacing for the compact home breakdown. */
  compact?: boolean;
}

/**
 * Hand-rolled horizontal bar breakdown (OQ1a/OQ2a). Bar length = row.total /
 * maxTotal (longest-bar normalization); % share = row.total / overviewTotal —
 * both display-only ratios off integer totals. No money is client-computed; every
 * money figure renders via `<Money>` (R3).
 *
 * dataviz: the bar FILL is the only thing wearing `--fs-viz-cat-*` (slots 1..8 by
 * rank; a 9th+ row folds to a muted neutral). Every label/value wears text tokens
 * and each bar ships a direct label (marker + name + value + %), so identity never
 * rests on the bar color (relief-rule + color-independence). The region is
 * `role="img"` with a summarizing aria-label; the bars are `aria-hidden` because
 * the paired `CategoryStatsTable` carries the data for assistive tech.
 */
export function CategoryBarChart({
  rows,
  overviewTotal,
  compact,
}: CategoryBarChartProps) {
  const { t } = useT();
  if (rows.length === 0) return null;

  const maxTotal = Math.max(...rows.map((r) => r.total));
  const ariaLabel = t(
    compact ? "stats:byCategory.chartLabelCompact" : "stats:byCategory.chartLabel",
    { categories: rows.length, top: rows[0].categoryName },
  );

  return (
    <div
      className={`${styles.chart}${compact ? ` ${styles.compactChart}` : ""}`}
      role="img"
      aria-label={ariaLabel}
    >
      {rows.map((row, i) => {
        // Bar fill: --fs-viz-cat-1..8 by rank; 9th+ → muted neutral.
        const barColor =
          i < 8 ? `var(--fs-viz-cat-${i + 1})` : "var(--fs-viz-ink-muted)";
        const widthPct = maxTotal > 0 ? (row.total / maxTotal) * 100 : 0;
        const sharePct =
          overviewTotal > 0 ? Math.round((row.total / overviewTotal) * 100) : 0;
        return (
          <div key={row.categoryUuid} className={styles.barRow} aria-hidden="true">
            <div className={styles.barHeader}>
              <span className={styles.barLabel}>
                <CategoryMarker
                  color={row.color}
                  icon={row.icon}
                  name={row.categoryName}
                  showLabel
                  size="sm"
                />
                {row.isDeleted ? (
                  <span className={styles.barDeletedTag}>
                    {t("stats:byCategory.deleted")}
                  </span>
                ) : null}
              </span>
              <span className={styles.barValue}>
                <Money amount={row.total} size="sm" />
                <span className={styles.barShare}>{sharePct}%</span>
              </span>
            </div>
            <div className={styles.barTrack}>
              <div
                className={styles.barFill}
                style={
                  { width: `${widthPct}%`, "--bar-color": barColor } as CSSProperties
                }
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}
