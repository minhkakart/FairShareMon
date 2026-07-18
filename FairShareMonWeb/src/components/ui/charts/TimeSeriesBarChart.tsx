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
 * The most axis labels we let the chart draw before it starts thinning (showing
 * every Nth `periodLabel`). Dense buckets (a ~31-day range, ~53 weeks) would
 * otherwise pack the axis with columns too narrow to read a label under; thinning
 * keeps the axis legible while EVERY column (and the paired table) still renders —
 * no data is dropped, only some decorative axis captions are skipped. ~12 mirrors
 * the "≤ 12 month buckets" density the value caps target.
 */
const MAX_AXIS_LABELS = 12;

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
 * Phone density (cycle-2 2b): each column keeps a legible `min-width`, and the
 * plot + its aligned axis share ONE `overflow-x: auto` scroller so a dense range
 * scrolls the chart box (not the page) with labels staying column-aligned —
 * matching the app's `Table` "scroll the data box" idiom. Above ~12 buckets the
 * axis thins to every Nth `periodLabel` so captions never collide; the columns
 * and the paired table always carry the full series.
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

  // Show every `stride`-th axis label so the densest ranges stay legible; the
  // hidden slots still occupy their column width, so labels stay aligned.
  const stride = Math.max(1, Math.ceil(items.length / MAX_AXIS_LABELS));
  const thinned = stride > 1;

  return (
    <div className={cx(styles.tsChart, className)}>
      <div className={styles.tsScroll}>
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
        <div
          className={cx(styles.tsAxis, thinned && styles.tsAxisThinned)}
          aria-hidden="true"
        >
          {items.map((item, index) => (
            <span key={item.key} className={styles.tsAxisLabel}>
              {index % stride === 0 ? item.periodLabel : null}
            </span>
          ))}
        </div>
      </div>
    </div>
  );
}
