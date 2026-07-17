import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import {
  Alert,
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
} from "@/components/ui";
import type { ButtonVariant, DialogTone } from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { ErrorCodes, isApiError } from "@/lib/api/errors";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useT } from "@/i18n/useT";

export interface ConfirmActionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  tone?: DialogTone;
  title: ReactNode;
  description: ReactNode;
  /** Optional consequence callout shown above the footer. */
  warning?: { title: ReactNode; body: ReactNode };
  /** Extra body content (e.g. an optional note field) above the footer. */
  children?: ReactNode;
  confirmLabel: ReactNode;
  confirmVariant?: ButtonVariant;
  /** Localized success toast title. */
  successToast: ReactNode;
  /** The mutation call. Resolves on success; rejects with an `ApiError`. */
  run: () => Promise<unknown>;
}

/**
 * Shared confirm dialog for the routine + danger sensitive actions
 * (enable/disable/revoke-tokens/set-role). Guard rejections (14001 self / 14002
 * admin-target/last-admin) surface INLINE as a danger alert (the server's
 * verbatim message); any other error toasts + closes. Success toasts + closes.
 * Cache invalidation lives in the mutation hook.
 */
export function ConfirmActionDialog({
  open,
  onOpenChange,
  tone = "default",
  title,
  description,
  warning,
  children,
  confirmLabel,
  confirmVariant = "primary",
  successToast,
  run,
}: ConfirmActionDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const [running, setRunning] = useState(false);
  const [guardMessage, setGuardMessage] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setGuardMessage(null);
      setRunning(false);
    }
  }, [open]);

  async function onConfirm() {
    setGuardMessage(null);
    setRunning(true);
    try {
      await run();
      toast.push({ tone: "success", title: successToast });
      onOpenChange(false);
    } catch (error) {
      if (
        isApiError(error) &&
        (error.code === ErrorCodes.AdminCannotTargetSelf ||
          error.code === ErrorCodes.AdminCannotTargetAdmin)
      ) {
        // A stale guard (e.g. the target became an admin) — show it inline.
        setGuardMessage(error.message);
      } else {
        toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
        onOpenChange(false);
      }
    } finally {
      setRunning(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        tone={tone}
        title={title}
        description={description}
        size="sm"
        closeLabel={t("admin:actions.cancel")}
      >
        {warning ? (
          <Alert tone="warning" title={warning.title}>
            {warning.body}
          </Alert>
        ) : null}
        {children}
        {guardMessage ? <Alert tone="danger">{guardMessage}</Alert> : null}
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("admin:actions.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant={confirmVariant}
            loading={running}
            onClick={() => void onConfirm()}
          >
            {confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
