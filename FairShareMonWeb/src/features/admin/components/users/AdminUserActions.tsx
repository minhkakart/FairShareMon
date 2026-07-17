import { useState } from "react";
import type { ReactNode } from "react";
import { Button } from "@/components/ui";
import type { ButtonVariant } from "@/components/ui";
import { useT } from "@/i18n/useT";
import { useCurrentUser } from "@/features/auth/hooks/useAuth";
import type { AdminUserDetailResponse } from "../../api/types";
import { TierGrantDialog } from "./TierGrantDialog";
import { TierRevokeDialog } from "./TierRevokeDialog";
import { DisableUserDialog } from "./DisableUserDialog";
import { EnableUserDialog } from "./EnableUserDialog";
import { RevokeTokensDialog } from "./RevokeTokensDialog";
import { ResetPasswordDialog } from "./ResetPasswordDialog";
import { SetRoleDialog } from "./SetRoleDialog";
import styles from "../admin.module.css";

type ActionKey =
  | "grant"
  | "revoke"
  | "disable"
  | "enable"
  | "revokeTokens"
  | "reset"
  | "promote"
  | "demote"
  | null;

/**
 * The user-detail action bar. Computes the client-side guard (OQ R-Guards14xxx):
 * the destructive actions (disable, revoke-tokens, reset-password, demote) are
 * DISABLED with an explanatory tooltip when the target is self (14001) or another
 * ADMIN (14002); tier grant/revoke + promote are always enabled. The dialogs
 * still branch on 14001/14002 if the server rejects a stale action.
 */
export function AdminUserActions({ user }: { user: AdminUserDetailResponse }) {
  const { t } = useT();
  const me = useCurrentUser();
  const [openDialog, setOpenDialog] = useState<ActionKey>(null);

  const isSelf = me?.uuid === user.uuid;
  const isAdminTarget = user.role === "ADMIN";
  const guarded = isSelf || isAdminTarget;
  const guardReason = isSelf
    ? t("admin:actions.guard.self")
    : isAdminTarget
      ? t("admin:actions.guard.admin")
      : undefined;

  const dialogUser = { uuid: user.uuid, username: user.username };
  const close = () => setOpenDialog(null);

  return (
    <>
      <div className={styles.actionBar}>
        {/* Tier — always enabled. */}
        <Button variant="premium" size="sm" onClick={() => setOpenDialog("grant")}>
          {t("admin:actions.grant.button")}
        </Button>
        {user.tier === "PREMIUM" ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setOpenDialog("revoke")}
          >
            {t("admin:actions.revoke.button")}
          </Button>
        ) : null}

        {/* Status — enable is routine; disable is guarded. */}
        {user.status === "DISABLED" ? (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setOpenDialog("enable")}
          >
            {t("admin:actions.enable.button")}
          </Button>
        ) : (
          <GuardedButton
            guarded={guarded}
            reason={guardReason}
            variant="danger"
            onClick={() => setOpenDialog("disable")}
          >
            {t("admin:actions.disable.button")}
          </GuardedButton>
        )}

        {/* Sessions — guarded. */}
        <GuardedButton
          guarded={guarded}
          reason={guardReason}
          variant="danger"
          onClick={() => setOpenDialog("revokeTokens")}
        >
          {t("admin:actions.revokeTokens.button")}
        </GuardedButton>

        {/* Reset password — guarded, highest severity. */}
        <GuardedButton
          guarded={guarded}
          reason={guardReason}
          variant="danger"
          onClick={() => setOpenDialog("reset")}
        >
          {t("admin:resetPassword.button")}
        </GuardedButton>

        {/* Role — promote is routine; demote is guarded. */}
        {isAdminTarget ? (
          <GuardedButton
            guarded={guarded}
            reason={guardReason}
            variant="danger"
            onClick={() => setOpenDialog("demote")}
          >
            {t("admin:actions.demote.button")}
          </GuardedButton>
        ) : (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setOpenDialog("promote")}
          >
            {t("admin:actions.promote.button")}
          </Button>
        )}
      </div>

      <TierGrantDialog
        user={dialogUser}
        open={openDialog === "grant"}
        onOpenChange={(o) => (o ? setOpenDialog("grant") : close())}
      />
      <TierRevokeDialog
        user={dialogUser}
        open={openDialog === "revoke"}
        onOpenChange={(o) => (o ? setOpenDialog("revoke") : close())}
      />
      <DisableUserDialog
        user={dialogUser}
        open={openDialog === "disable"}
        onOpenChange={(o) => (o ? setOpenDialog("disable") : close())}
      />
      <EnableUserDialog
        user={dialogUser}
        open={openDialog === "enable"}
        onOpenChange={(o) => (o ? setOpenDialog("enable") : close())}
      />
      <RevokeTokensDialog
        user={dialogUser}
        open={openDialog === "revokeTokens"}
        onOpenChange={(o) => (o ? setOpenDialog("revokeTokens") : close())}
      />
      <ResetPasswordDialog
        user={dialogUser}
        open={openDialog === "reset"}
        onOpenChange={(o) => (o ? setOpenDialog("reset") : close())}
      />
      <SetRoleDialog
        user={dialogUser}
        targetRole="ADMIN"
        open={openDialog === "promote"}
        onOpenChange={(o) => (o ? setOpenDialog("promote") : close())}
      />
      <SetRoleDialog
        user={dialogUser}
        targetRole="USER"
        open={openDialog === "demote"}
        onOpenChange={(o) => (o ? setOpenDialog("demote") : close())}
      />
    </>
  );
}

/** A destructive action button that renders disabled + tooltip when guarded. The
 *  wrapping span carries the native tooltip so a disabled button still explains
 *  itself on hover. */
function GuardedButton({
  guarded,
  reason,
  variant,
  onClick,
  children,
}: {
  guarded: boolean;
  reason?: string;
  variant: ButtonVariant;
  onClick: () => void;
  children: ReactNode;
}) {
  if (guarded) {
    return (
      <span title={reason}>
        <Button variant={variant} size="sm" disabled aria-disabled="true">
          {children}
        </Button>
      </span>
    );
  }
  return (
    <Button variant={variant} size="sm" onClick={onClick}>
      {children}
    </Button>
  );
}
