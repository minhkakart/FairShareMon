import type { CSSProperties, ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./charts.module.css";

export interface TimeSeriesBarItem {
  /** Stable React key (usually the period bound / ISO bucket). */
  key: string;
  /** Axis label under the column (e.g. `07/2026` for a month, `16/07` a day). */
  periodLabel: ReactNode;
  /**
   * Column-height ratio in 0..1 (the tallest bucket = 1). The CALLER computes it
   * off the API's integer values (`value / maxValue`) — the chart never touches
   * money math (R3).
   */
  ratio: number;
  /** The value shown on the column cap (a count or a `<Money>`). */
  value: ReactNode;
  /** Native hover tooltip text for the column (full "period: value" sentence). */
  title?: string;
}

export interface TimeSeriesBarChartProps {
  /** Buckets in ascending time order — rendered verbatim (no client re-sort). */
  items: TimeSeriesBarItem[];
  /**
   * A summarizing label for the `role="img"` region (localized). The paired
   * accessible TABLE (composed by the feature) is the data channel for assistive
   * tech, so this only needs to summarize the trend.
   */
  ariaLabel: string;
  /**
   * Show the value on each column cap. Default true (good for ≤ ~12 month
   * buckets). Set false for dense day buckets where caps would collide — the
   * paired table still carries every value, so nothing is gated.
   */
  showValues?: boolean;
  className?: string;
}

/**
 * A vertical column chart over ordered time buckets — the net-new shared chart
 * for the Admin signups + revenue dashboards (and any future over-time series).
 *
 * dataviz: this is ONE measure over time → ONE hue. The column fill is a single
 * sequential step (`--fs-viz-seq-500`, which clears 3:1 on both surfaces); every
 * label / axis / cap wears text tokens. Marks: column ≤ 2.75rem wide, square at
 * the baseline + 4px rounded data-end (top), ≥ 2px surface gaps, a recessive
 * baseline axis. The region is `role="img"` with a summarizing `aria-label` and
 * the columns are `aria-hidden` (the paired table is the data channel).
 * `prefers-reduced-motion` disables the grow transition.
 *
 * Renders nothing for an empty list — the caller shows its own `EmptyState`.
 */
export function TimeSeriesBarChart({
  items,
  ariaLabel,
  showValues = true,
  className,
}: TimeSeriesBarChartProps) {
  if (items.length === 0) return null;

  return (
    <div className={cx(styles.tsChart, className)}>
      <div className={styles.tsPlot} role="img" aria-label={ariaLabel}>
        {items.map((item) => {
          const heightPct = Math.max(0, Math.min(1, item.ratio)) * 100;
          return (
            <div
              key={item.key}
              className={styles.tsCol}
              aria-hidden="true"
              title={item.title}
            >
              {showValues ? (
                <span className={styles.tsCap}>{item.value}</span>
              ) : null}
              <div
                className={styles.tsBar}
                style={{ height: `${heightPct}%` } as CSSProperties}
              />
            </div>
          );
        })}
      </div>
      <div className={styles.tsAxis} aria-hidden="true">
        {items.map((item) => (
          <span key={item.key} className={styles.tsAxisLabel}>
            {item.periodLabel}
          </span>
        ))}
      </div>
    </div>
  );
}
