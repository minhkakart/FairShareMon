import { Link } from "react-router-dom";
import { useT } from "@/i18n/useT";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { thisMonthRequest } from "@/features/stats/dateRange";
import { useOverviewQuery } from "@/features/stats/hooks/useStats";
import { OverviewKpiRow } from "@/features/stats/components/OverviewKpiRow";
import styles from "./dashboard.module.css";

/**
 * Home this-month KPI row. Reuses the shared overview query (this-month key —
 * deduped with the compact breakdown below) and renders `OverviewKpiRow` under a
 * header linking to the full Stats page.
 */
export function DashboardOverview() {
  const { t } = useT();
  const request = thisMonthRequest();
  const overviewQuery = useOverviewQuery(request);

  return (
    <div>
      <div className={styles.cardHeadRow}>
        <span className={styles.cardTitle}>{t("common:home.overviewTitle")}</span>
        <Link className={styles.viewAll} to="/stats">
          {t("common:home.viewStats")}
        </Link>
      </div>
      <OverviewKpiRow
        data={overviewQuery.data}
        loading={overviewQuery.isPending}
        error={
          overviewQuery.isError
            ? resolveErrorMessage(overviewQuery.error, t)
            : undefined
        }
        onRetry={() => void overviewQuery.refetch()}
      />
    </div>
  );
}
