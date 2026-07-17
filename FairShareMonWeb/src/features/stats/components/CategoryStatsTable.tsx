import {
  CategoryMarker,
  Money,
  Table,
  TableBody,
  TableCell,
  TableFoot,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatCount } from "@/i18n/format";
import type { CategoryStatRow } from "../api/types";
import styles from "./stats.module.css";

export interface CategoryStatsTableProps {
  /** Rows in the API's total-DESC order — rendered verbatim. */
  rows: CategoryStatRow[];
  /** Authoritative overview total for the % share denominator + footer echo. */
  overviewTotal: number;
  /** Authoritative overview expense count for the footer (never client-summed). */
  overviewCount?: number;
}

/**
 * The always-present accessible data channel paired with `CategoryBarChart`.
 * Caption + four columns: Danh mục (row header — marker + name, `(đã xóa)` when
 * deleted) · Tổng (`<Money>`) · Số phiếu (count) · Tỷ trọng (% share, a
 * display-only ratio). The footer echoes the authoritative overview total +
 * count verbatim — never a client sum (R3).
 */
export function CategoryStatsTable({
  rows,
  overviewTotal,
  overviewCount,
}: CategoryStatsTableProps) {
  const { t } = useT();

  return (
    <Table caption={t("stats:byCategory.table.caption")} captionHidden>
      <TableHead>
        <TableRow>
          <TableHeaderCell scope="col">
            {t("stats:byCategory.table.category")}
          </TableHeaderCell>
          <TableHeaderCell scope="col" numeric>
            {t("stats:byCategory.table.total")}
          </TableHeaderCell>
          <TableHeaderCell scope="col" numeric>
            {t("stats:byCategory.table.count")}
          </TableHeaderCell>
          <TableHeaderCell scope="col" numeric>
            {t("stats:byCategory.table.share")}
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((row) => {
          const sharePct =
            overviewTotal > 0
              ? Math.round((row.total / overviewTotal) * 100)
              : 0;
          return (
            <TableRow key={row.categoryUuid} deleted={row.isDeleted}>
              <TableHeaderCell scope="row">
                <span className={styles.barLabel}>
                  <CategoryMarker
                    color={row.color}
                    icon={row.icon}
                    name={row.categoryName}
                    showLabel
                    size="sm"
                  />
                  {row.isDeleted ? (
                    <span className={styles.barDeletedTag}>
                      {t("stats:byCategory.deleted")}
                    </span>
                  ) : null}
                </span>
              </TableHeaderCell>
              <TableCell numeric>
                <Money amount={row.total} />
              </TableCell>
              <TableCell numeric>
                <span className={styles.shareCell}>
                  {formatCount(row.expenseCount)}
                </span>
              </TableCell>
              <TableCell numeric>
                <span className={styles.shareCell}>{sharePct}%</span>
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
      <TableFoot>
        <TableRow total>
          <TableHeaderCell scope="row">
            {t("stats:byCategory.table.totalRow")}
          </TableHeaderCell>
          <TableCell numeric>
            <Money amount={overviewTotal} />
          </TableCell>
          <TableCell numeric>
            <span className={styles.shareCell}>
              {overviewCount === undefined ? "—" : formatCount(overviewCount)}
            </span>
          </TableCell>
          <TableCell numeric>
            <span className={styles.shareCell}>100%</span>
          </TableCell>
        </TableRow>
      </TableFoot>
    </Table>
  );
}
