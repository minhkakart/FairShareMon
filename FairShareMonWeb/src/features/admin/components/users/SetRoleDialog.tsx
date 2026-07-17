import { useT } from "@/i18n/useT";
import type { Role } from "../../api/types";
import { useSetRole } from "../../hooks/useAdminUsers";
import { ConfirmActionDialog } from "./ConfirmActionDialog";

export interface SetRoleDialogProps {
  user: { uuid: string; username: string };
  /** The role to set: `ADMIN` (promote, routine) or `USER` (demote, danger). */
  targetRole: Role;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * Promote (USER→ADMIN, routine) or demote (ADMIN→USER, danger). Demote-of-self /
 * other-admin is blocked upstream (the action bar); a stale last-admin `14002`
 * from the server still surfaces inline via `ConfirmActionDialog`.
 */
export function SetRoleDialog({
  user,
  targetRole,
  open,
  onOpenChange,
}: SetRoleDialogProps) {
  const { t } = useT();
  const setRole = useSetRole(user.uuid);
  const demote = targetRole === "USER";

  return (
    <ConfirmActionDialog
      open={open}
      onOpenChange={onOpenChange}
      tone={demote ? "danger" : "default"}
      title={t(demote ? "admin:actions.demote.title" : "admin:actions.promote.title")}
      description={t(
        demote ? "admin:actions.demote.body" : "admin:actions.promote.body",
      )}
      warning={
        demote
          ? {
              title: t("admin:actions.demote.warningTitle"),
              body: t("admin:actions.demote.warning"),
            }
          : undefined
      }
      confirmLabel={t(
        demote ? "admin:actions.demote.submit" : "admin:actions.promote.submit",
      )}
      confirmVariant={demote ? "danger" : "primary"}
      successToast={t(
        demote ? "admin:actions.demote.toast" : "admin:actions.promote.toast",
        { name: user.username },
      )}
      run={() => setRole.mutateAsync({ role: targetRole })}
    />
  );
}
