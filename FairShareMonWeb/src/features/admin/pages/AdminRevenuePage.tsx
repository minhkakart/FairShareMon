import { useState } from "react";
import { Button, ErrorState, Stack } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { DEFAULT_RANGE, isCustomRangeInvalid, rangeToRequest } from "../dateRange";
import type { RangeValue } from "../dateRange";
import { useRevenueQuery } from "../hooks/useAdminDashboard";
import { AdminRangeControl } from "../components/dashboard/AdminRangeControl";
import { RevenueKpiRow } from "../components/dashboard/RevenueKpiRow";
import { RevenueChart } from "../components/dashboard/RevenueChart";
import { ReferencesList } from "../components/dashboard/ReferencesList";

function isBadRange(error: unknown): boolean {
  return isApiError(error) && error.code === ErrorCodes.ValidationFailed;
}

/**
 * /admin/revenue — the revenue surface. The same range control drives
 * `GET /admin/revenue`; renders total-revenue + grant-count KPIs, the revenue
 * over-time chart + paired money table, and the payment references list. Revenue
 * = API-computed SUM of GRANT rows, rendered verbatim via `<Money>` (R3, R10).
 */
export function AdminRevenuePage() {
  const { t } = useT();
  const [range, setRange] = useState<RangeValue>(DEFAULT_RANGE);

  const invalidRange = isCustomRangeInvalid(range);
  const request = rangeToRequest(range);
  const query = useRevenueQuery(request, !invalidRange);

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
          title={t("admin:revenue.states.error")}
          description={panelError}
          action={
            <Button variant="secondary" onClick={() => void query.refetch()}>
              {t("admin:revenue.states.retry")}
            </Button>
          }
        />
      ) : (
        <>
          <RevenueKpiRow data={data} loading={loading} />
          {data ? (
            <>
              <RevenueChart data={data} />
              <ReferencesList references={data.references} />
            </>
          ) : null}
        </>
      )}
    </Stack>
  );
}
