import { useState } from "react";
import { Link } from "react-router-dom";
import {
  Alert,
  Button,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorState,
  Money,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { formatDateTime, formatMoneyVnd } from "@/i18n/format";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import {
  useExpensesQuery,
  useRemoveExpenseEvent,
} from "@/features/expenses/hooks/useExpenses";
import type { EventResponse } from "../api/types";
import { AssignExpenseDialog } from "./AssignExpenseDialog";
import { PlusIcon } from "./icons";
import styles from "./EventExpensesSection.module.css";

export type EventExpensesSectionProps = {
  event: EventResponse;
};

const COLUMN_COUNT = 5;

/**
 * The event's expenses (`GET /expenses?eventUuid=`) — a compact table with a
 * per-row remove-from-event action and a header "Gán phiếu" picker. All write
 * controls are open-only: when the event is closed they are hidden and a short
 * read-only note is shown (the M4 guard already locks the expenses themselves).
 */
export function EventExpensesSection({ event }: EventExpensesSectionProps) {
  const { t } = useT();
  const toast = useToast();
  const closed = event.isClosed;
  const [assignOpen, setAssignOpen] = useState(false);
  const removeEvent = useRemoveExpenseEvent();

  const expensesQuery = useExpensesQuery({ eventUuid: event.uuid });
  const expenses = expensesQuery.data ?? [];

  async function onRemove(uuid: string) {
    try {
      await removeEvent.mutateAsync(uuid);
      toast.push({ tone: "success", title: t("events:toast.removed") });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    }
  }

  return (
    <Card>
      <CardHeader
        title={t("events:expensesSection.title")}
        action={
          closed ? undefined : (
            <Button
              variant="secondary"
              size="sm"
              iconStart={<PlusIcon />}
              onClick={() => setAssignOpen(true)}
            >
              {t("events:expensesSection.assign")}
            </Button>
          )
        }
      />
      <CardBody>
        {closed ? (
          <Alert tone="info">{t("events:expensesSection.closedNote")}</Alert>
        ) : null}

        {expensesQuery.isError ? (
          <ErrorState
            title={t("events:expensesSection.errorTitle")}
            description={resolveErrorMessage(expensesQuery.error, t)}
            action={
              <Button
                variant="secondary"
                onClick={() => void expensesQuery.refetch()}
              >
                {t("events:list.retry")}
              </Button>
            }
          />
        ) : (
          <Table caption={t("events:expensesSection.caption")} captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell>
                  {t("events:expensesSection.name")}
                </TableHeaderCell>
                <TableHeaderCell>
                  {t("events:expensesSection.payer")}
                </TableHeaderCell>
                <TableHeaderCell numeric>
                  {t("events:expensesSection.total")}
                </TableHeaderCell>
                <TableHeaderCell>
                  {t("events:expensesSection.time")}
                </TableHeaderCell>
                <TableHeaderCell align="right">
                  {t("events:expensesSection.actions")}
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {expensesQuery.isPending ? (
                Array.from({ length: 3 }).map((_, index) => (
                  <TableRow key={index}>
                    <TableHeaderCell scope="row">
                      <Skeleton width="10rem" />
                    </TableHeaderCell>
                    <TableCell>
                      <Skeleton width="7rem" />
                    </TableCell>
                    <TableCell numeric>
                      <Skeleton width="6rem" />
                    </TableCell>
                    <TableCell>
                      <Skeleton width="8rem" />
                    </TableCell>
                    <TableCell actions>
                      <Skeleton width="3rem" />
                    </TableCell>
                  </TableRow>
                ))
              ) : expenses.length === 0 ? (
                <TableEmpty colSpan={COLUMN_COUNT}>
                  <EmptyState
                    title={t("events:expensesSection.emptyTitle")}
                    description={
                      closed
                        ? t("events:expensesSection.emptyClosedBody")
                        : t("events:expensesSection.emptyBody")
                    }
                  />
                </TableEmpty>
              ) : (
                expenses.map((expense) => (
                  <TableRow key={expense.uuid}>
                    <TableHeaderCell scope="row">
                      <Link
                        className={styles.nameLink}
                        to={`/expenses/${expense.uuid}`}
                      >
                        {expense.name}
                      </Link>
                    </TableHeaderCell>
                    <TableCell>
                      <span className={styles.inlineWrap}>
                        {expense.payer.name}
                        {expense.payer.isDeleted ? (
                          <span className={styles.deletedTag}>
                            {t("events:balance.deletedTag")}
                          </span>
                        ) : null}
                      </span>
                    </TableCell>
                    <TableCell numeric>
                      <Money amount={expense.total} format={formatMoneyVnd} />
                    </TableCell>
                    <TableCell>{formatDateTime(expense.expenseTime)}</TableCell>
                    <TableCell actions>
                      {closed ? (
                        <span className={styles.muted}>
                          {t("events:expensesSection.readOnly")}
                        </span>
                      ) : (
                        <Button
                          variant="ghost"
                          size="sm"
                          disabled={removeEvent.isPending}
                          onClick={() => void onRemove(expense.uuid)}
                          aria-label={t("events:expensesSection.removeNamed", {
                            name: expense.name,
                          })}
                        >
                          {t("events:expensesSection.remove")}
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        )}
      </CardBody>

      {assignOpen ? (
        <AssignExpenseDialog
          eventUuid={event.uuid}
          eventName={event.name}
          startDate={event.startDate}
          endDate={event.endDate}
          open={assignOpen}
          onOpenChange={setAssignOpen}
        />
      ) : null}
    </Card>
  );
}
