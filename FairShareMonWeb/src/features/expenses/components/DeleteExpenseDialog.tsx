import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  FormError,
} from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useDeleteExpense } from "../hooks/useExpenses";

export type DeleteExpenseDialogProps = {
  uuid: string;
  name: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/** Terminal codes that close the dialog (stale expense / closed event). */
const TERMINAL_CODES: readonly number[] = [
  ErrorCodes.ExpenseNotFound,
  ErrorCodes.EventClosed,
];

/**
 * Confirm a hard-delete of the expense + cascade of its shares (B2). The body
 * states the change history is preserved (surviving audit). Close-on-error per
 * OQ12a: close on success and on terminal codes (6000 / 9001); keep open with an
 * inline error on network/transient failures. On success, return to the list.
 */
export function DeleteExpenseDialog({
  uuid,
  name,
  open,
  onOpenChange,
}: DeleteExpenseDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const navigate = useNavigate();
  const deleteExpense = useDeleteExpense();
  const [inlineError, setInlineError] = useState<string | null>(null);

  useEffect(() => {
    if (open) setInlineError(null);
  }, [open]);

  async function onConfirm() {
    setInlineError(null);
    try {
      await deleteExpense.mutateAsync(uuid);
      toast.push({ tone: "success", title: t("expenses:toast.deleted") });
      onOpenChange(false);
      void navigate("/expenses");
    } catch (error) {
      if (isApiError(error) && TERMINAL_CODES.includes(error.code)) {
        toast.push({ tone: "danger", title: error.message });
        onOpenChange(false);
        return;
      }
      setInlineError(resolveErrorMessage(error, t));
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("expenses:delete.title", { name })}
        description={t("expenses:delete.body")}
        size="sm"
        closeLabel={t("expenses:delete.cancel")}
      >
        {inlineError ? <FormError>{inlineError}</FormError> : null}
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("expenses:delete.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            loading={deleteExpense.isPending}
            onClick={() => void onConfirm()}
          >
            {t("expenses:delete.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
