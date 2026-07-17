import {
  Card,
  CardBody,
  EmptyState,
  Money,
  Table,
  TableBody,
  TableCell,
  TableFoot,
  TableHead,
  TableHeaderCell,
  TableRow,
  TimeSeriesBarChart,
} from "@/components/ui";
import type { TimeSeriesBarItem } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount, formatMoneyVnd } from "@/i18n/format";
import type { RevenueResponse } from "../../api/types";
import styles from "../admin.module.css";

export interface RevenueChartProps {
  data: RevenueResponse;
}

/**
 * Revenue over time: a `TimeSeriesBarChart` of per-bucket totals paired with an
 * accessible money table. All money renders via `<Money>` verbatim — the totals
 * come from the API (SUM of GRANT rows), never client-summed (R3). Empty → an
 * `EmptyState`, not an error.
 */
export function RevenueChart({ data }: RevenueChartProps) {
  const { t } = useT();
  const { buckets } = data;
  const max = buckets.length > 0 ? Math.max(...buckets.map((b) => b.total)) : 0;

  const items: TimeSeriesBarItem[] = buckets.map((b) => ({
    key: b.periodLabel,
    periodLabel: b.periodLabel,
    ratio: max > 0 ? b.total / max : 0,
    value: <Money amount={b.total} size="sm" />,
    title: `${b.periodLabel}: ${formatMoneyVnd(b.total)} · ${formatCount(b.grantCount)}`,
  }));

  return (
    <Card>
      <CardBody>
        <h3 className={styles.panelTitle}>{t("admin:revenue.chart.title")}</h3>
        {buckets.length === 0 ? (
          <EmptyState title={t("admin:revenue.references.empty")} />
        ) : (
          <>
            <TimeSeriesBarChart
              items={items}
              ariaLabel={t("admin:revenue.chart.chartLabel")}
              showValues={buckets.length <= 12}
            />
            <p className={styles.subhead}>
              {t("admin:revenue.chart.tableCaption")}
            </p>
            <Table
              caption={t("admin:revenue.chart.tableCaption")}
              captionHidden
            >
              <TableHead>
                <TableRow>
                  <TableHeaderCell scope="col">
                    {t("admin:revenue.chart.period")}
                  </TableHeaderCell>
                  <TableHeaderCell scope="col" numeric>
                    {t("admin:revenue.chart.revenue")}
                  </TableHeaderCell>
                  <TableHeaderCell scope="col" numeric>
                    {t("admin:revenue.chart.grantCount")}
                  </TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {buckets.map((b) => (
                  <TableRow key={b.periodLabel}>
                    <TableHeaderCell scope="row">
                      {b.periodLabel}
                    </TableHeaderCell>
                    <TableCell numeric>
                      <Money amount={b.total} />
                    </TableCell>
                    <TableCell numeric>{formatCount(b.grantCount)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
              <TableFoot>
                <TableRow total>
                  <TableHeaderCell scope="row">
                    {t("admin:revenue.chart.total")}
                  </TableHeaderCell>
                  <TableCell numeric>
                    <Money amount={data.totalRevenue} />
                  </TableCell>
                  <TableCell numeric>{formatCount(data.grantCount)}</TableCell>
                </TableRow>
              </TableFoot>
            </Table>
          </>
        )}
      </CardBody>
    </Card>
  );
}
