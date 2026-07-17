import { Button, EmptyState, ErrorState, Skeleton, Stack } from "@/components/ui";
import { useT } from "@/i18n/useT";
import type { ByCategoryStatsResponse } from "../api/types";
import { CategoryBarChart } from "./CategoryBarChart";
import { CategoryStatsTable } from "./CategoryStatsTable";
import styles from "./stats.module.css";

export interface CategoryBreakdownProps {
  data?: ByCategoryStatsResponse;
  /** Authoritative overview total (share denominator + table footer). */
  overviewTotal: number;
  /** Authoritative overview count for the table footer. */
  overviewCount?: number;
  loading?: boolean;
  /** Localized error message; when set an ErrorState is shown. */
  error?: string;
  onRetry?: () => void;
}

const SKELETON_BARS = 5;

/**
 * Composes the category bar chart with its always-present accessible table from a
 * `ByCategoryStatsResponse` (+ the overview total for the share denominator and
 * footer). Handles loading (skeleton bars), empty (`EmptyState`), and error
 * (`ErrorState` with retry).
 */
export function CategoryBreakdown({
  data,
  overviewTotal,
  overviewCount,
  loading,
  error,
  onRetry,
}: CategoryBreakdownProps) {
  const { t } = useT();

  if (error) {
    return (
      <ErrorState
        title={t("stats:byCategory.error")}
        description={error}
        action={
          onRetry ? (
            <Button variant="secondary" onClick={onRetry}>
              {t("stats:states.retry")}
            </Button>
          ) : undefined
        }
      />
    );
  }

  if (loading || !data) {
    return (
      <div className={styles.chart} aria-hidden="true">
        {Array.from({ length: SKELETON_BARS }).map((_, i) => (
          <div key={i} className={styles.barRow}>
            <div className={styles.barHeader}>
              <Skeleton width="10rem" />
              <Skeleton width="6rem" />
            </div>
            <Skeleton width="100%" height="0.75rem" />
          </div>
        ))}
      </div>
    );
  }

  if (data.rows.length === 0) {
    return (
      <EmptyState
        title={t("stats:byCategory.empty")}
        description={t("stats:byCategory.emptyBody")}
      />
    );
  }

  return (
    <Stack gap="5">
      <CategoryBarChart rows={data.rows} overviewTotal={overviewTotal} />
      <CategoryStatsTable
        rows={data.rows}
        overviewTotal={overviewTotal}
        overviewCount={overviewCount}
      />
    </Stack>
  );
}
