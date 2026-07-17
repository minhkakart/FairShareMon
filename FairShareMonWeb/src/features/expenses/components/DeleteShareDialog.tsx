import { useEffect, useState } from "react";
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
import type { ShareResponse } from "../api/types";
import { useDeleteShare } from "../hooks/useExpenses";

export type DeleteShareDialogProps = {
  expenseUuid: string;
  /** The share to remove (null → closed). */
  share: ShareResponse | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/** Terminal codes that close the dialog (stale share / owner-rep / closed event). */
const TERMINAL_CODES: readonly number[] = [
  ErrorCodes.ShareNotFound,
  ErrorCodes.OwnerRepresentativeShareNotDeletable,
  ErrorCodes.EventClosed,
];

/**
 * Confirm removing a share (B4). Close-on-error per OQ12a: close on success and
 * on terminal codes (7000 stale / 7002 owner-rep / 9001 closed event); keep the
 * dialog open with an inline error on network/transient failures for in-place
 * retry. The owner-rep share has no delete control (this is defensive).
 */
export function DeleteShareDialog({
  expenseUuid,
  share,
  open,
  onOpenChange,
}: DeleteShareDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const deleteShare = useDeleteShare();
  const [inlineError, setInlineError] = useState<string | null>(null);

  useEffect(() => {
    if (open) setInlineError(null);
  }, [open]);

  async function onConfirm() {
    if (!share) return;
    setInlineError(null);
    try {
      await deleteShare.mutateAsync({ uuid: expenseUuid, shareUuid: share.uuid });
      toast.push({ tone: "success", title: t("expenses:toast.shareDeleted") });
      onOpenChange(false);
    } catch (error) {
      if (isApiError(error) && TERMINAL_CODES.includes(error.code)) {
        toast.push({ tone: "danger", title: error.message });
        onOpenChange(false);
        return;
      }
      // Network / transient: keep open for an in-place retry.
      setInlineError(resolveErrorMessage(error, t));
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("expenses:deleteShare.title", {
          name: share?.member.name ?? "",
        })}
        description={t("expenses:deleteShare.body")}
        size="sm"
        closeLabel={t("expenses:deleteShare.cancel")}
      >
        {inlineError ? <FormError>{inlineError}</FormError> : null}
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("expenses:deleteShare.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            loading={deleteShare.isPending}
            onClick={() => void onConfirm()}
          >
            {t("expenses:deleteShare.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
