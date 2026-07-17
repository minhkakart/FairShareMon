import { useT } from "@/i18n/useT";
import { useDisableUser } from "../../hooks/useAdminUsers";
import { ConfirmActionDialog } from "./ConfirmActionDialog";

export interface UserActionDialogProps {
  user: { uuid: string; username: string };
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/** Disable an account (danger): kills sessions + blocks login (14003). Guards
 *  14001/14002 surface inline. */
export function DisableUserDialog({
  user,
  open,
  onOpenChange,
}: UserActionDialogProps) {
  const { t } = useT();
  const disable = useDisableUser(user.uuid);
  return (
    <ConfirmActionDialog
      open={open}
      onOpenChange={onOpenChange}
      tone="danger"
      title={t("admin:actions.disable.title")}
      description={t("admin:actions.disable.body")}
      warning={{
        title: t("admin:actions.disable.warningTitle"),
        body: t("admin:actions.disable.warning"),
      }}
      confirmLabel={t("admin:actions.disable.submit")}
      confirmVariant="danger"
      successToast={t("admin:actions.disable.toast", { name: user.username })}
      run={() => disable.mutateAsync()}
    />
  );
}
