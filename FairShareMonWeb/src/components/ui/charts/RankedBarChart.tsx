import type { CSSProperties, ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./charts.module.css";

export interface RankedBarItem {
  /** Stable React key + identity — color follows the ENTITY, never the rank. */
  key: string;
  /**
   * The row's direct identity label — a SLOT. The stats charts pass a
   * `<CategoryMarker>` (+ an `(đã xóa)` tag); the admin distribution charts pass
   * plain text or a `<Badge>`. Whatever is passed carries identity so meaning
   * never rests on the bar color (relief-rule + color-independence).
   */
  label: ReactNode;
  /** The primary value node shown on the header line (a `<Money>` or a count). */
  value: ReactNode;
  /**
   * Bar-length ratio in 0..1 (the longest bar = 1). The CALLER computes it off
   * the API's integer totals (`total / maxTotal`) — the chart never touches
   * money math (R3).
   */
  ratio: number;
  /** Optional trailing secondary value (e.g. a `42%` share). Text tokens. */
  meta?: ReactNode;
  /**
   * Optional explicit fill color (a CSS color / var). Default: the categorical
   * slot by rank (`--fs-viz-cat-1..8`, 9th+ → a muted neutral). Pass this only
   * when identity must be pinned to the entity across a changing series count.
   */
  color?: string;
}

export interface RankedBarChartProps {
  /** Rows in the API's rank order (longest first) — rendered verbatim. */
  items: RankedBarItem[];
  /**
   * A summarizing label for the `role="img"` region (localized). The chart is
   * decorative for assistive tech — the paired accessible TABLE (composed by the
   * feature) is the data channel, so this only needs to summarize.
   */
  ariaLabel: string;
  /** Tighter spacing for a compact/embedded breakdown. */
  compact?: boolean;
  className?: string;
}

/**
 * A ranked horizontal-bar list — generalized from the M6 `CategoryBarChart` so
 * Stats (M6) and Admin (M8) share one bar system.
 *
 * dataviz: the bar FILL is the only element wearing a `--fs-viz-*` color — slot
 * `--fs-viz-cat-1..8` by rank, a 9th+ folds to `--fs-viz-ink-muted` (color stops
 * distinguishing but the label + value still carry identity). Every label/value
 * wears text tokens; each row ships a direct label + value so the light-mode
 * relief rule is satisfied. The region is `role="img"` with a summarizing
 * `aria-label`; the bars are `aria-hidden` because the paired table carries the
 * data. `prefers-reduced-motion` disables the grow transition.
 *
 * Renders nothing for an empty list — the caller shows its own `EmptyState`.
 */
export function RankedBarChart({
  items,
  ariaLabel,
  compact,
  className,
}: RankedBarChartProps) {
  if (items.length === 0) return null;

  return (
    <div
      className={cx(styles.rankedChart, compact && styles.rankedCompact, className)}
      role="img"
      aria-label={ariaLabel}
    >
      {items.map((item, i) => {
        const barColor =
          item.color ??
          (i < 8 ? `var(--fs-viz-cat-${i + 1})` : "var(--fs-viz-ink-muted)");
        const widthPct = Math.max(0, Math.min(1, item.ratio)) * 100;
        return (
          <div key={item.key} className={styles.barRow} aria-hidden="true">
            <div className={styles.barHeader}>
              <span className={styles.barLabel}>{item.label}</span>
              <span className={styles.barValue}>
                {item.value}
                {item.meta != null ? (
                  <span className={styles.barMeta}>{item.meta}</span>
                ) : null}
              </span>
            </div>
            <div className={styles.barTrack}>
              <div
                className={styles.barFill}
                style={
                  {
                    width: `${widthPct}%`,
                    "--bar-color": barColor,
                  } as CSSProperties
                }
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}
