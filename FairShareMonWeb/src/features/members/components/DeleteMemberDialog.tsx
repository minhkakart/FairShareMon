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
import { useDeleteMember } from "../hooks/useMembers";
import type { MemberResponse } from "../api/types";

export type DeleteMemberDialogProps = {
  /** The member to soft-delete (null → dialog closed). */
  member: MemberResponse | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * Confirm a soft-delete (OQ4a). The body explains history is preserved (R7).
 * Owner-rep never reaches here (no delete control is rendered); a defensive
 * `3001` and a stale `3000` both surface as a toast and close.
 */
export function DeleteMemberDialog({
  member,
  open,
  onOpenChange,
}: DeleteMemberDialogProps) {
  const { t } = useT();
  const toast = useToast();
  const deleteMember = useDeleteMember();

  async function onConfirm() {
    if (!member) return;
    try {
      await deleteMember.mutateAsync(member.uuid);
      toast.push({ tone: "success", title: t("members:toast.deleted") });
    } catch (error) {
      // 3000 (stale list) / 3001 (defensive owner-rep) surface localized server
      // text; client-synthetic network/unexpected states fall back to i18n copy.
      toast.push({
        tone: "danger",
        title: resolveErrorMessage(error, t),
      });
    } finally {
      onOpenChange(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        title={t("members:delete.title", { name: member?.name ?? "" })}
        description={t("members:delete.body")}
        size="sm"
        closeLabel={t("members:delete.cancel")}
      >
        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              {t("members:delete.cancel")}
            </Button>
          </DialogClose>
          <Button
            type="button"
            variant="danger"
            loading={deleteMember.isPending}
            onClick={() => void onConfirm()}
          >
            {t("members:delete.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
