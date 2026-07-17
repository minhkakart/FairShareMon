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
import { useDeleteEvent } from "../hooks/useEvents";

export type DeleteEventDialogProps = {
  uuid: string;
  name: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/** Terminal codes that close the dialog (stale event / already closed). */
const TERMINAL_CODES: readonly number[] = [
  ErrorCodes.EventNotFound,
  ErrorCodes.EventClosed,
];

/**
 * Confirm a hard-delete of an OPEN event. The body explains the event's expenses
 * are NOT deleted — they become loose (SET NULL). Close-on-error per OQ7a: close
 * on success and on terminal codes (9000 stale / 9001 closed-guard); keep open
 * with an inline error on network/transient for in-place retry. On success →
 * return to the events list.
 */
export function DeleteEventDialog({
  uuid,
  name,
  open,
  onOpenChange,
}: DeleteEventDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const navigate = useNavigate();
  const deleteEvent = useDeleteEvent();
  const [inlineError, setInlineError] = useState<string | null>(null);

  useEffect(() => {
    if (open) setInlineError(null);
  }, [open]);

  async function onConfirm() {
    setInlineError(null);
    try {
      await deleteEvent.mutateAsync(uuid);
      toast.push({ tone: "success", title: t("events:toast.deleted") });
      onOpenChange(false);
      void navigate("/events");
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
        title={t("events:delete.title", { name })}
        description={t("events:delete.body")}
        size="sm"
        closeLabel={t("events:delete.cancel")}
      >
        {inlineError ? <FormError>{inlineError}</FormError> : null}
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("events:delete.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            loading={deleteEvent.isPending}
            onClick={() => void onConfirm()}
          >
            {t("events:delete.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
