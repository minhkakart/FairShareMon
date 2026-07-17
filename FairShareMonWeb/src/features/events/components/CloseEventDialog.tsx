import { useEffect, useState } from "react";
import {
  Alert,
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
import { useCloseEvent } from "../hooks/useEvents";
import { LockIcon } from "./icons";
import styles from "./CloseEventDialog.module.css";

export type CloseEventDialogProps = {
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
 * The one-way close confirm (ui-designer danger treatment). Emphatic,
 * irreversible copy + a warning `Alert` describing the lock consequences + a
 * MANDATORY acknowledgment checkbox gating the danger button. Close-on-error per
 * OQ7a: close on success and terminal codes (9000 / 9001 — a re-close on an
 * already-closed event is treated as done); stay open with an inline error on
 * network/transient for in-place retry. On success → status flips to closed and
 * the M4 expense write-guard lights up (caches invalidated in the hook).
 */
export function CloseEventDialog({
  uuid,
  name,
  open,
  onOpenChange,
}: CloseEventDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const closeEvent = useCloseEvent();
  const [ack, setAck] = useState(false);
  const [inlineError, setInlineError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setAck(false);
      setInlineError(null);
    }
  }, [open]);

  async function onConfirm() {
    setInlineError(null);
    try {
      await closeEvent.mutateAsync(uuid);
      toast.push({ tone: "success", title: t("events:close.toast") });
      onOpenChange(false);
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
        tone="danger"
        size="sm"
        title={t("events:close.title", { name })}
        description={t("events:close.subtitle")}
        closeLabel={t("events:close.cancel")}
      >
        <Alert tone="warning" title={t("events:close.warningTitle")}>
          {t("events:close.body")}
        </Alert>

        {inlineError ? <FormError>{inlineError}</FormError> : null}

        <label className={styles.ack}>
          <input
            type="checkbox"
            className={styles.ackBox}
            checked={ack}
            onChange={(e) => setAck(e.target.checked)}
          />
          <span>{t("events:close.acknowledge")}</span>
        </label>

        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("events:close.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            iconStart={<LockIcon />}
            disabled={!ack}
            loading={closeEvent.isPending}
            onClick={() => void onConfirm()}
          >
            {t("events:close.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
