import { KpiRow, KpiTile, KpiValue } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount } from "@/i18n/format";
import type { AdminMetricsResponse } from "../../api/types";

export interface MetricsKpiRowProps {
  data?: AdminMetricsResponse;
  loading?: boolean;
}

/** Count from a distribution by key (0 when absent — valid data, not empty). */
function countFor(
  dist: AdminMetricsResponse["statusDistribution"] | undefined,
  key: string,
): number {
  return dist?.find((d) => d.key === key)?.count ?? 0;
}

/**
 * The metrics KPI row: total users + active users. Counts only — NO ledger figure
 * of any kind (R10). Loading → skeleton tiles; zeros are valid data (`0`), not an
 * empty state.
 */
export function MetricsKpiRow({ data, loading }: MetricsKpiRowProps) {
  const { t } = useT();

  if (loading || !data) {
    return (
      <KpiRow>
        <KpiTile label={t("admin:dashboard.kpi.totalUsers")} loading />
        <KpiTile label={t("admin:dashboard.kpi.activeUsers")} loading />
      </KpiRow>
    );
  }

  return (
    <KpiRow>
      <KpiTile
        label={t("admin:dashboard.kpi.totalUsers")}
        value={<KpiValue>{formatCount(data.totalUsers)}</KpiValue>}
        hint={t("admin:dashboard.kpi.totalUsersHint")}
      />
      <KpiTile
        label={t("admin:dashboard.kpi.activeUsers")}
        value={
          <KpiValue>{formatCount(countFor(data.statusDistribution, "ACTIVE"))}</KpiValue>
        }
      />
    </KpiRow>
  );
}
