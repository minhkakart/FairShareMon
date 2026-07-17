import { Link } from "react-router-dom";
import {
  Badge,
  Button,
  CategoryMarker,
  Money,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { formatDateTime, formatMoneyVnd } from "@/i18n/format";
import type { ExpenseSummaryResponse } from "../api/types";
import { SettledToggle } from "./SettledToggle";
import { LockIcon } from "./icons";
import styles from "./ExpensesTable.module.css";

export type ExpensesTableProps = {
  expenses: ExpenseSummaryResponse[];
};

/**
 * The expense list in backend order (`expenseTime` DESC — never re-sorted). The
 * name links to the detail route; payer/category render verbatim (a soft-deleted
 * link shows a muted "(đã xóa)" treatment). Total is a `Money` numeric cell, the
 * settled state is the live `SettledToggle` (color-independent), and the event/
 * loose indicator is a `Badge`. Read-only presentational — the page owns data.
 */
export function ExpensesTable({ expenses }: ExpensesTableProps) {
  const { t } = useT();
  const deletedTag = t("expenses:badge.deletedTag");

  return (
    <Table caption={t("expenses:list.caption")} captionHidden>
      <TableHead>
        <TableRow>
          <TableHeaderCell>{t("expenses:list.name")}</TableHeaderCell>
          <TableHeaderCell>{t("expenses:list.payer")}</TableHeaderCell>
          <TableHeaderCell>{t("expenses:list.category")}</TableHeaderCell>
          <TableHeaderCell numeric>{t("expenses:list.total")}</TableHeaderCell>
          <TableHeaderCell>{t("expenses:list.time")}</TableHeaderCell>
          <TableHeaderCell>{t("expenses:list.settled")}</TableHeaderCell>
          <TableHeaderCell>{t("expenses:list.event")}</TableHeaderCell>
          <TableHeaderCell align="right">
            {t("expenses:list.actions")}
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {expenses.map((expense) => (
          <TableRow key={expense.uuid}>
            <TableHeaderCell scope="row">
              <Link className={styles.nameLink} to={`/expenses/${expense.uuid}`}>
                {expense.name}
              </Link>
            </TableHeaderCell>
            <TableCell>
              <span className={styles.inlineWrap}>
                {expense.payer.name}
                {expense.payer.isDeleted ? (
                  <span className={styles.deletedTag}>{deletedTag}</span>
                ) : null}
              </span>
            </TableCell>
            <TableCell>
              <span className={styles.inlineWrap}>
                <CategoryMarker
                  color={expense.category.color}
                  icon={expense.category.icon}
                  name={expense.category.name}
                  showLabel
                />
                {expense.category.isDeleted ? (
                  <span className={styles.deletedTag}>{deletedTag}</span>
                ) : null}
              </span>
            </TableCell>
            <TableCell numeric>
              <Money amount={expense.total} format={formatMoneyVnd} />
            </TableCell>
            <TableCell>{formatDateTime(expense.expenseTime)}</TableCell>
            <TableCell>
              <SettledToggle
                uuid={expense.uuid}
                isSettled={expense.isSettled}
                contextName={expense.name}
              />
            </TableCell>
            <TableCell>
              {expense.eventUuid ? (
                <span className={styles.inlineWrap}>
                  <Badge tone="neutral">
                    {expense.eventName ?? t("expenses:badge.event")}
                  </Badge>
                  {expense.eventIsClosed ? (
                    <Badge tone="neutral" icon={<LockIcon />}>
                      {t("expenses:badge.closed")}
                    </Badge>
                  ) : null}
                </span>
              ) : (
                <Badge tone="neutral">{t("expenses:badge.loose")}</Badge>
              )}
            </TableCell>
            <TableCell actions>
              <Button asChild variant="ghost" size="sm">
                <Link
                  to={`/expenses/${expense.uuid}`}
                  aria-label={t("expenses:list.viewNamed", {
                    name: expense.name,
                  })}
                >
                  {t("expenses:list.view")}
                </Link>
              </Button>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
