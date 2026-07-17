import { KpiRow, KpiTile, KpiValue, Money } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount } from "@/i18n/format";
import type { RevenueResponse } from "../../api/types";

export interface RevenueKpiRowProps {
  data?: RevenueResponse;
  loading?: boolean;
}

/**
 * The revenue KPI row: total revenue (`<Money>`, the exact API value — never
 * client-summed, R3) + grant count. Loading → skeleton tiles; zeros are valid data.
 */
export function RevenueKpiRow({ data, loading }: RevenueKpiRowProps) {
  const { t } = useT();

  if (loading || !data) {
    return (
      <KpiRow>
        <KpiTile label={t("admin:revenue.kpi.total")} loading />
        <KpiTile label={t("admin:revenue.kpi.grantCount")} loading />
      </KpiRow>
    );
  }

  return (
    <KpiRow>
      <KpiTile
        label={t("admin:revenue.kpi.total")}
        value={<Money amount={data.totalRevenue} size="xl" />}
        hint={t("admin:revenue.kpi.totalHint")}
      />
      <KpiTile
        label={t("admin:revenue.kpi.grantCount")}
        value={<KpiValue>{formatCount(data.grantCount)}</KpiValue>}
      />
    </KpiRow>
  );
}
