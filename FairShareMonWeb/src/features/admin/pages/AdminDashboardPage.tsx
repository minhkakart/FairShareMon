import { useState } from "react";
import { Button, ErrorState, Stack } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { DEFAULT_RANGE, isCustomRangeInvalid, rangeToRequest } from "../dateRange";
import type { RangeValue } from "../dateRange";
import { useMetricsQuery } from "../hooks/useAdminDashboard";
import { AdminRangeControl } from "../components/dashboard/AdminRangeControl";
import { MetricsKpiRow } from "../components/dashboard/MetricsKpiRow";
import { DistributionPanel } from "../components/dashboard/DistributionPanel";
import { SignupsPanel } from "../components/dashboard/SignupsPanel";
import { AdminTierBadge } from "../components/AdminTierBadge";
import { RoleBadge } from "../components/RoleBadge";
import { StatusBadge } from "../components/StatusBadge";
import type { Role, Status, Tier } from "../api/types";
import styles from "../components/admin.module.css";

/** True for a bad-range `1001` — surfaced on the range control, not the panels. */
function isBadRange(error: unknown): boolean {
  return isApiError(error) && error.code === ErrorCodes.ValidationFailed;
}

/**
 * /admin/dashboard — the metrics surface. A range control (presets + bucket)
 * drives `GET /admin/dashboard`; renders the KPI row, the three distribution
 * panels (tier/role/status → RankedBarChart + paired table), and signups over
 * time. Every figure is account metadata — NO ledger data (R10). Loading →
 * skeletons; zero-state → `0`/EmptyState; error → ErrorState + retry.
 */
export function AdminDashboardPage() {
  const { t } = useT();
  const [range, setRange] = useState<RangeValue>(DEFAULT_RANGE);

  const invalidRange = isCustomRangeInvalid(range);
  const request = rangeToRequest(range);
  const query = useMetricsQuery(request, !invalidRange);

  const apiRangeMessage =
    isBadRange(query.error) && query.error
      ? resolveErrorMessage(query.error, t)
      : undefined;
  const panelError =
    query.error && !isBadRange(query.error)
      ? resolveErrorMessage(query.error, t)
      : undefined;

  const data = query.data;
  const loading = query.isPending && !invalidRange;

  return (
    <Stack gap="5">
      <AdminRangeControl
        value={range}
        onChange={setRange}
        apiError={apiRangeMessage}
      />

      {panelError ? (
        <ErrorState
          title={t("admin:dashboard.states.error")}
          description={panelError}
          action={
            <Button variant="secondary" onClick={() => void query.refetch()}>
              {t("admin:dashboard.states.retry")}
            </Button>
          }
        />
      ) : (
        <>
          <MetricsKpiRow data={data} loading={loading} />

          {data ? (
            <>
              <div className={styles.dashGrid}>
                <DistributionPanel
                  title={t("admin:dashboard.distributions.tier")}
                  ariaLabel={t("admin:dashboard.distributions.tierChartLabel")}
                  items={data.tierDistribution}
                  total={data.totalUsers}
                  renderLabel={(key) => <AdminTierBadge tier={key as Tier} />}
                />
                <DistributionPanel
                  title={t("admin:dashboard.distributions.role")}
                  ariaLabel={t("admin:dashboard.distributions.roleChartLabel")}
                  items={data.roleDistribution}
                  total={data.totalUsers}
                  renderLabel={(key) => <RoleBadge role={key as Role} />}
                />
                <DistributionPanel
                  title={t("admin:dashboard.distributions.status")}
                  ariaLabel={t("admin:dashboard.distributions.statusChartLabel")}
                  items={data.statusDistribution}
                  total={data.totalUsers}
                  renderLabel={(key) => <StatusBadge status={key as Status} />}
                />
              </div>

              <SignupsPanel signups={data.signups} />
            </>
          ) : null}
        </>
      )}
    </Stack>
  );
}
