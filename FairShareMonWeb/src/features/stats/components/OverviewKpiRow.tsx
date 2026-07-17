import { Button, Card, ErrorState, Money } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount } from "@/i18n/format";
import type { OverviewStatsResponse } from "../api/types";
import { StatTile } from "./StatTile";
import styles from "./stats.module.css";

export interface OverviewKpiRowProps {
  data?: OverviewStatsResponse;
  loading?: boolean;
  /** Localized error message; when set the row renders a compact ErrorState. */
  error?: string;
  onRetry?: () => void;
}

/**
 * The overview KPI row: total spending (`<Money>`, the exact API value) and
 * expense count. Loading → skeleton tiles; error → compact ErrorState; a zero
 * range renders `0` tiles (valid data, not an empty state). No "average per
 * expense" tile — that would be float math on money (R3).
 */
export function OverviewKpiRow({
  data,
  loading,
  error,
  onRetry,
}: OverviewKpiRowProps) {
  const { t } = useT();

  if (error) {
    return (
      <Card>
        <ErrorState
          title={t("stats:kpi.errorTitle")}
          description={error}
          action={
            onRetry ? (
              <Button variant="secondary" onClick={onRetry}>
                {t("stats:states.retry")}
              </Button>
            ) : undefined
          }
        />
      </Card>
    );
  }

  if (loading || !data) {
    return (
      <div className={styles.kpiRow}>
        <StatTile label={t("stats:kpi.totalSpending")} loading />
        <StatTile label={t("stats:kpi.expenseCount")} loading />
      </div>
    );
  }

  return (
    <div className={styles.kpiRow}>
      <StatTile
        label={t("stats:kpi.totalSpending")}
        value={<Money amount={data.totalSpending} size="xl" />}
      />
      <StatTile
        label={t("stats:kpi.expenseCount")}
        value={
          <span className={styles.statValue}>
            {formatCount(data.expenseCount)}
          </span>
        }
        hint={t("stats:kpi.expenseCountHint")}
      />
    </div>
  );
}
