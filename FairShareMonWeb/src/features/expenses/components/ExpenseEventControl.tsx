import { useState } from "react";
import { Link } from "react-router-dom";
import { Badge, Button, Select } from "@/components/ui";
import type { SelectOption } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useEventsQuery } from "@/features/events/hooks/useEvents";
import type { ExpenseResponse } from "../api/types";
import { useAssignExpenseEvent, useRemoveExpenseEvent } from "../hooks/useExpenses";
import { LockIcon } from "./icons";
import styles from "./ExpenseEventControl.module.css";

export type ExpenseEventControlProps = {
  expense: ExpenseResponse;
};

/**
 * The expense-detail event control (OQ3a). Shows the current event (or "loose")
 * and lets the caller assign/move to an OPEN event (a `Select` of the caller's
 * open events) or remove from the event. Disabled when the expense's own owning
 * event is closed (can't move out of a closed source → defensive `9001`). Error
 * mapping: `9000` (stale target) / `9001` (closed source/target) → toast;
 * `9002` (out of range) / `9003` → inline message.
 */
export function ExpenseEventControl({ expense }: ExpenseEventControlProps) {
  const { t } = useT();
  const toast = useToast();
  const assign = useAssignExpenseEvent();
  const removeEvent = useRemoveExpenseEvent();
  const [inlineError, setInlineError] = useState<string | null>(null);

  const closed = expense.eventIsClosed === true;
  const eventsQuery = useEventsQuery({ closed: false });

  // Assignable targets are open events only, excluding the expense's current
  // event (a closed owning event renders the read-only branch below, so it never
  // needs to appear in this Select).
  const openEvents = (eventsQuery.data ?? []).filter(
    (e) => !e.isClosed && e.uuid !== expense.eventUuid,
  );
  const options: SelectOption[] = openEvents.map((e) => ({
    value: e.uuid,
    label: e.name,
  }));

  const busy = assign.isPending || removeEvent.isPending;

  async function onAssign(eventUuid: string) {
    setInlineError(null);
    try {
      await assign.mutateAsync({ uuid: expense.uuid, body: { eventUuid } });
      toast.push({ tone: "success", title: t("expenses:expenseEvent.assigned") });
    } catch (error) {
      handleError(error);
    }
  }

  async function onRemove() {
    setInlineError(null);
    try {
      await removeEvent.mutateAsync(expense.uuid);
      toast.push({ tone: "success", title: t("expenses:expenseEvent.removed") });
    } catch (error) {
      handleError(error);
    }
  }

  function handleError(error: unknown) {
    if (isApiError(error)) {
      if (
        error.code === ErrorCodes.ExpenseTimeOutOfEventRange ||
        error.code === ErrorCodes.EventRangeExcludesAssignedExpenses
      ) {
        setInlineError(error.message);
        return;
      }
      if (
        error.code === ErrorCodes.EventClosed ||
        error.code === ErrorCodes.EventNotFound
      ) {
        toast.push({ tone: "danger", title: error.message });
        return;
      }
    }
    setInlineError(resolveErrorMessage(error, t));
  }

  // A closed owning event is immutable — show the linkage read-only.
  if (closed) {
    return (
      <div className={styles.control}>
        <span className={styles.inlineWrap}>
          {expense.eventName ? (
            <Link className={styles.eventLink} to={`/events/${expense.eventUuid}`}>
              {expense.eventName}
            </Link>
          ) : null}
          <Badge tone="neutral" icon={<LockIcon />}>
            {t("expenses:badge.closed")}
          </Badge>
        </span>
        <p className={styles.note}>{t("expenses:expenseEvent.closedNote")}</p>
      </div>
    );
  }

  return (
    <div className={styles.control}>
      <div className={styles.current}>
        {expense.eventUuid ? (
          <Link className={styles.eventLink} to={`/events/${expense.eventUuid}`}>
            {expense.eventName ?? t("expenses:badge.event")}
          </Link>
        ) : (
          <span className={styles.muted}>
            {t("expenses:expenseEvent.loose")}
          </span>
        )}
        {expense.eventUuid ? (
          <Button
            variant="ghost"
            size="sm"
            disabled={busy}
            onClick={() => void onRemove()}
          >
            {t("expenses:expenseEvent.remove")}
          </Button>
        ) : null}
      </div>

      <Select
        label={
          expense.eventUuid
            ? t("expenses:expenseEvent.moveLabel")
            : t("expenses:expenseEvent.assignLabel")
        }
        value={undefined}
        onValueChange={(v) => void onAssign(v)}
        options={options}
        placeholder={
          options.length === 0
            ? t("expenses:expenseEvent.noOpenEvents")
            : t("expenses:expenseEvent.changePlaceholder")
        }
        disabled={busy || options.length === 0}
        error={inlineError ?? undefined}
      />
    </div>
  );
}
