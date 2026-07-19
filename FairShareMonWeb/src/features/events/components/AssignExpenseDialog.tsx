import { useEffect, useState } from "react";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  EmptyState,
  ErrorState,
  FormError,
  Money,
  Skeleton,
  TextField,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { formatDateTime, formatMoneyVnd } from "@/i18n/format";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import {
  useAssignExpenseEvent,
  useExpensesQuery,
} from "@/features/expenses/hooks/useExpenses";
import { SearchIcon } from "./icons";
import styles from "./AssignExpenseDialog.module.css";

export type AssignExpenseDialogProps = {
  eventUuid: string;
  eventName: string;
  /** The event's date range (ISO) — seeds the eligible-expense query. */
  startDate: string;
  endDate: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * The assign-expense picker (OQ4a — ui-designer spec). Offers the caller's LOOSE
 * expenses whose `expenseTime` is within the event's date range (a searchable
 * single-select radio list); confirm → `PUT /expenses/:uuid/event`. The backend
 * is authoritative on validation: `9002` out-of-range → inline message; `9000`
 * (stale target) / `9001` (closed) → toast. Close-on-success; stay open on
 * transient. Rendered only while open (query runs on demand).
 */
export function AssignExpenseDialog({
  eventUuid,
  eventName,
  startDate,
  endDate,
  open,
  onOpenChange,
}: AssignExpenseDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const assign = useAssignExpenseEvent();
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<string | null>(null);
  const [inlineError, setInlineError] = useState<string | null>(null);

  const eligibleQuery = useExpensesQuery({
    looseOnly: true,
    from: startDate,
    to: endDate,
  });

  useEffect(() => {
    if (open) {
      setQuery("");
      setSelected(null);
      setInlineError(null);
    }
  }, [open]);

  const all = eligibleQuery.data ?? [];
  const search = query.trim().toLocaleLowerCase();
  const filtered = search
    ? all.filter((e) => e.name.toLocaleLowerCase().includes(search))
    : all;
  // A selection the search filter has hidden must not be confirmable — otherwise
  // "Gán" would assign an expense the caller can no longer see.
  const selectedVisible =
    selected !== null && filtered.some((e) => e.uuid === selected);

  async function onConfirm() {
    if (!selected) return;
    setInlineError(null);
    try {
      await assign.mutateAsync({ uuid: selected, body: { eventUuid } });
      toast.push({ tone: "success", title: t("events:toast.assigned") });
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error)) {
        if (error.code === ErrorCodes.ExpenseTimeOutOfEventRange) {
          setInlineError(error.message);
          return;
        }
        if (
          error.code === ErrorCodes.EventClosed ||
          error.code === ErrorCodes.EventNotFound ||
          error.code === ErrorCodes.ExpenseNotFound
        ) {
          toast.push({ tone: "danger", title: error.message });
          onOpenChange(false);
          return;
        }
      }
      setInlineError(resolveErrorMessage(error, t));
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        size="md"
        title={t("events:assign.title", { name: eventName })}
        description={t("events:assign.hintInRange")}
        closeLabel={t("events:assign.cancel")}
      >
        {inlineError ? <FormError>{inlineError}</FormError> : null}

        <TextField
          label={t("events:assign.searchLabel")}
          hideLabelVisually
          placeholder={t("events:assign.searchPlaceholder")}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          addonEnd={<SearchIcon />}
        />

        {eligibleQuery.isError ? (
          <ErrorState
            title={t("events:assign.errorTitle")}
            description={resolveErrorMessage(eligibleQuery.error, t)}
            action={
              <Button
                variant="secondary"
                onClick={() => void eligibleQuery.refetch()}
              >
                {t("events:list.retry")}
              </Button>
            }
          />
        ) : eligibleQuery.isPending ? (
          <div className={styles.pickerList} aria-hidden="true">
            {[0, 1, 2].map((i) => (
              <div key={i} className={styles.skeletonRow}>
                <Skeleton width="1.1rem" height="1.1rem" circle />
                <div className={styles.skeletonText}>
                  <Skeleton width="60%" height="0.9rem" />
                  <Skeleton width="35%" height="0.75rem" />
                </div>
                <Skeleton width="5rem" height="0.9rem" />
              </div>
            ))}
          </div>
        ) : filtered.length === 0 ? (
          <div className={styles.emptyPanel}>
            <EmptyState
              title={t("events:assign.emptyTitle")}
              description={
                all.length === 0
                  ? t("events:assign.emptyBody")
                  : t("events:assign.noMatchesBody")
              }
            />
          </div>
        ) : (
          <fieldset className={styles.fieldset}>
            <legend className={styles.srOnly}>
              {t("events:assign.legend")}
            </legend>
            <div className={styles.pickerList}>
              {filtered.map((expense) => (
                <label key={expense.uuid} className={styles.pickerRow}>
                  <input
                    type="radio"
                    name="assign-expense"
                    className={styles.radio}
                    value={expense.uuid}
                    checked={selected === expense.uuid}
                    onChange={() => setSelected(expense.uuid)}
                  />
                  <span className={styles.pickerText}>
                    <span className={styles.pickerName}>{expense.name}</span>
                    <span className={styles.pickerDate}>
                      {formatDateTime(expense.expenseTime)}
                    </span>
                  </span>
                  <Money
                    amount={expense.total}
                    format={formatMoneyVnd}
                    className={styles.pickerTotal}
                  />
                </label>
              ))}
            </div>
          </fieldset>
        )}

        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("events:assign.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="primary"
            disabled={!selectedVisible}
            loading={assign.isPending}
            onClick={() => void onConfirm()}
          >
            {t("events:assign.confirm")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
