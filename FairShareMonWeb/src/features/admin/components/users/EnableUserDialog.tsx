import { useT } from "@/i18n/useT";
import { useEnableUser } from "../../hooks/useAdminUsers";
import { ConfirmActionDialog } from "./ConfirmActionDialog";
import type { UserActionDialogProps } from "./DisableUserDialog";

/** Re-enable a disabled account (routine confirm). */
export function EnableUserDialog({
  user,
  open,
  onOpenChange,
}: UserActionDialogProps) {
  const { t } = useT();
  const enable = useEnableUser(user.uuid);
  return (
    <ConfirmActionDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t("admin:actions.enable.title")}
      description={t("admin:actions.enable.body")}
      confirmLabel={t("admin:actions.enable.submit")}
      successToast={t("admin:actions.enable.toast", { name: user.username })}
      run={() => enable.mutateAsync()}
    />
  );
}
