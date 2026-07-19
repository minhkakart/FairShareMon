import { useState } from "react";
import { Card, CardBody, PageHeader, Stack } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import type { AppTFunction } from "@/i18n/useT";
import {
  DEFAULT_RANGE,
  isCustomRangeIncomplete,
  isCustomRangeInvalid,
  presetToRequest,
} from "../dateRange";
import type { RangeValue } from "../dateRange";
import { useByCategoryQuery, useOverviewQuery } from "../hooks/useStats";
import { OverviewKpiRow } from "../components/OverviewKpiRow";
import { CategoryBreakdown } from "../components/CategoryBreakdown";
import { StatsRangeControl } from "../components/StatsRangeControl";
import styles from "../components/stats.module.css";

/** True for a bad-range `1001` — surfaced on the range control, not the panels. */
function isBadRange(error: unknown): boolean {
  return isApiError(error) && error.code === ErrorCodes.ValidationFailed;
}

/** A localized message for a non-`1001` error, or undefined. */
function panelError(error: unknown, t: AppTFunction): string | undefined {
  if (!error || isBadRange(error)) return undefined;
  return resolveErrorMessage(error, t);
}

/**
 * /stats — the statistics surface. A single date-range control (time-range lens,
 * OQ3a) drives both `GET /stats/overview` (KPI tiles) and `GET /stats/by-category`
 * (the ranked bar breakdown + its paired accessible table). Default range "This
 * month". An invalid custom range is prevented client-side (the query is disabled
 * and the control shows an inline message); a server-returned `1001` still
 * surfaces on the control.
 */
export function StatsPage() {
  const { t } = useT();
  const [range, setRange] = useState<RangeValue>(DEFAULT_RANGE);

  const invalidRange = isCustomRangeInvalid(range);
  // Disable while a custom range is inverted OR still missing a bound — an empty
  // bound resolves to the all-time key and would flash all-time figures.
  const queryEnabled = !invalidRange && !isCustomRangeIncomplete(range);
  const request = presetToRequest(range);

  const overviewQuery = useOverviewQuery(request, queryEnabled);
  const byCategoryQuery = useByCategoryQuery(request, queryEnabled);

  const overviewTotal = overviewQuery.data?.totalSpending ?? 0;
  const overviewCount = overviewQuery.data?.expenseCount;

  const badRangeError =
    (isBadRange(overviewQuery.error) && overviewQuery.error) ||
    (isBadRange(byCategoryQuery.error) && byCategoryQuery.error) ||
    null;
  const apiRangeMessage = badRangeError
    ? resolveErrorMessage(badRangeError, t)
    : undefined;

  return (
    <Stack gap="6">
      <PageHeader title={t("stats:page.title")} description={t("stats:page.subtitle")} />

      <StatsRangeControl
        value={range}
        onChange={setRange}
        apiError={apiRangeMessage}
      />

      <OverviewKpiRow
        data={overviewQuery.data}
        loading={overviewQuery.isPending && queryEnabled}
        error={panelError(overviewQuery.error, t)}
        onRetry={() => void overviewQuery.refetch()}
      />

      <section>
        <h2 className={styles.sectionTitle}>{t("stats:byCategory.title")}</h2>
        <Card>
          <CardBody>
            <CategoryBreakdown
              data={byCategoryQuery.data}
              overviewTotal={overviewTotal}
              overviewCount={overviewCount}
              loading={byCategoryQuery.isPending && queryEnabled}
              error={panelError(byCategoryQuery.error, t)}
              onRetry={() => void byCategoryQuery.refetch()}
            />
          </CardBody>
        </Card>
      </section>
    </Stack>
  );
}
