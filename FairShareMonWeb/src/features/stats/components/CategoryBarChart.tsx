import { CategoryMarker, Money, RankedBarChart } from "@/components/ui";
import type { RankedBarItem } from "@/components/ui";
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
 * A thin adapter over the shared `RankedBarChart` (M8 OQ1a): maps
 * `CategoryStatRow[]` → `RankedBarItem[]` so the M6 category breakdown rides the
 * one shared bar system. The label SLOT carries the `CategoryMarker` (+ the
 * `(đã xóa)` tag) and the value slot a `<Money>`, so identity never rests on the
 * bar fill color; `ratio = total / maxTotal` and `meta = total / overviewTotal`
 * are display-only ratios off integer totals (no money math — R3).
 *
 * Renders nothing for an empty list — the `CategoryBreakdown` owns the empty state.
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

  const items: RankedBarItem[] = rows.map((row) => ({
    key: row.categoryUuid,
    label: (
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
    ),
    value: <Money amount={row.total} size="sm" />,
    ratio: maxTotal > 0 ? row.total / maxTotal : 0,
    meta:
      overviewTotal > 0
        ? `${Math.round((row.total / overviewTotal) * 100)}%`
        : "0%",
  }));

  return <RankedBarChart items={items} ariaLabel={ariaLabel} compact={compact} />;
}
