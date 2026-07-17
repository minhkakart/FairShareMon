import { useT } from "@/i18n/useT";
import { useRevokeTokens } from "../../hooks/useAdminUsers";
import { ConfirmActionDialog } from "./ConfirmActionDialog";
import type { UserActionDialogProps } from "./DisableUserDialog";

/** Revoke all of a user's sessions (danger): forces re-login everywhere. Guards
 *  14001/14002 surface inline. */
export function RevokeTokensDialog({
  user,
  open,
  onOpenChange,
}: UserActionDialogProps) {
  const { t } = useT();
  const revoke = useRevokeTokens(user.uuid);
  return (
    <ConfirmActionDialog
      open={open}
      onOpenChange={onOpenChange}
      tone="danger"
      title={t("admin:actions.revokeTokens.title")}
      description={t("admin:actions.revokeTokens.body")}
      confirmLabel={t("admin:actions.revokeTokens.submit")}
      confirmVariant="danger"
      successToast={t("admin:actions.revokeTokens.toast", {
        name: user.username,
      })}
      run={() => revoke.mutateAsync()}
    />
  );
}
