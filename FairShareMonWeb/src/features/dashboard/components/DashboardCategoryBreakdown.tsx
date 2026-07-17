import { Link } from "react-router-dom";
import {
  Button,
  Card,
  EmptyState,
  ErrorState,
  Skeleton,
  Stack,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { thisMonthRequest } from "@/features/stats/dateRange";
import {
  useByCategoryQuery,
  useOverviewQuery,
} from "@/features/stats/hooks/useStats";
import { CategoryBarChart } from "@/features/stats/components/CategoryBarChart";
import statsStyles from "@/features/stats/components/stats.module.css";
import styles from "./dashboard.module.css";

const TOP_N = 5;
const SKELETON_BARS = 4;

/**
 * The home's compact category breakdown (OQ5a) — the top-5 ranked bars for this
 * month, chart-only (the full accessible table lives on `/stats`, one click away
 * via the header link). Shares the this-month overview + by-category caches with
 * the KPI row above (no extra network).
 */
export function DashboardCategoryBreakdown() {
  const { t } = useT();
  const request = thisMonthRequest();
  const byCategoryQuery = useByCategoryQuery(request);
  const overviewQuery = useOverviewQuery(request);
  const overviewTotal = overviewQuery.data?.totalSpending ?? 0;

  const rows = byCategoryQuery.data?.rows.slice(0, TOP_N) ?? [];

  return (
    <Card>
      <div className={styles.cardHeadRow}>
        <span className={styles.cardTitle}>
          {t("common:home.categoryBreakdown")}
        </span>
        <Link className={styles.viewAll} to="/stats">
          {t("common:home.viewAll")}
        </Link>
      </div>

      {byCategoryQuery.isError ? (
        <ErrorState
          title={t("stats:byCategory.error")}
          description={resolveErrorMessage(byCategoryQuery.error, t)}
          action={
            <Button
              variant="secondary"
              onClick={() => void byCategoryQuery.refetch()}
            >
              {t("stats:states.retry")}
            </Button>
          }
        />
      ) : byCategoryQuery.isPending ? (
        <div className={`${statsStyles.chart} ${statsStyles.compactChart}`} aria-hidden="true">
          {Array.from({ length: SKELETON_BARS }).map((_, i) => (
            <div key={i} className={statsStyles.barRow}>
              <div className={statsStyles.barHeader}>
                <Skeleton width="8rem" />
                <Skeleton width="5rem" />
              </div>
              <Skeleton width="100%" height="0.75rem" />
            </div>
          ))}
        </div>
      ) : rows.length === 0 ? (
        <EmptyState title={t("common:home.categoryEmpty")} />
      ) : (
        <Stack gap="3">
          <CategoryBarChart rows={rows} overviewTotal={overviewTotal} compact />
        </Stack>
      )}
    </Card>
  );
}
