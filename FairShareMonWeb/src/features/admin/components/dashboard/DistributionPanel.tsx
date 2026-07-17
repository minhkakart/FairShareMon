import type { ReactNode } from "react";
import {
  Card,
  CardBody,
  RankedBarChart,
  Table,
  TableBody,
  TableCell,
  TableFoot,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import type { RankedBarItem } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount } from "@/i18n/format";
import type { MetricCount } from "../../api/types";
import styles from "../admin.module.css";

export interface DistributionPanelProps {
  title: string;
  /** Summarizing label for the chart's `role="img"` region. */
  ariaLabel: string;
  items: MetricCount[];
  /** Denominator for the % share (total users). */
  total: number;
  /** Renders the identity label for a distribution key (a `TierBadge`/`RoleBadge`/
   *  `StatusBadge`), so identity never rests on the bar color. */
  renderLabel: (key: string) => ReactNode;
}

/**
 * A distribution panel (OQ6a): a `RankedBarChart` above its paired accessible
 * table. The label slot carries a badge so identity + state never rest on the bar
 * fill; the bar fill is decorative (`--fs-viz-cat-*` by rank). % share is a
 * display-only ratio off the integer counts.
 */
export function DistributionPanel({
  title,
  ariaLabel,
  items,
  total,
  renderLabel,
}: DistributionPanelProps) {
  const { t } = useT();
  const max = items.length > 0 ? Math.max(...items.map((d) => d.count)) : 0;
  const sharePct = (count: number) =>
    total > 0 ? Math.round((count / total) * 100) : 0;

  const barItems: RankedBarItem[] = items.map((d) => ({
    key: d.key,
    label: renderLabel(d.key),
    value: formatCount(d.count),
    ratio: max > 0 ? d.count / max : 0,
    meta: `${sharePct(d.count)}%`,
  }));

  return (
    <Card>
      <CardBody>
        <div className={styles.panelStack}>
          <h3 className={styles.panelTitle}>{title}</h3>
          <RankedBarChart items={barItems} ariaLabel={ariaLabel} compact />
          <Table caption={title} captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">
                  {t("admin:dashboard.distributions.group")}
                </TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  {t("admin:dashboard.distributions.count")}
                </TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  {t("admin:dashboard.distributions.share")}
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((d) => (
                <TableRow key={d.key}>
                  <TableHeaderCell scope="row">
                    {renderLabel(d.key)}
                  </TableHeaderCell>
                  <TableCell numeric>{formatCount(d.count)}</TableCell>
                  <TableCell numeric>{sharePct(d.count)}%</TableCell>
                </TableRow>
              ))}
            </TableBody>
            <TableFoot>
              <TableRow total>
                <TableHeaderCell scope="row">
                  {t("admin:dashboard.distributions.total")}
                </TableHeaderCell>
                <TableCell numeric>{formatCount(total)}</TableCell>
                <TableCell numeric>100%</TableCell>
              </TableRow>
            </TableFoot>
          </Table>
        </div>
      </CardBody>
    </Card>
  );
}
