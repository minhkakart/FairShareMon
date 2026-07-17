import { useT } from "@/i18n/useT";
import {
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
} from "@/components/ui";
import { useToast } from "@/app/ToastHost";
import { resolveErrorMessage } from "@/lib/api/http-error-handling";
import { useDeleteBankAccount } from "../hooks/useBankAccounts";
import type { BankAccountResponse } from "../api/types";

export type DeleteBankAccountDialogProps = {
  /** The account to delete (null → dialog closed). */
  account: BankAccountResponse | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * Confirm a hard-delete. The body explains the server's default-promotion:
 * deleting the default promotes the most-recently-added remaining account;
 * deleting the last leaves an empty wallet. `13003` (stale-tier gate) / `12000`
 * (stale row) both surface localized server text + close. Delete is a Premium
 * mutation.
 */
export function DeleteBankAccountDialog({
  account,
  open,
  onOpenChange,
}: DeleteBankAccountDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const deleteAccount = useDeleteBankAccount();

  async function onConfirm() {
    if (!account) return;
    try {
      await deleteAccount.mutateAsync(account.uuid);
      toast.push({ tone: "success", title: t("wallet:toast.deleted") });
    } catch (error) {
      toast.push({ tone: "danger", title: resolveErrorMessage(error, t) });
    } finally {
      onOpenChange(false);
    }
  }

  const body = account?.isDefault
    ? t("wallet:delete.bodyDefault")
    : t("wallet:delete.body");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("wallet:delete.title", { bank: account?.bankName ?? "" })}
        description={body}
        tone="danger"
        size="sm"
        closeLabel={t("wallet:delete.cancel")}
      >
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("wallet:delete.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            loading={deleteAccount.isPending}
            onClick={() => void onConfirm()}
          >
            {t("wallet:delete.confirm")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
