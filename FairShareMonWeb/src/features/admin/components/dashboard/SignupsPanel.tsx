import {
  Card,
  CardBody,
  EmptyState,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  TimeSeriesBarChart,
} from "@/components/ui";
import type { TimeSeriesBarItem } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount } from "@/i18n/format";
import type { PeriodMetric } from "../../api/types";
import styles from "../admin.module.css";

export interface SignupsPanelProps {
  signups: PeriodMetric[];
}

/**
 * Signups over time: a `TimeSeriesBarChart` (columns per bucket) paired with an
 * accessible table. Empty (no signups in range) → an `EmptyState`, not an error.
 */
export function SignupsPanel({ signups }: SignupsPanelProps) {
  const { t } = useT();
  const max = signups.length > 0 ? Math.max(...signups.map((s) => s.count)) : 0;

  const items: TimeSeriesBarItem[] = signups.map((s) => ({
    key: s.periodLabel,
    periodLabel: s.periodLabel,
    ratio: max > 0 ? s.count / max : 0,
    value: formatCount(s.count),
    title: `${s.periodLabel}: ${formatCount(s.count)}`,
  }));

  return (
    <Card>
      <CardBody>
        <h3 className={styles.panelTitle}>{t("admin:dashboard.signups.title")}</h3>
        {signups.length === 0 ? (
          <EmptyState title={t("admin:dashboard.signups.empty")} />
        ) : (
          <>
            <TimeSeriesBarChart
              items={items}
              ariaLabel={t("admin:dashboard.signups.chartLabel")}
              showValues={signups.length <= 12}
            />
            <p className={styles.subhead}>
              {t("admin:dashboard.signups.tableCaption")}
            </p>
            <Table
              caption={t("admin:dashboard.signups.tableCaption")}
              captionHidden
            >
              <TableHead>
                <TableRow>
                  <TableHeaderCell scope="col">
                    {t("admin:dashboard.signups.period")}
                  </TableHeaderCell>
                  <TableHeaderCell scope="col" numeric>
                    {t("admin:dashboard.signups.count")}
                  </TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {signups.map((s) => (
                  <TableRow key={s.periodLabel}>
                    <TableHeaderCell scope="row">
                      {s.periodLabel}
                    </TableHeaderCell>
                    <TableCell numeric>{formatCount(s.count)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </>
        )}
      </CardBody>
    </Card>
  );
}
